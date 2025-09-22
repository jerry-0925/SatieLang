#!/usr/bin/env python3
"""
Multi-Provider Audio Generation Server for SatieLang
Supports AudioLDM2 and ElevenLabs for generating audio from text prompts.
"""

import os
import io
import logging
import base64
from flask import Flask, request, jsonify, send_file
from flask_cors import CORS
import numpy as np
import soundfile as sf

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app)  # Enable CORS for Unity integration

# Global model variables
audioldm2_model = None
device = None

def initialize_audioldm2():
    """Initialize the AudioLDM2 model."""
    global audioldm2_model, device

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
        audioldm2_model = AudioLDM2Pipeline.from_pretrained(
            "cvssp/audioldm2",
            torch_dtype=dtype,
            use_safetensors=True
        ).to(device)
    except Exception as e:
        logger.warning(f"Failed to load with safetensors: {e}")
        # Fallback to regular loading
        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        audioldm2_model = AudioLDM2Pipeline.from_pretrained(
            "cvssp/audioldm2",
            torch_dtype=dtype
        ).to(device)

    logger.info("AudioLDM2 model loaded successfully!")
    return True

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    # Check if ElevenLabs API key is available
    elevenlabs_available = bool(os.environ.get('ELEVENLABS_API_KEY'))
    if not elevenlabs_available:
        # Try to check .env file
        try:
            with open('.env', 'r') as f:
                for line in f:
                    if line.startswith('ELEVENLABS_API_KEY='):
                        elevenlabs_available = True
                        break
        except:
            pass

    return jsonify({
        "status": "healthy",
        "providers": {
            "audioldm2": audioldm2_model is not None,
            "elevenlabs": elevenlabs_available
        },
        "device": str(device) if device else "not initialized"
    })

def generate_with_elevenlabs(prompt, seed, sample_rate, duration_seconds, prompt_influence, looping=False):
    """Generate audio using ElevenLabs API."""
    try:
        from elevenlabs import generate, set_api_key, Voice, VoiceSettings
        import json
        import platform

        # Get API key from environment variable
        api_key = os.environ.get('ELEVENLABS_API_KEY')

        # Try to read from Unity's API key storage
        if not api_key:
            unity_key_paths = []
            if platform.system() == 'Darwin':  # macOS
                unity_key_paths.append(os.path.expanduser('~/Library/Application Support/DefaultCompany/SatieLang/satie_api_keys.json'))
            elif platform.system() == 'Windows':
                unity_key_paths.append(os.path.expanduser('~/AppData/LocalLow/DefaultCompany/SatieLang/satie_api_keys.json'))
            elif platform.system() == 'Linux':
                unity_key_paths.append(os.path.expanduser('~/.config/unity3d/DefaultCompany/SatieLang/satie_api_keys.json'))

            for key_path in unity_key_paths:
                if os.path.exists(key_path):
                    try:
                        with open(key_path, 'r') as f:
                            data = json.load(f)
                            for key_config in data.get('keys', []):
                                if key_config.get('provider') == 1:  # ElevenLabs enum value
                                    encrypted_key = key_config.get('key', '')
                                    # Try to decode if it's base64 encoded (simple fallback)
                                    if encrypted_key.startswith('B64:'):
                                        import base64
                                        api_key = base64.b64decode(encrypted_key[4:]).decode('utf-8')
                                        logger.info("Found ElevenLabs API key from Unity storage")
                                    break
                    except Exception as e:
                        logger.warning(f"Failed to read Unity API keys: {e}")

        if not api_key:
            # Try to read from a local .env file as fallback
            try:
                with open('.env', 'r') as f:
                    for line in f:
                        if line.startswith('ELEVENLABS_API_KEY='):
                            api_key = line.strip().split('=', 1)[1].strip('"\'')
                            break
            except:
                pass

        if not api_key:
            raise Exception("ELEVENLABS_API_KEY not found. Please set it in Unity's API Key Manager (Window > Satie > API Key Manager)")

        set_api_key(api_key)

        logger.info(f"Generating audio with ElevenLabs: '{prompt}'")

        # Generate sound effect using ElevenLabs sound generation
        from elevenlabs.client import ElevenLabs
        client = ElevenLabs(api_key=api_key)

        # Generate sound effect
        result = client.text_to_sound_effects.convert(
            text=prompt,
            duration_seconds=duration_seconds,
            prompt_influence=prompt_influence
        )

        # The result is an iterator of audio chunks
        audio_chunks = []
        for chunk in result:
            audio_chunks.append(chunk)

        # Combine chunks into a single audio buffer
        audio_bytes = b''.join(audio_chunks)

        # Convert bytes to audio array
        audio_buffer = io.BytesIO(audio_bytes)
        audio_data, orig_sr = sf.read(audio_buffer)

        # Resample if needed
        if orig_sr != sample_rate:
            import librosa
            audio_data = librosa.resample(
                audio_data,
                orig_sr=orig_sr,
                target_sr=sample_rate
            )

        # Apply looping if requested
        if looping:
            # Simple crossfade for looping
            fade_duration = int(0.1 * sample_rate)  # 100ms fade
            if len(audio_data) > fade_duration * 2:
                # Create fade out at end
                audio_data[-fade_duration:] *= np.linspace(1, 0, fade_duration)
                # Create fade in at start
                audio_data[:fade_duration] *= np.linspace(0, 1, fade_duration)

        return audio_data

    except ImportError:
        raise Exception("elevenlabs package not installed. Run: pip install elevenlabs")
    except Exception as e:
        logger.error(f"ElevenLabs generation error: {str(e)}")
        raise

@app.route('/generate', methods=['POST'])
def generate_audio():
    """Generate audio from text prompt using the specified provider."""
    global audioldm2_model

    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        seed = data.get('seed', 0)
        sample_rate = data.get('sample_rate', 44100)
        provider = data.get('provider', 'elevenlabs').lower()

        # Provider-specific parameters
        num_inference_steps = data.get('num_inference_steps', 200)
        audio_length_in_s = data.get('audio_length_in_s', 10.0)
        duration_seconds = data.get('duration_seconds', 10.0)
        prompt_influence = data.get('prompt_influence', 0.3)
        looping = data.get('looping', False)

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        logger.info(f"Generating audio for prompt: '{prompt}' with provider: {provider}, seed: {seed}")

        # Generate audio based on provider
        if provider == 'audioldm2':
            if audioldm2_model is None:
                return jsonify({"error": "AudioLDM2 model not initialized"}), 503

            import torch
            # Set seed for reproducibility
            generator = torch.Generator(device=device).manual_seed(seed)

            # Generate audio with error handling for different model versions
            with torch.no_grad():
                try:
                    output = audioldm2_model(
                        prompt,
                        num_inference_steps=num_inference_steps,
                        audio_length_in_s=audio_length_in_s,
                        generator=generator
                    )
                except AttributeError as e:
                    if '_get_initial_cache_position' in str(e):
                        # Try without some parameters for compatibility
                        logger.warning("Compatibility issue detected, trying simplified generation")
                        output = audioldm2_model(prompt, generator=generator)
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
        elif provider == 'elevenlabs':
            audio_data = generate_with_elevenlabs(prompt, seed, sample_rate, duration_seconds, prompt_influence, looping)
        elif provider == 'test':
            # Generate a simple test tone
            duration = 2.0
            t = np.linspace(0, duration, int(sample_rate * duration))
            frequency = 440 + seed * 10  # A4 with slight variation based on seed
            audio_data = 0.5 * np.sin(2 * np.pi * frequency * t)
            logger.info(f"Generated test tone at {frequency}Hz")
        else:
            return jsonify({"error": f"Unknown provider: {provider}"}), 400

        # Create WAV file in memory
        wav_buffer = io.BytesIO()
        sf.write(wav_buffer, audio_data, sample_rate, format='WAV')
        wav_buffer.seek(0)

        logger.info(f"Audio generated successfully using {provider}")

        # Return the WAV file
        return send_file(
            wav_buffer,
            mimetype='audio/wav',
            as_attachment=True,
            download_name=f'generated_{provider}_{seed}.wav'
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
        "name": "Multi-Provider Audio Generation Server for SatieLang",
        "version": "2.0.0",
        "providers": ["audioldm2", "elevenlabs", "test"],
        "endpoints": {
            "/health": "Health check",
            "/generate": "Generate audio from prompt",
            "/generate_multiple": "Generate multiple audio variations"
        },
        "setup": {
            "elevenlabs": "Set ELEVENLABS_API_KEY environment variable or add to .env file",
            "audioldm2": "Will be initialized on first use (requires torch and diffusers)"
        }
    })

if __name__ == '__main__':
    # Try to initialize AudioLDM2 if available (optional)
    try:
        initialize_audioldm2()
    except:
        logger.info("AudioLDM2 initialization skipped (will initialize on demand)")

    # Run the server
    port = int(os.environ.get('PORT', 5001))
    logger.info(f"Starting server on port {port}")
    logger.info("For ElevenLabs support, set ELEVENLABS_API_KEY environment variable")
    app.run(host='0.0.0.0', port=port, debug=False)