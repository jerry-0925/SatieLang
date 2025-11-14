# DAW-Style Mixer Guide

## Overview

The Satie Runtime now includes a professional DAW-style mixer interface directly in the Unity Inspector. This allows you to control all active audio tracks in real-time, just like you would in Ableton Live, Logic Pro, or any modern DAW.

## Features

### 🎚 Master Channel
- **Master Volume**: Control overall output level (0-100%)
- **Master Mute**: Instantly mute all tracks
- Located at the top of the mixer for easy access

### 🎛 Mixer Groups
Create custom groups to organize and control multiple tracks together:
- **Solo/Mute**: Solo or mute entire groups
- **Volume Control**: Adjust group volume with a slider
- **Color Coding**: Assign colors to groups for visual organization
- **Pattern Matching**: Auto-assign tracks based on:
  - Clip name contains (e.g., "bird", "music/")
  - Kind filter (loop or oneshot)
- **Track Counter**: See how many tracks are in each group (play mode)
- **Collapsible**: Fold/unfold groups to save space

### 🎵 Individual Track Controls
View and control all active tracks during play mode:
- **Play Indicator**: Visual indicator showing which tracks are playing (▶)
- **Mute Button**: Mute individual tracks
- **Volume Slider**: Adjust track volume in real-time
- **Stop Button**: Stop specific tracks
- **Track Info**: View clip name, kind (loop/oneshot), key, and source count

## How to Use

### Setting Up Mixer Groups (Edit Mode)

1. **Select your SatieRuntime GameObject** in the hierarchy
2. **In the Inspector**, expand "🎚 DAW Mixer"
3. **Click the "+" button** next to "Mixer Groups" to create a new group
4. **Configure the group:**
   - Name it (e.g., "Birds", "Music", "Ambience")
   - Choose a color for visual identification
   - Set up track matching patterns:
     - **Clip Name Contains**: Add patterns like "bird", "music/drone", etc.
     - **Kind Filter**: Add "loop" or "oneshot" to filter by type

Example configurations:
```
Group: "Background Music"
- Clip Name Contains: ["music/"]
- Kind Filter: ["loop"]
- Color: Blue

Group: "Bird Sounds"
- Clip Name Contains: ["bird"]
- Kind Filter: ["oneshot"]
- Color: Green

Group: "Drones"
- Clip Name Contains: ["drone", "pad"]
- Kind Filter: ["loop"]
- Color: Purple
```

### Using the Mixer (Play Mode)

1. **Enter Play Mode** in Unity
2. **The mixer updates in real-time** showing all active tracks
3. **Use the controls:**
   - **Master Mute (M)**: Quick silence everything
   - **Group Solo (S)**: Hear only tracks in this group
   - **Group Mute (M)**: Silence all tracks in a group
   - **Group Volume**: Adjust all tracks in the group simultaneously
   - **Individual Track Controls**: Fine-tune specific tracks

### Workflow Tips

#### Solo Workflow
Just like in a DAW, soloing helps you focus on specific elements:
1. Click **S** on a group to solo it (all other groups mute)
2. Multiple groups can be soloed simultaneously
3. Click **S** again to un-solo

#### Quick Muting
- **Group Mute**: Mute entire categories (e.g., mute all bird sounds)
- **Individual Mute**: Mute specific problematic tracks
- **Master Mute**: Emergency silence button

#### Volume Balancing
1. Start with all groups at 100%
2. Lower group volumes to balance (e.g., birds at 60%, music at 80%)
3. Use master volume for overall output control
4. Fine-tune individual tracks if needed

## Advanced Usage

### Dynamic Group Control via Code

You can control mixer groups programmatically:

```csharp
// Get the runtime
SatieRuntime runtime = GetComponent<SatieRuntime>();

// Add a new group at runtime
var musicGroup = runtime.AddMixerGroup("Dynamic Music");
musicGroup.clipNamePatterns.Add("music/");
musicGroup.color = Color.blue;
musicGroup.volume = 0.8f;

// Apply groups to update tracks
runtime.ApplyMixerGroups();

// Modify existing group
var groups = runtime.GetMixerGroups();
groups[0].mute = true;
runtime.ApplyMixerGroups();

// Master controls
runtime.SetMasterVolume(0.5f);
runtime.SetMasterMute(true);
```

### Track Key Format

Every track has a unique key you can use for precise control:
```
Format: {lineNumber}_{kind}_{clip}_{instanceIndex}
Example: "0_loop_music/drone_0"
```

Use track keys for programmatic control:
```csharp
runtime.StopTrack("0_loop_music/drone_0");
runtime.SetTrackVolume("1_oneshot_bird_0", 0.5f);
runtime.SetTrackMute("2_loop_ambient_0", true);
```

## Keyboard Shortcuts (Unity Editor)

While the GameObject is selected:
- **R**: Reload script (delta reload, preserves persistent tracks)
- **Shift+R**: Full reload (resets everything)

## Visual Indicators

- **🎚** = Mixer section
- **S** = Solo button (yellow when active)
- **M** = Mute button (red when active)
- **▶** = Track is currently playing (green)
- **▢** = Track is stopped (gray)
- **(N)** = Number of tracks in group

## Best Practices

### Organization
1. **Create groups early**: Set up groups before pressing play
2. **Use descriptive names**: "Rain Ambience" not "Group 1"
3. **Color code logically**: Use similar colors for related sounds
4. **Pattern matching**: Use broad patterns for flexibility (e.g., "music/" catches all music)

### Performance
1. **Collapse unused groups**: Keeps the Inspector clean
2. **Use groups for batch operations**: More efficient than individual track control
3. **Master volume first**: Adjust master before individual tracks

### Mixing Strategy
1. **Start with groups**: Get the overall balance right
2. **Then adjust individuals**: Fine-tune problematic tracks
3. **Use solo liberally**: Isolate and fix issues quickly
4. **Test with master mute**: Check silence detection

## Troubleshooting

### Tracks not appearing in group
- Check your clip name patterns match the actual clip names
- Remember patterns are case-sensitive
- Use simpler patterns (e.g., "bird" instead of "bird/forest/")

### Group controls not working
- Make sure you're in Play Mode
- Check that `ApplyMixerGroups()` is being called
- Verify tracks match the group's patterns

### Mixer not updating
- Click the "Refresh" button in the mixer header
- The mixer auto-refreshes during play mode

### Can't see mixer
- Check that "🎚 DAW Mixer" foldout is expanded
- Create at least one mixer group to see mixer in edit mode
- Mixer always shows in play mode if tracks are active

## Comparison to Traditional DAWs

| Feature | Satie Mixer | Traditional DAW |
|---------|-------------|-----------------|
| Solo/Mute | ✅ Groups & Tracks | ✅ Channels |
| Volume Faders | ✅ Groups & Tracks | ✅ Channels |
| Master Channel | ✅ Yes | ✅ Yes |
| Color Coding | ✅ Groups | ✅ Channels |
| Real-time Updates | ✅ Yes | ✅ Yes |
| Pattern-based Routing | ✅ Unique to Satie | ❌ Manual routing |
| Live Script Editing | ✅ Yes | ❌ Not applicable |

## Future Enhancements

Potential features for future versions:
- VU meters showing real-time levels
- Pan controls for stereo positioning
- Send effects (reverb, delay buses)
- Automation recording
- Group-level effects
- Snapshot system for saving mixer states
- MIDI controller support

## Example Workflow

### Setting up a soundscape scene:

1. **Create groups in edit mode:**
   ```
   - "Music" (color: blue) → matches "music/"
   - "Nature" (color: green) → matches "bird", "wind", "water"
   - "Ambience" (color: purple) → matches "drone", "pad"
   ```

2. **Enter play mode**

3. **Balance the mix:**
   - Solo "Music" group, adjust volume to 70%
   - Solo "Nature" group, adjust volume to 50%
   - Solo "Ambience" group, adjust volume to 80%
   - Un-solo all

4. **Fine-tune:**
   - Mute specific bird calls that are too loud
   - Adjust individual drone volumes
   - Use master volume for overall level

5. **Test:**
   - Use master mute to test silence detection
   - Solo each group to check for issues
   - Adjust as needed

This gives you professional-level mixing control directly in Unity's Inspector!
