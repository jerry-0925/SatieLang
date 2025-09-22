#!/usr/bin/env python3
"""
Multi-Provider Audio Generation Server for SatieLang
Supports both AudioLDM2 and Eleven Labs for audio generation
"""

import os
import io
import json
import logging
import tempfile
import base64
from enum import Enum
from typing import Optional, Dict, Any
from flask import Flask, request, jsonify, send_file
from flask_cors import CORS
import numpy as np
import soundfile as sf
import torch

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)
CORS(app)  # Enable CORS for Unity integration

class AudioProvider(Enum):
    AUDIOLDM2 = "audioldm2"
    ELEVENLABS = "elevenlabs"
    TEST = "test"  # Test mode with simple sine waves

class AudioGenerationServer:
    def __init__(self):
        self.audioldm2_model = None
        self.device = None
        self.elevenlabs_api_key = None
        self.default_provider = AudioProvider.AUDIOLDM2
        self.config = self.load_config()

    def load_config(self):
        """Load configuration from file if it exists."""
        config_path = "audio_config.json"
        if os.path.exists(config_path):
            with open(config_path, 'r') as f:
                config = json.load(f)
                logger.info(f"Loaded configuration from {config_path}")
                return config
        else:
            # Create default config
            default_config = {
                "default_provider": "audioldm2",
                "elevenlabs": {
                    "api_key": "",
                    "duration_seconds": 10.0,
                    "prompt_influence": 0.3
                },
                "audioldm2": {
                    "model_id": "cvssp/audioldm2",
                    "use_float16": True,
                    "num_inference_steps": 200,
                    "audio_length_in_s": 10.0
                }
            }
            # Save default config
            with open(config_path, 'w') as f:
                json.dump(default_config, f, indent=2)
            logger.info(f"Created default configuration at {config_path}")
            return default_config

    def initialize_audioldm2(self):
        """Initialize the AudioLDM2 model."""
        if self.audioldm2_model is not None:
            logger.info("AudioLDM2 model already initialized")
            return

        logger.info("Initializing AudioLDM2 model...")

        try:
            from diffusers import AudioLDM2Pipeline

            # Determine device (GPU if available)
            if torch.cuda.is_available():
                self.device = torch.device("cuda")
            elif torch.backends.mps.is_available():
                self.device = torch.device("mps")
            else:
                self.device = torch.device("cpu")
            logger.info(f"Using device: {self.device}")

            # Load the model
            model_config = self.config.get("audioldm2", {})
            dtype = torch.float16 if torch.cuda.is_available() and model_config.get("use_float16", True) else torch.float32

            self.audioldm2_model = AudioLDM2Pipeline.from_pretrained(
                model_config.get("model_id", "cvssp/audioldm2"),
                torch_dtype=dtype,
                use_safetensors=True
            ).to(self.device)

            logger.info("AudioLDM2 model loaded successfully!")
        except Exception as e:
            logger.error(f"Failed to initialize AudioLDM2: {e}")
            raise

    def initialize_elevenlabs(self):
        """Initialize Eleven Labs API."""
        elevenlabs_config = self.config.get("elevenlabs", {})
        self.elevenlabs_api_key = elevenlabs_config.get("api_key", "")

        # Try to read from Unity's API key storage if not in config
        if not self.elevenlabs_api_key:
            self.elevenlabs_api_key = self.get_unity_api_key()

        if not self.elevenlabs_api_key:
            logger.warning("Eleven Labs API key not found in config or Unity storage")
            return False

        logger.info("Eleven Labs API configured")
        return True

    def get_unity_api_key(self):
        """Read ElevenLabs API key from Unity's encrypted storage."""
        import platform
        import base64

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
                                    logger.info("Found ElevenLabs API key from Unity storage (B64)")
                                    return api_key
                                else:
                                    # For now, we can't decrypt the AES encryption without the proper key
                                    # But Unity should update to use B64 for cross-app compatibility
                                    logger.warning("Found encrypted ElevenLabs key but cannot decrypt (use B64 encoding in Unity)")
                                break
                except Exception as e:
                    logger.warning(f"Failed to read Unity API keys: {e}")

        return None

    def generate_with_audioldm2(self, prompt: str, seed: int = 0, **kwargs) -> bytes:
        """Generate audio using AudioLDM2."""
        if self.audioldm2_model is None:
            self.initialize_audioldm2()

        model_config = self.config.get("audioldm2", {})
        num_inference_steps = kwargs.get("num_inference_steps", model_config.get("num_inference_steps", 200))
        audio_length_in_s = kwargs.get("audio_length_in_s", model_config.get("audio_length_in_s", 10.0))
        sample_rate = kwargs.get("sample_rate", 16000)

        logger.info(f"Generating audio with AudioLDM2 - Prompt: '{prompt}', Seed: {seed}")

        # Set seed for reproducibility
        generator = torch.Generator(device=self.device).manual_seed(seed)

        # Generate audio
        with torch.no_grad():
            output = self.audioldm2_model(
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

        # Normalize audio to prevent clipping
        max_val = np.abs(audio_data).max()
        if max_val > 0:
            audio_data = audio_data / max_val * 0.95

        # Convert to the requested sample rate if needed
        if sample_rate != 16000:
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

        return wav_buffer.getvalue()

    def generate_with_elevenlabs(self, prompt: str, api_key_override: str = None, **kwargs) -> bytes:
        """Generate sound effects using Eleven Labs Sound Generation API."""
        # Use override key if provided, otherwise use configured key
        api_key = api_key_override or self.elevenlabs_api_key

        if not api_key:
            if not self.initialize_elevenlabs():
                raise ValueError("Eleven Labs API key not configured")
            api_key = self.elevenlabs_api_key

        try:
            import requests

            elevenlabs_config = self.config.get("elevenlabs", {})
            duration_seconds = kwargs.get("duration_seconds", elevenlabs_config.get("duration_seconds", 10.0))
            prompt_influence = kwargs.get("prompt_influence", elevenlabs_config.get("prompt_influence", 0.3))

            logger.info(f"Generating sound effect with Eleven Labs - Prompt: '{prompt}'")

            # Use the sound generation endpoint
            url = "https://api.elevenlabs.io/v1/sound-generation"

            headers = {
                "Accept": "audio/mpeg",
                "Content-Type": "application/json",
                "xi-api-key": api_key
            }

            data = {
                "text": prompt,
                "duration_seconds": duration_seconds,
                "prompt_influence": prompt_influence
            }

            response = requests.post(url, json=data, headers=headers)

            if response.status_code != 200:
                error_msg = f"Eleven Labs API error: {response.status_code} - {response.text}"
                logger.error(error_msg)
                raise Exception(error_msg)

            # Convert MP3 to WAV
            mp3_data = response.content

            if not mp3_data or len(mp3_data) == 0:
                raise Exception("Eleven Labs returned empty audio data")

            # Use pydub to convert MP3 to WAV
            try:
                from pydub import AudioSegment
                logger.info(f"Converting MP3 to WAV (received {len(mp3_data)} bytes)")
                audio = AudioSegment.from_mp3(io.BytesIO(mp3_data))

                # Set to the requested sample rate
                sample_rate = kwargs.get("sample_rate", 44100)
                audio = audio.set_frame_rate(sample_rate)

                # Export as WAV
                wav_buffer = io.BytesIO()
                audio.export(wav_buffer, format="wav", parameters=["-ar", str(sample_rate)])
                wav_buffer.seek(0)

                wav_data = wav_buffer.getvalue()
                logger.info(f"Successfully converted to WAV ({len(wav_data)} bytes, {sample_rate}Hz)")
                return wav_data
            except ImportError as e:
                error_msg = "pydub not installed - cannot convert Eleven Labs MP3 to WAV. Install with: pip install pydub"
                logger.error(error_msg)
                raise Exception(error_msg)
            except Exception as e:
                error_msg = f"Failed to convert MP3 to WAV: {str(e)}"
                logger.error(error_msg)
                raise Exception(error_msg)

        except Exception as e:
            logger.error(f"Eleven Labs sound generation failed: {e}")
            raise

    def generate_test_audio(self, prompt: str, seed: int = 0, **kwargs) -> bytes:
        """Generate simple test audio (sine wave)."""
        sample_rate = kwargs.get("sample_rate", 44100)

        logger.info(f"Generating test audio for: '{prompt}' with seed: {seed}")

        # Generate different frequencies based on prompt
        if 'river' in prompt.lower() or 'water' in prompt.lower():
            frequency = 200 + seed * 50  # Low frequency for water
        elif 'ocean' in prompt.lower() or 'wave' in prompt.lower():
            frequency = 150 + seed * 30  # Very low frequency for ocean
        elif 'bird' in prompt.lower() or 'chirp' in prompt.lower():
            frequency = 1000 + seed * 200  # High frequency for bird
        elif 'wind' in prompt.lower():
            frequency = 300 + seed * 40  # Mid-low frequency for wind
        else:
            frequency = 440 + seed * 100  # Default A note

        # Generate 3 seconds of sine wave with harmonics
        duration = 3.0
        t = np.linspace(0, duration, int(sample_rate * duration))

        # Create a more interesting sound with multiple harmonics
        audio = np.sin(2 * np.pi * frequency * t) * 0.3
        audio += np.sin(2 * np.pi * frequency * 2 * t) * 0.1  # Harmonic
        audio += np.sin(2 * np.pi * frequency * 0.5 * t) * 0.1  # Sub-harmonic

        # Add some noise for texture
        audio += np.random.normal(0, 0.01, len(t))

        # Apply envelope (fade in/out)
        envelope = np.ones_like(t)
        fade_len = int(0.1 * sample_rate)
        envelope[:fade_len] = np.linspace(0, 1, fade_len)
        envelope[-fade_len:] = np.linspace(1, 0, fade_len)
        audio *= envelope

        # Normalize
        audio = audio / np.max(np.abs(audio)) * 0.9

        # Create WAV file in memory
        wav_buffer = io.BytesIO()
        sf.write(wav_buffer, audio, sample_rate, format='WAV')
        wav_buffer.seek(0)

        return wav_buffer.getvalue()

# Create server instance
server = AudioGenerationServer()
# Try to initialize ElevenLabs from Unity storage on startup
server.initialize_elevenlabs()

@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint."""
    return jsonify({
        "status": "healthy",
        "providers": {
            "audioldm2": {
                "available": True,
                "initialized": server.audioldm2_model is not None
            },
            "elevenlabs": {
                "available": bool(server.config.get("elevenlabs", {}).get("api_key")),
                "initialized": bool(server.elevenlabs_api_key)
            },
            "test": {
                "available": True,
                "initialized": True
            }
        },
        "default_provider": server.default_provider.value
    })

@app.route('/generate', methods=['POST'])
def generate_audio():
    """Generate audio with specified provider."""
    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        provider = data.get('provider', server.config.get("default_provider", "audioldm2"))
        seed = data.get('seed', 0)

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        # Generate audio based on provider
        if provider == AudioProvider.ELEVENLABS.value:
            # Get API key from header if provided
            api_key_from_header = request.headers.get('X-ElevenLabs-Key')
            # Remove prompt from data dict to avoid duplicate argument
            generation_params = {k: v for k, v in data.items() if k != 'prompt'}
            audio_data = server.generate_with_elevenlabs(prompt, api_key_override=api_key_from_header, **generation_params)
        elif provider == AudioProvider.TEST.value:
            audio_data = server.generate_test_audio(prompt, seed, **data)
        else:  # Default to AudioLDM2
            audio_data = server.generate_with_audioldm2(prompt, seed, **data)

        logger.info(f"Audio generated successfully using {provider}")

        # Return the WAV file
        return send_file(
            io.BytesIO(audio_data),
            mimetype='audio/wav',
            as_attachment=True,
            download_name=f'generated_{provider}_{seed}.wav'
        )

    except Exception as e:
        logger.error(f"Error generating audio: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/generate_multiple', methods=['POST'])
def generate_multiple_audio():
    """Generate multiple audio variations."""
    try:
        # Parse request data
        data = request.get_json()
        prompt = data.get('prompt', '')
        num_options = data.get('num_options', 3)
        provider = data.get('provider', server.config.get("default_provider", "audioldm2"))

        if not prompt:
            return jsonify({"error": "No prompt provided"}), 400

        logger.info(f"Generating {num_options} audio options using {provider}")

        audio_files = []

        for i in range(num_options):
            # Use different seed for each variation
            data['seed'] = i

            # Generate audio based on provider
            if provider == AudioProvider.ELEVENLABS.value:
                # For Eleven Labs, vary prompt influence for different variations
                data['prompt_influence'] = min(1.0, data.get('prompt_influence', 0.3) + (i * 0.1))
                # Remove prompt from data dict to avoid duplicate argument
                generation_params = {k: v for k, v in data.items() if k != 'prompt'}
                audio_data = server.generate_with_elevenlabs(prompt, **generation_params)
            elif provider == AudioProvider.TEST.value:
                audio_data = server.generate_test_audio(prompt, i, **data)
            else:  # Default to AudioLDM2
                audio_data = server.generate_with_audioldm2(prompt, i, **data)

            # Encode as base64 for JSON response
            audio_base64 = base64.b64encode(audio_data).decode('utf-8')
            audio_files.append(audio_base64)

            logger.info(f"Generated option {i+1}/{num_options}")

        logger.info(f"All {num_options} audio options generated successfully")

        return jsonify({
            "prompt": prompt,
            "provider": provider,
            "audio_files": audio_files,
            "sample_rate": data.get('sample_rate', 44100)
        })

    except Exception as e:
        logger.error(f"Error generating multiple audio: {str(e)}")
        return jsonify({"error": str(e)}), 500

@app.route('/config', methods=['GET', 'POST'])
def config_endpoint():
    """Get or update server configuration."""
    if request.method == 'GET':
        return jsonify(server.config)
    else:
        try:
            new_config = request.get_json()
            server.config.update(new_config)

            # Save updated config
            with open("audio_config.json", 'w') as f:
                json.dump(server.config, f, indent=2)

            # Reinitialize if needed
            if "elevenlabs" in new_config:
                server.initialize_elevenlabs()

            return jsonify({"status": "Configuration updated", "config": server.config})
        except Exception as e:
            return jsonify({"error": str(e)}), 500

@app.route('/', methods=['GET'])
def index():
    """Root endpoint with API information."""
    return jsonify({
        "name": "Multi-Provider Audio Generation Server",
        "version": "2.0.0",
        "providers": ["audioldm2", "elevenlabs", "test"],
        "endpoints": {
            "/health": "Health check and provider status",
            "/generate": "Generate single audio from prompt",
            "/generate_multiple": "Generate multiple audio variations",
            "/config": "Get or update server configuration"
        }
    })

if __name__ == '__main__':
    # Run the server
    port = int(os.environ.get('PORT', 5001))
    logger.info(f"Starting Multi-Provider Audio Generation Server on port {port}")
    logger.info("Configure providers in audio_config.json")
    app.run(host='0.0.0.0', port=port, debug=False)