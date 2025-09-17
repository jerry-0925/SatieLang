# Audio Generation Setup for SatieLang

This system supports multiple audio generation providers: AudioLDM2 (local AI model) and Eleven Labs (cloud-based sound effects).

## Features

- **AudioLDM2**: Open-source text-to-audio generation (runs locally)
- **Eleven Labs**: High-quality sound effects generation (requires API key)
- **Test Mode**: Simple sine wave generation for testing
- **Unity Integration**: Full control through Inspector settings

## Setup Instructions

### 1. Install Python Dependencies

```bash
# Create virtual environment (recommended)
python3 -m venv audioldm2_venv
source audioldm2_venv/bin/activate  # On Windows: audioldm2_venv\Scripts\activate

# Install requirements
pip install -r requirements.txt
```

### 2. Configure Providers

Create or edit `audio_config.json`:

```json
{
  "default_provider": "audioldm2",
  "elevenlabs": {
    "api_key": "YOUR_ELEVENLABS_API_KEY_HERE",
    "duration_seconds": 10.0,
    "prompt_influence": 0.3
  },
  "audioldm2": {
    "model_id": "cvssp/audioldm2",
    "use_float16": true,
    "num_inference_steps": 200,
    "audio_length_in_s": 10.0
  }
}
```

#### Getting an Eleven Labs API Key:
1. Sign up at https://elevenlabs.io
2. Go to Profile Settings → API Keys
3. Generate and copy your API key
4. Paste it in `audio_config.json`

### 3. Start the Server

```bash
# Activate virtual environment first if not already active
source audioldm2_venv/bin/activate

# Run the server
python audio_generation_server.py
```

The server will start on `http://localhost:5001`

### 4. Unity Configuration

In Unity, the SatieAudioGen component provides these Inspector settings:

#### Server Configuration
- **API URL**: Server endpoint (default: `http://localhost:5001/generate`)
- **Sample Rate**: Audio sample rate (default: 44100)
- **Num Options**: Number of variations to generate
- **Default Provider**: Choose between AudioLDM2, ElevenLabs, or Test

#### Eleven Labs Settings (visible in Inspector)
- **Duration**: Sound effect duration (1-30 seconds)
- **Prompt Influence**: How closely to follow the prompt (0-1, where 0.3 is balanced)

#### AudioLDM2 Settings (visible in Inspector)
- **Inference Steps**: Quality/speed tradeoff (50-500, higher = better quality but slower)
- **Duration**: Audio duration (1-30 seconds)

## Usage Examples

### From Unity Code

```csharp
// Using default provider
var result = await SatieAudioGen.Instance.GenerateAudioOptions(
    "ocean waves crashing on beach",
    numOptions: 3
);

// Using specific provider
var result = await SatieAudioGen.Instance.GenerateAudioOptions(
    "thunder and rain",
    numOptions: 2,
    provider: AudioProvider.ElevenLabs
);

// Convert to AudioClip
AudioClip clip = SatieAudioGen.Instance.ConvertBytesToAudioClip(
    result.audioData[0],
    "generated_audio"
);
```

### Server API Endpoints

#### Generate Single Audio
```
POST http://localhost:5001/generate
{
    "prompt": "waterfall in a cave",
    "provider": "elevenlabs",
    "duration_seconds": 5,
    "prompt_influence": 0.4
}
```

#### Generate Multiple Variations
```
POST http://localhost:5001/generate_multiple
{
    "prompt": "wind through trees",
    "provider": "audioldm2",
    "num_options": 3
}
```

#### Health Check
```
GET http://localhost:5001/health
```

## Provider Comparison

| Feature | AudioLDM2 | Eleven Labs | Test Mode |
|---------|-----------|-------------|-----------|
| **Speed** | Slow (10-30s) | Fast (1-3s) | Instant |
| **Quality** | Good | Excellent | Basic |
| **Cost** | Free (local) | API credits | Free |
| **GPU Required** | Recommended | No | No |
| **Internet** | No (after download) | Yes | No |
| **Best For** | Experimental sounds | Professional SFX | Testing |

## Troubleshooting

### Server won't start
- Ensure all dependencies are installed: `pip install -r requirements.txt`
- Check Python version (3.8+ required)
- For AudioLDM2, first run will download the model (~2GB)

### Eleven Labs errors
- Verify API key in `audio_config.json`
- Check API credits at https://elevenlabs.io
- Ensure internet connection

### Unity connection errors
- Check server is running: `python audio_generation_server.py`
- Verify URL in Unity Inspector (default: `http://localhost:5001/generate`)
- Check firewall settings

### Performance issues
- AudioLDM2: Use GPU if available (CUDA or MPS)
- Reduce inference steps for faster generation (at cost of quality)
- Use Test mode for rapid prototyping

## Tips

1. **Prompt Writing**: Be specific - "heavy rain on metal roof" works better than just "rain"
2. **Variations**: Generate multiple options to choose the best one
3. **Caching**: Generated audio is cached per prompt+provider combination
4. **Testing**: Use Test mode for quick iterations, then switch to real providers