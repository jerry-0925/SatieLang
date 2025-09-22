# SatieLang

SatieLang is a Domain Specific Language (DSL) designed for generative and event-based audio scripting within the Unity game engine. It allows you to define complex audio behaviors with a simple, declarative syntax, managing audio playback, spatialization, and parameter randomization with ease.

## Getting Started

### Prerequisites

#### Unity Version
You need **Unity 6000.1.1f1**. You can download it from:
* **Unity Hub**: Search for version 6000.1.1f1 in the Unity Hub's "Install Editor" section
* **Unity Download Archive**: [https://unity.com/releases/editor/archive](https://unity.com/releases/editor/archive) (Search for 6000.1.1f1)

#### API Keys Configuration
SatieLang includes an integrated API Key Manager for AI-powered audio generation features. To configure API keys:

1. **Open the API Key Manager**:
   - In Unity, go to menu: **Satie > API Key Manager**
   - A window will open showing all available API providers

2. **Configure API Keys**:
   - **OpenAI API Key** (for AI-powered features):
     - Get your API key from [OpenAI Platform](https://platform.openai.com/api-keys)
     - Enter it in the OpenAI field in the API Key Manager
     - Click "Save" to securely store the key

   - **ElevenLabs API Key** (for AI sound effects generation):
     - Get your API key from [ElevenLabs](https://elevenlabs.io/)
     - Enter it in the ElevenLabs field in the API Key Manager
     - Click "Save" to securely store the key

3. **Alternative: Environment Variables**:
   - You can also set API keys using environment variables:
     - `SATIE_API_KEY_OPENAI` for OpenAI
     - `SATIE_API_KEY_ELEVENLABS` for ElevenLabs
   - Environment variables take priority over saved keys

**Note**: API keys are encrypted and stored securely in Unity's persistent data path. The manager also supports automatic migration of legacy API key files.

#### Steam Audio Setup
SatieLang includes built-in support for Steam Audio's advanced spatial audio features:

1. **Steam Audio is Pre-Installed**:
   - The Steam Audio plugin is already included in the SatieLang project
   - Located in `Assets/Plugins/SteamAudio/`
   - No additional download needed

2. **Enable Steam Audio Features**:
   - SatieLang automatically detects and uses Steam Audio when available
   - The `SatieSpatialAudio` component manages Steam Audio integration
   - Configure spatial features in the Unity Inspector:
     - Enable HRTF for realistic 3D audio positioning
     - Toggle occlusion, transmission, and reflection effects
     - Adjust default spatial settings (min/max distance, rolloff, etc.)

3. **Using Steam Audio**:
   - Steam Audio is configured at the Unity component level
   - When audio clips are played from `.sat` scripts, they inherit spatial settings from the Unity scene
   - Spatial audio is controlled via Unity's AudioSource components and Steam Audio components
   - The `SatieSpatialAudio` component handles the integration automatically

### Quick Start: "Hello World" Demo Scene

The repository includes a demo scene to help you get started quickly:
1.  Open the SatieLang project in Unity.
2.  Navigate to the `Assets > Tutorial` folder in the Project window.
3.  Open the scene named **"Hello World"**.
4.  Press Play to experience SatieLang in action. Examine the Satie Script (`.sat` file) and the `SatieRuntime` component in the scene to see how it's configured.

### Setting Up a New Scene

To use SatieLang in your own Unity scene:

1.  Create a new scene (File > New Scene).
2.  Create an empty GameObject (GameObject > Create Empty). You might want to name it something like "SatieAudioSystem".
3.  With the new GameObject selected, click "Add Component" in the Inspector window.
4.  Search for and add the **`SatieRuntime`** component.
5.  You will need a Satie Script to drive the audio. Create one as described below and assign it to the `Script File` field in the `SatieRuntime` component.

### Creating a Satie Script

SatieLang scripts are plain text files with a `.sat` extension. You can create them easily within Unity:

1.  In the Project window, navigate to the folder where you want to create your script (e.g., `Assets/AudioScripts`).
2.  Right-click in the folder.
3.  Select **Create > Satie Script (.sat)**.
4.  Unity will create a new Satie script file (e.g., `NewSatieScript.sat`) with some default content. Rename it as desired.

The default content will be:
```satie
# Satie Script - Hello World
loop "hello":
    volume = 0.8
    pitch = 0.8..1.2
```

You can also spawn multiple copies of a loop or oneshot by prefixing the
statement with a number and `*`. For example, the following plays four
independent loops of the same clip:

```satie
4 * loop "hello":
    volume = 0.8
    pitch = 0.8..1.2
```

### Full Tutorial
For a complete walkthrough of SatieLang syntax, usage, and examples, see the [SatieLang Tutorial](https://github.com/mateolarreaferro/SatieLang/blob/main/Assets/Tutorial.md).

## Basic Syntax Understanding

### Core Concepts

SatieLang uses a declarative syntax to define audio behaviors. Here are the fundamental concepts:

#### 1. Audio Playback Types
```satie
# One-shot sound (plays once)
oneshot "explosion":
    volume = 0.9
    pitch = 0.8..1.2

# Looping sound (plays continuously)
loop "ambient_wind":
    volume = 0.5
    pitch = 1.0
```

#### 2. Parameter Randomization
Use ranges (`to`) to randomize parameters:
```satie
loop "footsteps":
    volume = 0.6to0.9      # Random volume between 0.6 and 0.9
    pitch = 0.9to1.1       # Random pitch between 0.9 and 1.1
    every = 0.5to1.5       # Play every 0.5 to 1.5 seconds
```

#### 3. Parameter Interpolation
Animate volume and pitch over time:
```satie
loop "engine":
    volume = goto(0and0.8 as inquad in 2)      # Fade from 0 to 0.8 over 2 seconds
    pitch = gobetween(0.5and2.0 as inoutquad in 3)  # Oscillate pitch between 0.5 and 2.0 every 3 seconds
```

Easing functions available: `linear`, `inquad`, `outquad`, `inoutquad`, `incubic`, `outcubic`, `inoutcubic`

#### 4. Multiple Instances
Spawn multiple independent instances:
```satie
# Creates 5 separate bird chirp loops
5 * loop "bird_chirp":
    volume = 0.3to0.6
    pitch = 0.8to1.3
    every = 1.0to5.0
```

#### 5. Timing and Fading
Control timing and fade effects:
```satie
loop "ambient":
    starts_at = 2.0        # Start after 2 seconds
    duration = 10.0        # Play for 10 seconds
    fade_in = 1.0         # Fade in over 1 second
    fade_out = 2.0        # Fade out over 2 seconds
    overlap = true        # Allow overlapping instances

oneshot "sfx" every 0.5to2.0:  # Play repeatedly with random intervals
    volume = 0.8
```

#### 6. Groups
Apply properties to multiple statements:
```satie
group:
    volume = 0.5
    pitch = 1.2

    loop "layer1":
        fade_in = 1.0

    loop "layer2":
        fade_in = 2.0
endgroup
```

#### 7. Movement and Visualization
Position and move audio sources in 3D space:
```satie
loop "flying_sound":
    move = fly,-20to20,0to20,-20to30,1  # Fly randomly in 3D space
    visual = trail                       # Add visual trail

oneshot "static_sound":
    move = pos,10,5,-10                  # Fixed position
    visual = sphere                       # Show as sphere
```

#### 8. Comments
Use `#` for single-line comments:
```satie
# This is a comment
loop "music":  # This is also a comment
    volume = 0.5
```

#### 9. Audio File References
Supported formats and paths:
```satie
loop "audio/music/track1"      # Path to audio file
loop "sfx/explosion"           # Automatically looks in Audio folder
oneshot "bird/1to4" every 5to10:  # Random selection from bird1 to bird4
    volume = 0.1
```

### Quick Reference

| Statement | Description | Example |
|-----------|-------------|---------|
| `loop` | Continuous playback | `loop "ambient": volume = 0.5` |
| `oneshot` | Single playback | `oneshot "click": pitch = 1.0` |
| `every` | Repeat interval | `oneshot "beep" every 2to5:` |
| `volume` | Audio volume (0-1) | `volume = 0.8` or `volume = 0.5to1.0` |
| `pitch` | Playback speed/pitch | `pitch = 0.5to2.0` |
| `starts_at` | Delay before starting | `starts_at = 2.0` |
| `duration` | How long to play | `duration = 10.0` |
| `fade_in` | Fade in time | `fade_in = 1.0` |
| `fade_out` | Fade out time | `fade_out = 2.0` |
| `overlap` | Allow overlapping | `overlap = true` |
| `move` | Position/movement | `move = fly,-10to10,0to20,-5to5,1` |
| `visual` | Visual representation | `visual = trail and sphere` |
| `goto()` | Interpolate to value | `goto(0and1 as inquad in 2)` |
| `gobetween()` | Oscillate between | `gobetween(0.5and2 as linear in 3)` |
| `to` | Range randomization | `0.5to1.0` |
| `*` | Multiple instances | `3 * loop "rain"` |
| `group` | Group statements | `group: volume = 0.5` |

