#!/usr/bin/env python3
"""
AudioLDM2 Server for SatieLang
This server provides an API for generating audio from text prompts using AudioLDM2.
"""

import os
import io
import json
import logging
import tempfile
from flask import Flask, request, jsonify, send_file
from flask_cors import CORS
import numpy as np
import soundfile as sf
import torch
from diffusers import AudioLDM2Pipeline

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app)  # Enable CORS for Unity integration

# Global model variable
model = None
device = None

def initialize_model():
    """Initialize the AudioLDM2 model."""
    global model, device

    logger.info("Initializing AudioLDM2 model...")

    # Determine device (GPU if available)
    if torch.cuda.is_available():
        device = torch.device("cuda")
    elif torch.backends.mps.is_available():
        device = torch.device("mps")
    else:
        device = torch.device("cpu")
    logger.info(f"Using device: {device}")

    # Load the model with specific configuration to avoid compatibility issues
    try:
        # Use float16 for CUDA, float32 for MPS and CPU (MPS doesn't support float16 well)
        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        model = AudioLDM2Pipeline.from_pretrained(
            "cvssp/audioldm2",
            torch_dtype=dtype,
            use_safetensors=True
        ).to(device)
    except Exception as e:
        logger.warning(f"Failed to load with safetensors: {e}")
        # Fallback to regular loading
        model = AudioLDM2Pipeline.from_pretrained(
            "cvssp/audioldm2",
            torch_dtype=dtype
        ).to(device)

    logger.info("Model loaded successfully!")

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    return jsonify({
        "status": "healthy",
        "model_loaded": model is not None,
        "device": str(device) if device else "not initialized"
    })

@app.route('/generate', methods=['POST'])
def generate_audio():
    """Generate audio from text prompt."""
    global model

    if model is None:
        return jsonify({"error": "Model not initialized"}), 503

    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        seed = data.get('seed', 0)
        sample_rate = data.get('sample_rate', 16000)
        num_inference_steps = data.get('num_inference_steps', 200)
        audio_length_in_s = data.get('audio_length_in_s', 10.0)

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        logger.info(f"Generating audio for prompt: '{prompt}' with seed: {seed}")

        # Set seed for reproducibility
        generator = torch.Generator(device=device).manual_seed(seed)

        # Generate audio with error handling for different model versions
        with torch.no_grad():
            try:
                output = model(
                    prompt,
                    num_inference_steps=num_inference_steps,
                    audio_length_in_s=audio_length_in_s,
                    generator=generator
                )
            except AttributeError as e:
                if '_get_initial_cache_position' in str(e):
                    # Try without some parameters for compatibility
                    logger.warning("Compatibility issue detected, trying simplified generation")
                    output = model(prompt, generator=generator)
                else:
                    raise e

        # Get the audio array
        audio_array = output.audios[0]

        # Ensure audio is in the correct shape
        if audio_array.ndim == 1:
            audio_data = audio_array
        else:
            audio_data = audio_array.squeeze()

        # Normalize audio to prevent clipping
        max_val = np.abs(audio_data).max()
        if max_val > 0:
            audio_data = audio_data / max_val * 0.95

        # Convert to the requested sample rate if needed
        if sample_rate != 16000:
            # AudioLDM2 generates at 16kHz by default
            # Resample if different rate is requested
            import librosa
            audio_data = librosa.resample(
                audio_data,
                orig_sr=16000,
                target_sr=sample_rate
            )

        # Create WAV file in memory
        wav_buffer = io.BytesIO()
        sf.write(wav_buffer, audio_data, sample_rate, format='WAV')
        wav_buffer.seek(0)

        logger.info(f"Audio generated successfully for prompt: '{prompt}'")

        # Return the WAV file
        return send_file(
            wav_buffer,
            mimetype='audio/wav',
            as_attachment=True,
            download_name=f'generated_{seed}.wav'
        )

    except Exception as e:
        logger.error(f"Error generating audio: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/generate_multiple', methods=['POST'])
def generate_multiple_audio():
    """Generate multiple audio variations from a single prompt."""
    global model

    if model is None:
        return jsonify({"error": "Model not initialized"}), 503

    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        num_options = data.get('num_options', 3)
        sample_rate = data.get('sample_rate', 16000)
        num_inference_steps = data.get('num_inference_steps', 200)
        audio_length_in_s = data.get('audio_length_in_s', 10.0)

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        logger.info(f"Generating {num_options} audio options for prompt: '{prompt}'")

        audio_files = []

        for i in range(num_options):
            # Use different seed for each variation
            seed = i
            generator = torch.Generator(device=device).manual_seed(seed)

            # Generate audio
            with torch.no_grad():
                output = model(
                    prompt,
                    num_inference_steps=num_inference_steps,
                    audio_length_in_s=audio_length_in_s,
                    generator=generator
                )

            # Get the audio array
            audio_array = output.audios[0]

            # Ensure audio is in the correct shape
            if audio_array.ndim == 1:
                audio_data = audio_array
            else:
                audio_data = audio_array.squeeze()

            # Normalize audio
            max_val = np.abs(audio_data).max()
            if max_val > 0:
                audio_data = audio_data / max_val * 0.95

            # Resample if needed
            if sample_rate != 16000:
                import librosa
                audio_data = librosa.resample(
                    audio_data,
                    orig_sr=16000,
                    target_sr=sample_rate
                )

            # Convert to WAV bytes
            wav_buffer = io.BytesIO()
            sf.write(wav_buffer, audio_data, sample_rate, format='WAV')
            wav_bytes = wav_buffer.getvalue()

            # Encode as base64 for JSON response
            import base64
            audio_base64 = base64.b64encode(wav_bytes).decode('utf-8')
            audio_files.append(audio_base64)

            logger.info(f"Generated option {i+1}/{num_options}")

        logger.info(f"All {num_options} audio options generated successfully")

        return jsonify({
            "prompt": prompt,
            "audio_files": audio_files,
            "sample_rate": sample_rate
        })

    except Exception as e:
        logger.error(f"Error generating multiple audio: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/', methods=['GET'])
def index():
    """Root endpoint with API information."""
    return jsonify({
        "name": "AudioLDM2 Server for SatieLang",
        "version": "1.0.0",
        "endpoints": {
            "/health": "Health check",
            "/generate": "Generate single audio from prompt",
            "/generate_multiple": "Generate multiple audio variations"
        }
    })

if __name__ == '__main__':
    # Initialize model on startup
    initialize_model()

    # Run the server
    port = int(os.environ.get('PORT', 5000))
    app.run(host='0.0.0.0', port=port, debug=False)