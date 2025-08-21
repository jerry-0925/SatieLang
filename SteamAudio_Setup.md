# Steam Audio HRTF Setup for SatieLang

## Installation

### 1. Download and Install Steam Audio

**Manual Download Method:**
1. Go to: https://valvesoftware.github.io/steam-audio/downloads.html
2. Download the latest Unity plugin (e.g., `steamaudio_unity_4.5.3.zip`)
3. Extract the zip file
4. In Unity: Assets > Import Package > Custom Package
5. Navigate to the extracted folder and select `SteamAudioUnity.unitypackage`
6. Import all files

**Alternative: Unity Asset Store**
1. Open Unity Asset Store in browser
2. Search for "Steam Audio"
3. Add to My Assets
4. In Unity: Window > Package Manager
5. Switch dropdown to "My Assets"
6. Download and Import Steam Audio

### 2. Enable Steam Audio in Project

1. **Add Scripting Define Symbol:**
   - Edit > Project Settings > Player
   - Scroll to "Other Settings" > "Scripting Define Symbols"
   - Add: `STEAMAUDIO_ENABLED`
   - Click Apply

2. **Configure Audio Settings:**
   - Edit > Project Settings > Audio
   - Set "Spatializer Plugin" to: `Steam Audio Spatializer`
   - Set "Ambisonic Decoder Plugin" to: `Steam Audio Ambisonics`

### 3. Setup Your Scene

1. **Add Steam Audio Manager:**
   - GameObject > 3D Object > Steam Audio > Steam Audio Manager
   - Or manually add the component to an empty GameObject

2. **Configure Main Camera:**
   - Select your Main Camera
   - Ensure it has Unity's Audio Listener component (required)
   - Add Component > Steam Audio > Steam Audio Listener
   - The Steam Audio Listener works alongside Unity's Audio Listener (do NOT remove/disable it)

3. **Optional: Add Room/Environment:**
   - For reverb and occlusion effects
   - GameObject > 3D Object > Steam Audio > Steam Audio Probe Batch
   - Configure room size and material properties

## Using with SatieLang

The integration is automatic! When Steam Audio is installed:

1. All spatialized sounds (with `move` parameter) will use HRTF
2. The `useHRTF` checkbox appears in SatieRuntime inspector
3. Enable/disable HRTF per scene as needed

### How it Works:
- **AudioSource.spatialize = true** enables the spatializer plugin
- If Steam Audio Spatializer is selected in Project Settings, it provides HRTF
- If not configured, Unity's default spatializer is used (basic 3D panning)
- The SteamAudioSource component adds optional features (occlusion, reflections)

### Example .sat file:
```satie
# HRTF will be applied to these moving sources
oneshot "bird/1to4" every 2to5:
    volume = 0.8
    move = fly, -10to10, 0to10, -10to10, 0.5
    visual = sphere

# Static sounds also benefit from HRTF
loop "ambience/forest":
    volume = 0.3
    move = pos, 5, 2, -3
```

## Steam Audio Settings (in SatieRuntime)

The following settings are configured automatically:
- **Directivity**: 0.0 (omnidirectional)
- **Occlusion**: Enabled (raycast-based)
- **Transmission**: Enabled (frequency-dependent)
- **Reflections**: Enabled (real-time)

## Testing Your Setup

1. **Wear Headphones** (HRTF only works with headphones)

2. **Create a test scene:**
   - Add SatieRuntime to an empty GameObject
   - Create a simple .sat file with moving sounds
   - Enable "Use HRTF" in the inspector

3. **What to listen for:**
   - Clear front/back differentiation
   - Vertical positioning (up/down)
   - Smooth movement without clicks
   - Natural distance attenuation

## Performance Optimization

- **Max Sources**: Keep under 32-64 HRTF sources
- **Distance Culling**: Sounds beyond 50m can use simpler spatialization
- **LOD System**: Consider disabling HRTF for background/ambient sounds

## Troubleshooting

**"There are no audio listeners in the scene" error:**
- Ensure Unity's AudioListener component is present and enabled on Main Camera
- Steam Audio Listener requires Unity's AudioListener to be active
- Do NOT disable or remove Unity's AudioListener component

**No HRTF effect:**
- Check headphones are connected
- Verify Steam Audio Spatializer is selected in Audio settings
- Ensure STEAMAUDIO_ENABLED is in define symbols
- Check useHRTF is enabled in SatieRuntime

**Performance issues:**
- Reduce reflection quality in Steam Audio Manager
- Decrease occlusion rays from 16 to 8
- Disable transmission for non-critical sounds

**Build errors:**
- If Steam Audio components are missing, check the package is imported
- Ensure all platforms have Steam Audio native libraries

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Fully Supported | Best performance |
| macOS    | ✅ Fully Supported | Intel & Apple Silicon |
| Linux    | ✅ Fully Supported | Ubuntu 18.04+ tested |
| Android  | ✅ Supported | May need optimization |
| iOS      | ✅ Supported | Requires iOS 10+ |
| WebGL    | ❌ Not Supported | Use Resonance Audio instead |

## Advanced Features

### Custom HRTF Profiles
Steam Audio supports custom HRTF datasets:
1. Place .sofa files in StreamingAssets/SteamAudio/
2. Select in Steam Audio Manager > HRTF settings

### Acoustic Materials
Define surface properties for realistic reflections:
- Steam Audio > Create > Acoustic Material
- Assign to meshes with Steam Audio Geometry component

### Performance Profiling
- Window > Analysis > Profiler
- Check Audio DSP usage
- Target < 25% for smooth playback