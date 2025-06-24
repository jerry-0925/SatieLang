# SatieLang

SatieLang is a Domain Specific Language (DSL) designed for generative and event-based audio scripting within the Unity game engine. It allows you to define complex audio behaviors with a simple, declarative syntax, managing audio playback, spatialization, and parameter randomization with ease.

## Getting Started

### Prerequisites

You need **Unity 2022.3.13f1**. You can download it from the Unity Hub or the Unity Download Archive:
* [Unity Download Archive](https://unity.com/releases/editor/archive) (Search for 2022.3.13)

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

