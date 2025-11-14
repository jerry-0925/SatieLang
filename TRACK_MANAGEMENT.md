# Satie Track Management System

## Overview

The Satie Track Management system provides intelligent voice/instance tracking for all audio playback in SatieLang. This solves the problem where persistent loops and oneshots would lose their references after creation, making them impossible to control or stop programmatically.

## Architecture

### Key Components

1. **SatieTrack** (`Assets/Architecture/SatieTrack.cs`)
   - Represents a single voice/track in the audio system
   - Encapsulates AudioSource(s), coroutine, and statement metadata
   - Provides per-track control methods (mute, volume, pitch, stop)

2. **SatieTrackManager** (`Assets/Architecture/SatieTrackManager.cs`)
   - Central manager for all active tracks
   - Handles track lifecycle (creation, querying, stopping)
   - Provides filtering by persistence, kind, etc.

3. **SatieRuntime** (updated)
   - Now uses TrackManager internally
   - Exposes public API for track control
   - Maintains backward compatibility

## Why This Design?

This follows industry best practices used by professional audio engines (FMOD, Wwise, Unity AudioMixer):

- **Single Responsibility**: TrackManager handles voice lifecycle; SatieRuntime handles script execution
- **Maintainability**: Centralized tracking makes debugging and feature additions easier
- **Scalability**: Easy to add features like "fade all", "get by name", voice limiting, etc.
- **Encapsulation**: Each track knows its own state and provides safe access methods

## Usage Examples

### Basic Track Control

```csharp
public class MyController : MonoBehaviour
{
    [SerializeField] private SatieRuntime satieRuntime;

    void Update()
    {
        // Print all active tracks
        if (Input.GetKeyDown(KeyCode.T))
        {
            satieRuntime.PrintTrackDebugInfo();
        }

        // Stop all tracks
        if (Input.GetKeyDown(KeyCode.S))
        {
            satieRuntime.StopAllTracks(includePersistent: true);
        }

        // Mute all tracks
        if (Input.GetKeyDown(KeyCode.M))
        {
            satieRuntime.MuteAllTracks(true);
        }
    }
}
```

### Working with Persistent Tracks

```csharp
// Get all persistent tracks
var persistentTracks = satieRuntime.GetPersistentTracks();

foreach (var track in persistentTracks)
{
    Debug.Log($"Persistent track: {track.Key}");
    Debug.Log($"  Clip: {track.Statement.clip}");
    Debug.Log($"  Kind: {track.Statement.kind}");
    Debug.Log($"  Is Playing: {track.IsPlaying}");

    // Control the track
    track.SetMute(true);
    track.SetVolume(0.5f);
    track.SetPitch(1.2f);
}
```

### Finding Specific Tracks

```csharp
// Find track by clip name
public SatieTrack FindTrackByClipName(string clipName)
{
    var tracks = satieRuntime.GetAllTracks();

    foreach (var track in tracks)
    {
        if (track.Statement.clip == clipName)
        {
            return track;
        }
    }

    return null;
}

// Stop all loops
public void StopAllLoops()
{
    var tracks = satieRuntime.GetAllTracks();
    var loopKeys = new List<string>();

    foreach (var track in tracks)
    {
        if (track.Statement.kind == "loop")
        {
            loopKeys.Add(track.Key);
        }
    }

    foreach (var key in loopKeys)
    {
        satieRuntime.StopTrack(key);
    }
}
```

### Advanced: Custom Fade Out

```csharp
public IEnumerator FadeOutTrack(string trackKey, float duration)
{
    var track = satieRuntime.GetTrack(trackKey);
    if (track == null) yield break;

    float startTime = Time.time;
    float startVolume = track.Sources.Count > 0 ? track.Sources[0].volume : 1f;

    while (Time.time - startTime < duration)
    {
        float t = (Time.time - startTime) / duration;
        float newVolume = Mathf.Lerp(startVolume, 0f, t);
        track.SetVolume(newVolume);
        yield return null;
    }

    satieRuntime.StopTrack(trackKey);
}
```

### Direct TrackManager Access

For advanced use cases, you can access the TrackManager directly:

```csharp
var trackManager = satieRuntime.GetTrackManager();

// Register callbacks for track lifecycle
trackManager.OnTrackStarted += (track) => {
    Debug.Log($"Track started: {track.Key}");
};

trackManager.OnTrackStopped += (track) => {
    Debug.Log($"Track stopped: {track.Key}");
};

// Get statistics
int totalTracks = trackManager.GetTrackCount();
int persistentCount = trackManager.GetPersistentTrackCount();

// Clean up dead tracks (called automatically, but can be manual)
trackManager.CleanupDeadTracks();
```

## Track Keys

Each track is identified by a unique key generated from:
- Line number in the script
- Statement kind (loop/oneshot)
- Clip name
- Instance index (for count > 1)

Format: `{lineNumber}_{kind}_{clip}_{instanceIndex}`

Example: `0_loop_ambient_0`

This key remains stable across script reloads as long as the script structure doesn't change, which is crucial for persistent track management.

## Public API Reference

### SatieRuntime Methods

| Method | Description |
|--------|-------------|
| `GetTrackManager()` | Get direct access to the track manager |
| `StopTrack(string key)` | Stop a specific track |
| `SetTrackMute(string key, bool muted)` | Mute/unmute a track |
| `SetTrackVolume(string key, float volume)` | Set track volume |
| `SetTrackPitch(string key, float pitch)` | Set track pitch |
| `GetTrack(string key)` | Get a track by key |
| `GetAllTracks()` | Get all active tracks |
| `GetPersistentTracks()` | Get only persistent tracks |
| `StopAllTracks(bool includePersistent)` | Stop all tracks |
| `MuteAllTracks(bool muted)` | Mute/unmute all tracks |
| `GetTrackCount()` | Get count of active tracks |
| `PrintTrackDebugInfo()` | Print debug info for all tracks |

### SatieTrack Properties

| Property | Type | Description |
|----------|------|-------------|
| `Key` | string | Unique identifier for this track |
| `Statement` | Statement | The statement that created this track |
| `Sources` | List<AudioSource> | All audio sources for this track |
| `Coroutine` | Coroutine | The coroutine running this track |
| `IsPersistent` | bool | Whether this track persists across reloads |
| `IsPlaying` | bool | Whether any source is currently playing |
| `CreatedAtTime` | float | When this track was created (Time.time) |

### SatieTrack Methods

| Method | Description |
|--------|-------------|
| `AddSource(AudioSource)` | Add an audio source to this track |
| `RemoveSource(AudioSource)` | Remove an audio source |
| `SetMute(bool)` | Mute/unmute all sources |
| `SetVolume(float)` | Set volume on all sources |
| `SetPitch(float)` | Set pitch on all sources |
| `Stop()` | Stop all sources |
| `Destroy()` | Destroy all sources and GameObjects |
| `GetDebugInfo()` | Get debug information string |

## Performance Considerations

- **Memory**: Each track maintains a list of AudioSources, which is lightweight
- **Lookup**: Track lookup is O(1) using dictionary
- **Iteration**: Iterating all tracks is O(n), efficient for typical use cases (< 100 tracks)
- **Cleanup**: Dead tracks are automatically removed when their sources are destroyed

## Migration from Old System

The new system is **fully backward compatible**. Your existing Satie scripts will work without modification. The changes are all internal to SatieRuntime.

Key differences:
- Old: Tracks stored in multiple disconnected dictionaries and lists
- New: Tracks unified in a single TrackManager with proper encapsulation
- Old: No way to access persistent tracks after creation
- New: Full API for querying and controlling all tracks

## Future Enhancements

The TrackManager architecture makes these features easy to add:

1. **Voice Limiting**: Maximum concurrent voices per clip
2. **Priority System**: Stop lowest-priority tracks when limit reached
3. **Track Groups**: Group tracks for batch control
4. **Fade Utilities**: Built-in fade in/out/crossfade
5. **Analytics**: Track play time, trigger count, etc.
6. **Serialization**: Save/load track state
7. **Remote Control**: Network-based track control for live performance

## Debugging Tips

```csharp
// Print all tracks
satieRuntime.PrintTrackDebugInfo();

// Find why a track won't stop
var track = satieRuntime.GetTrack("0_loop_ambient_0");
if (track != null)
{
    Debug.Log($"Sources: {track.Sources.Count}");
    Debug.Log($"Playing: {track.IsPlaying}");
    Debug.Log($"Persistent: {track.IsPersistent}");
}

// Monitor track count
void Update()
{
    Debug.Log($"Active tracks: {satieRuntime.GetTrackCount()}");
}
```

## Example Script Location

See `Assets/Architecture/Examples/SatieTrackControlExample.cs` for a comprehensive example with keyboard controls for testing all features.

## Questions?

This system follows standard industry patterns for audio voice management. If you need to add custom functionality, the TrackManager provides a clean extension point without cluttering SatieRuntime.
