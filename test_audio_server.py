#!/usr/bin/env python3
"""
Test Audio Server - Generates simple sine wave audio for testing
"""

import io
import json
import numpy as np
import soundfile as sf
from flask import Flask, request, jsonify, send_file
from flask_cors import CORS

app = Flask(__name__)
CORS(app)

@app.route('/health', methods=['GET'])
def health_check():
    return jsonify({"status": "healthy", "type": "test_server"})

@app.route('/generate', methods=['POST'])
def generate_audio():
    """Generate a simple sine wave audio for testing."""
    try:
        data = request.get_json()
        prompt = data.get('prompt', 'test')
        seed = data.get('seed', 0)
        sample_rate = data.get('sample_rate', 44100)

        print(f"Generating test audio for: '{prompt}' with seed: {seed}")

        # Generate different frequencies based on prompt
        if 'river' in prompt.lower():
            frequency = 200 + seed * 50  # Low frequency for river
        elif 'ocean' in prompt.lower():
            frequency = 150 + seed * 30  # Very low frequency for ocean
        elif 'bird' in prompt.lower():
            frequency = 1000 + seed * 200  # High frequency for bird
        else:
            frequency = 440 + seed * 100  # Default A note

        # Generate 3 seconds of sine wave
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

        print(f"Test audio generated successfully ({frequency}Hz)")

        return send_file(
            wav_buffer,
            mimetype='audio/wav',
            as_attachment=True,
            download_name=f'test_{seed}.wav'
        )

    except Exception as e:
        print(f"Error: {e}")
        return jsonify({"error": str(e)}), 500

@app.route('/', methods=['GET'])
def index():
    return jsonify({
        "name": "Test Audio Server",
        "status": "running",
        "note": "This generates simple test audio, not AI audio"
    })

if __name__ == '__main__':
    print("Starting TEST audio server (not AudioLDM2)...")
    print("This server generates simple sine waves for testing.")
    app.run(host='0.0.0.0', port=5000, debug=False)