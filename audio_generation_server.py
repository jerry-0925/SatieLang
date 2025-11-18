#!/usr/bin/env python3
"""
ElevenLabs Audio Generation Server for SatieLang
Generates audio from text prompts using ElevenLabs API.
"""

import os
import io
import json
import logging
import platform
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

def get_unity_api_key():
    """Read ElevenLabs API key from Unity's API key storage."""
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
                                api_key = base64.b64decode(encrypted_key[4:]).decode('utf-8')
                                logger.info("Found ElevenLabs API key from Unity storage")
                                return api_key
                            break
            except Exception as e:
                logger.warning(f"Failed to read Unity API keys: {e}")

    # Try to read from environment variable
    api_key = os.environ.get('ELEVENLABS_API_KEY')
    if api_key:
        logger.info("Found ElevenLabs API key from environment variable")
        return api_key

    # Try to read from a local .env file as fallback
    try:
        with open('.env', 'r') as f:
            for line in f:
                if line.startswith('ELEVENLABS_API_KEY='):
                    api_key = line.strip().split('=', 1)[1].strip('"\'')
                    logger.info("Found ElevenLabs API key from .env file")
                    return api_key
    except:
        pass

    return None

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    elevenlabs_available = bool(get_unity_api_key())

    return jsonify({
        "status": "healthy",
        "provider": "elevenlabs",
        "api_key_configured": elevenlabs_available
    })

def generate_with_elevenlabs(prompt, seed, sample_rate, duration_seconds, prompt_influence, looping=False, api_key_override=None):
    """Generate audio using ElevenLabs API."""
    try:
        from elevenlabs.client import ElevenLabs

        # Get API key
        api_key = api_key_override or get_unity_api_key()

        if not api_key:
            raise Exception("ELEVENLABS_API_KEY not found. Please set it in Unity's API Key Manager (Window > Satie > API Key Manager)")

        logger.info(f"Generating audio with ElevenLabs: '{prompt}'")

        # Create client
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
    """Generate audio from text prompt using ElevenLabs."""
    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        seed = data.get('seed', 0)
        sample_rate = data.get('sample_rate', 44100)
        duration_seconds = data.get('duration_seconds', 10.0)
        prompt_influence = data.get('prompt_influence', 0.3)
        looping = data.get('looping', False)

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        logger.info(f"Generating audio for prompt: '{prompt}', seed: {seed}")

        # Get API key from header if provided
        api_key_override = request.headers.get('X-ElevenLabs-Key')

        # Generate audio
        audio_data = generate_with_elevenlabs(
            prompt,
            seed,
            sample_rate,
            duration_seconds,
            prompt_influence,
            looping,
            api_key_override
        )

        # Create WAV file in memory
        wav_buffer = io.BytesIO()
        sf.write(wav_buffer, audio_data, sample_rate, format='WAV')
        wav_buffer.seek(0)

        logger.info(f"Audio generated successfully")

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

@app.route('/', methods=['GET'])
def index():
    """Root endpoint with API information."""
    return jsonify({
        "name": "ElevenLabs Audio Generation Server for SatieLang",
        "version": "3.0.0",
        "provider": "elevenlabs",
        "endpoints": {
            "/health": "Health check",
            "/generate": "Generate audio from prompt"
        },
        "setup": {
            "elevenlabs": "Set ELEVENLABS_API_KEY environment variable or add to Unity's API Key Manager"
        }
    })

if __name__ == '__main__':
    # Run the server
    port = int(os.environ.get('PORT', 5001))
    logger.info(f"Starting ElevenLabs Audio Generation Server on port {port}")
    logger.info("For ElevenLabs support, set ELEVENLABS_API_KEY environment variable or configure in Unity")
    app.run(host='0.0.0.0', port=port, debug=False)
