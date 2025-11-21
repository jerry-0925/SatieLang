# SatieLang

SatieLang is a Domain Specific Language (DSL) for generative and event-based audio scripting in Unity. Define complex audio behaviors with simple, declarative syntax.

## Setup

### Unity Version
You need **Unity 6000.1.1f1**. Download from:
- **Unity Hub**: Search for 6000.1.1f1 in "Install Editor"
- **Unity Download Archive**: [https://unity.com/releases/editor/archive](https://unity.com/releases/editor/archive)

### API Keys Configuration

SatieLang uses API keys for AI-powered audio generation. You can configure them in two ways:

#### Option 1: C# File (Recommended)
Edit `Assets/Satie Scripts/SatieAPIKeys.cs` and add your keys directly:

```csharp
public static class SatieAPIKeys
{
    public static string OpenAIKey = "your-openai-key-here";
    public static string ElevenLabsKey = "your-elevenlabs-key-here";
}
```

Get your keys from:
- OpenAI: [https://platform.openai.com/api-keys](https://platform.openai.com/api-keys)
- ElevenLabs: [https://elevenlabs.io/](https://elevenlabs.io/)

#### Option 2: Unity API Key Manager
1. In Unity, go to **Satie > API Key Manager**
2. Enter your API keys in the fields
3. Click "Save"

Keys are encrypted and stored in Unity's persistent data path.

### Quick Start

1. Open the project in Unity
2. Navigate to `Assets > Tutorial` folder
3. Open the **"Hello World"** scene
4. Press Play

### Creating Your First Script

1. Right-click in the Project window
2. Select **Create > Satie Script (.sat)**
3. Rename and edit your script
4. Create an empty GameObject in your scene
5. Add the **SatieRuntime** component
6. Assign your `.sat` script to the `Script File` field

## Basic Syntax

### Playback Types
```satie
# One-shot sound (plays once)
oneshot "explosion"
    volume 0.9

# Looping sound (plays continuously)
loop "ambient"
    volume 0.5
```

### Randomization
Use `to` to create ranges:
```satie
loop "footsteps" every 0.5to1.5
    volume 0.6to0.9        # Random volume
    pitch 0.9to1.1         # Random pitch
```

### Interpolation
Animate parameters over time:
```satie
loop "engine"
    volume goto(0and0.8 as inquad in 2)           # Fade in
    pitch gobetween(0.5and2.0 as linear in 3)     # Oscillate
```

Easing functions: `linear`, `inquad`, `outquad`, `inoutquad`, `incubic`, `outcubic`, `inoutcubic`

### Multiple Instances
```satie
5 * loop "bird_chirp"
    volume 0.3to0.6
    pitch 0.8to1.3
```

### Timing and Fading
```satie
loop "ambient"
    start 2.0                          # Delay start
    volume goto(0and0.8 in 2)          # Fade in over 2 seconds
    end 10 fade 2                      # End at 10s with 2s fade out
```

### Groups
Apply properties to multiple sounds:
```satie
group background
    volume 0.5

    loop "layer1"
        volume goto(0and0.5 in 1)

    loop "layer2"
        volume goto(0and0.5 in 2)
endgroup
```

### 3D Audio
```satie
loop "flying_sound"
    move fly speed 1                    # Random 3D movement
    visual trail                        # Visual effect

oneshot "static_sound"
    move x 10 y 5 z -10                 # Fixed position
```

### Comments
```satie
# This is a comment
loop "music"  # Inline comment
    volume 0.5
```

## Quick Reference

| Feature | Example |
|---------|---------|
| Loop | `loop "ambient"` |
| One-shot | `oneshot "click"` |
| Repeat | `oneshot "beep" every 2to5` |
| Volume | `volume 0.8` or `volume 0.5to1.0` |
| Pitch | `pitch 0.9to1.1` |
| Start delay | `start 2.0` |
| End with fade | `end 10 fade 2` |
| Fade in | `volume goto(0and0.8 in 2)` |
| Interpolate | `goto(0and1 as inquad in 2)` |
| Oscillate | `gobetween(0.5and2 as linear in 3)` |
| Multiple | `3 * loop "rain"` |
| Group | `group intro` |

## Additional Resources

For a complete tutorial, see [SatieLang Tutorial](https://github.com/mateolarreaferro/SatieLang/blob/main/Assets/Tutorial.md).

