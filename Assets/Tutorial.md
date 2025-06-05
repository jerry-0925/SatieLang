# Satie Language Tutorial

Welcome to the **Satie** scripting language for sound-based behavior in Unity. This tutorial introduces the syntax and capabilities of SatieLang through progressively more complex examples located in `Assets/Tutorial`.

---

## Section 0: Syntax Overview

Each **statement** in Satie is either a `loop` or a `oneshot`, optionally nested inside a `group`.

### Top-Level Forms
- `loop "clip/path":`: continuously playing sound
- `oneshot "clip/path" every X..Y:`: discrete event triggered periodically
- `group name:`: defines a set of shared defaults for child statements

### Core Parameters
| Name        | Type               | Description                                                                 |
|-------------|--------------------|-----------------------------------------------------------------------------|
| `clip`      | `string`           | Path to an audio clip under `Resources/Audio/`                             |
| `volume`    | `RangeOrValue`     | Amplitude (0.0 to 1.0); can be randomized                                   |
| `pitch`     | `RangeOrValue`     | Playback speed multiplier (1.0 = normal)                                   |
| `starts_at` | `RangeOrValue`     | When the sound starts (in seconds)                                         |
| `duration`  | `RangeOrValue`     | Lifespan of the sound (in seconds); it stops after this                    |
| `fade_in`   | `RangeOrValue`     | Time to fade in                                                            |
| `fade_out`  | `RangeOrValue`     | Time to fade out                                                           |
| `every`     | `RangeOrValue`     | Interval for repeating a `oneshot`                                         |
| `overlap`   | `bool`             | If true, allows overlapping instances                                      |
| `visualize` | `bool`             | Shows a Trail Renderer for moving sounds                                   |

### Movement (`move`)
Syntax:
```
move = [type], xRange, yRange, zRange, speed
```
- `pos`: static position
- `walk`: movement in X and Z only
- `fly`: movement in full 3D space
- Last parameter is always speed (units per second)

### Randomization
- Use `5..10` for ranges (any numeric parameter)
- Use `"clip/1..3"` to randomly choose one of multiple files

---

## Section 1: Hello World (1. hello world)
```satie
# 'oneshot' defines a single, non-looping sound event
# Triggers every 3–5 seconds, with full volume
oneshot "conversation/hello" every 3..5:
    volume = 1
```
- Clip must be located at `Assets/Resources/Audio/conversation/hello.wav`

---

## Section 2: Loops (2. loops)
```satie
loop "ambience/forest":
    volume = 0.1
    fade_in = 5

loop "ambience/water":
    volume = 0.08
    pitch = 0.8..1.2
    fade_in = 10
```
- Loops are great for ambience
- Fade-ins help avoid abrupt starts

---

## Section 3: Randomization (3. randomization)
```satie
oneshot "bicycle/1..30" every 0.5..1:
    volume = 0.5..1
    pitch = 0.5..1.5
    move = fly, -5..5, 0..5, -5..5, 0.06
```
- File and parameter randomization create natural variety

---

## Section 4: Spatialization (4. spatialization)
```satie
group birds:
volume = 0.4
pitch = 0.35
    oneshot "bird/1..3" every 1..10:
        volume = 0.8..1
        fade_in = 1..5
        move = fly, -15..15, 0..15, -15..15, 0.1
        visualize = true

    oneshot "bird/1..3" every 1..5:
        volume = 0.8..1
        fade_in = 1..5
        move = fly, -20..20, 0..15, -20..20, 0.03..0.1
        visualize = true

group conversation:
volume = 0.8
    oneshot "conversation/hello" every 1..4:
        volume = 0.8..1
        pitch = 0.1..1.5
        fade_in = 1..5
        move = walk, -5..5, -5..5, 0.08
        visualize = true

group forest:
volume = 0.2
    loop "ambience/forest":
        volume = 0.4
        fade_in = 10
        move = pos, 2, 0, 3
    loop "ambience/water":
        volume = 0.8
        fade_in = 10
        move = walk, -10..10, -10..10, 0.01
```
- `move` types define spatial behavior
- `visualize` adds a Trail Renderer for debugging

---

## Section 5: Groups (5. groups)
```satie
group music:
pitch = 0.5
volume = 1
fade_in = 0.5

    loop "music/drone":
        volume = 0.07
        fade_in = 35

    loop "music/drone":
        volume = 0.02
        pitch = 2
        fade_in = 30..40

    oneshot "music/1..3" every 20..40:
        volume = 0.5
        move = pos, -10..10, 0..10, -10..10

    loop "sacred/1":
        volume = 0.3
        move = walk, -10..10, -10..10, 0.01
        visualize = true
        fade_in = 50
```
- Group-level parameters apply to all child tracks unless overridden
- `fade_in = 0.5` multiplies children's fade_in time (e.g., halves it)

---

## Section 6: Sequencing (6. sequencing)
```satie
loop "music/drone":
    volume = 0.2
    starts_at = 1
    fade_in = 35
    duration = 60

loop "music/drone":
    volume = 0.08
    pitch = 2
    starts_at = 10
    fade_in = 30..40
    duration = 50

loop "music/drone":
    volume = 0.04
    pitch = 2.5
    starts_at = 15
    fade_in = 35
    duration = 35
```
- `starts_at` defines the delay (in seconds) before a track starts
- `duration` defines how long a track exists before being removed
- This enables layering and timed evolution of ambient textures
