using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Satie;

public class SatieRuntime : MonoBehaviour
{
    [Tooltip(".sp script (TextAsset)")]
    [SerializeField] private TextAsset scriptFile;
    public TextAsset ScriptFile => scriptFile;

    // Track manager handles all voice lifecycle
    private SatieTrackManager trackManager;

    // Mixer groups for DAW-style control
    [SerializeField] private List<SatieMixerGroup> mixerGroups = new List<SatieMixerGroup>();

    // Master controls
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] private bool masterMute = false;

    // Recording
    [Header("Ambisonic Recording")]
    [Tooltip("Reference to AmbisonicRecorder component for scene recording (auto-detected if not set)")]
    [SerializeField] private AmbisonicRecorder ambisonicRecorder;

    // Components
    private SatieSpatialAudio spatialAudio;

    void Start()
    {
        if (!scriptFile)
        {
            Debug.LogError("SatieRuntime: TextAsset missing.");
            return;
        }

        // Initialize track manager BEFORE Sync
        trackManager = new SatieTrackManager(this);

        // Get spatial audio component
        spatialAudio = GetComponent<SatieSpatialAudio>();

        // Auto-detect ambisonic recorder if not set
        if (ambisonicRecorder == null)
        {
            ambisonicRecorder = FindObjectOfType<AmbisonicRecorder>();
            if (ambisonicRecorder != null)
            {
                Debug.Log("[SatieRuntime] Auto-detected AmbisonicRecorder");
            }
        }

        Sync(fullReset: true);
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && !Input.GetKey(KeyCode.LeftShift)) Sync(false);
        if (Input.GetKeyDown(KeyCode.R) &&  Input.GetKey(KeyCode.LeftShift)) Sync(true);
    }
#endif
    
    public void Sync(bool fullReset)
    {
        if (fullReset) HardReset();

        // Parse all statements first to check if any are soloed
        var allStatements = SatieParser.Parse(scriptFile.text);
        bool anySolo = allStatements.Any(s => s.solo);

        if (anySolo)
            Debug.Log($"[SP] Solo mode active - only solo statements will play");

        // Build a map of current statement keys and their persistent status
        var currentPersistentKeys = new HashSet<string>();
        int lineNumber = 0;
        foreach (var stmt in allStatements)
        {
            for (int i = 0; i < Mathf.Max(1, stmt.count); ++i)
            {
                string stmtKey = $"{lineNumber}_{stmt.kind}_{stmt.clip}_{i}";
                if (stmt.persistent)
                    currentPersistentKeys.Add(stmtKey);
            }
            lineNumber++;
        }

        // Stop any previously persistent tracks that are no longer marked as persistent
        var keysToStop = new List<string>();
        foreach (var track in trackManager.GetPersistentTracks())
        {
            if (!currentPersistentKeys.Contains(track.Key))
            {
                keysToStop.Add(track.Key);
            }
        }
        foreach (var key in keysToStop)
        {
            Debug.Log($"[SP] Stopping previously persistent track (no longer persistent): {key}");
            trackManager.StopTrack(key);
        }

        // Now process all statements
        lineNumber = 0;
        foreach (var stmt in allStatements)
        {
            // Determine if this statement should actually spawn based on solo logic
            bool shouldSpawn = true;

            if (anySolo)
            {
                // If anything is soloed, only spawn solo statements
                shouldSpawn = stmt.solo;
            }
            // Note: mute doesn't affect spawning, only volume (handled in SpawnSource)

            for (int i = 0; i < Mathf.Max(1, stmt.count); ++i)
            {
                // Generate stable key based on script content and position
                // This key will be the same across parses if the script structure doesn't change
                string stmtKey = $"{lineNumber}_{stmt.kind}_{stmt.clip}_{i}";

                // Check if this track is already running
                bool isAlreadyRunning = trackManager.HasTrack(stmtKey);

                // If unsoloed (when solo mode is active), stop it
                if (!shouldSpawn)
                {
                    Debug.Log($"[SP] Skipping non-solo statement: {stmt.clip} (solo mode active)");
                    if (isAlreadyRunning)
                    {
                        trackManager.StopTrack(stmtKey);
                    }
                    continue;
                }

                // Update properties of already-running tracks
                if (isAlreadyRunning)
                {
                    UpdateTrackMuteState(stmtKey, stmt.mute, anySolo && !stmt.solo);
                    continue;
                }

                // Create new track
                var track = trackManager.CreateTrack(stmtKey, stmt);
                var coroutine = StartCoroutine(RunStmt(track, anySolo));
                track.Coroutine = coroutine;
            }
            lineNumber++;
        }

        Debug.Log($"[SP] Synced ({(fullReset ? "full" : "delta")}).");
    }

    void UpdateTrackMuteState(string stmtKey, bool explicitMute, bool implicitMuteFromSolo)
    {
        bool shouldBeMuted = explicitMute || implicitMuteFromSolo;
        trackManager.SetTrackMute(stmtKey, shouldBeMuted);
    }

    IEnumerator RunStmt(SatieTrack track, bool anySoloActive)
    {
        Statement s = track.Statement;

        yield return new WaitForSeconds(s.starts_at.Sample());

        if (s.kind == "loop")  yield return HandleLoop(track, anySoloActive);
        else yield return HandleOneShot(track, anySoloActive);

        // Cleanup when statement finishes (non-persistent tracks auto-remove)
        if (!track.IsPersistent)
        {
            trackManager.StopTrack(track.Key);
        }
    }
    
    IEnumerator HandleLoop(SatieTrack track, bool anySoloActive)
    {
        Statement s = track.Statement;
        var src = SpawnSource(s, anySoloActive);
        if (!src) yield break;

        // Track this source in the track
        track.AddSource(src);

        if (s.duration.isSet)
        {
            float fadeOut = s.fade_out.Sample();
            yield return StopAfter(src, s.duration.Sample(), fadeOut);
        }
        else
        {
            // Loop with no duration - play indefinitely, never return
            while (true)
            {
                yield return null;
            }
        }
    }

    IEnumerator HandleOneShot(SatieTrack track, bool anySoloActive)
    {
        Statement s = track.Statement;
        Debug.Log($"[HandleOneShot] clip={s.clip}, every.isSet={s.every.isSet}, every.min={s.every.min}, every.max={s.every.max}");

        // If no 'every' is set, play once and exit
        if (!s.every.isSet)
        {
            Debug.Log($"[HandleOneShot] Playing once and exiting");
            var src = SpawnSource(s, anySoloActive);
            if (src) track.AddSource(src);
            yield break;
        }

        Debug.Log($"[HandleOneShot] Entering repeat loop");
        // Repeating oneshot logic
        AudioSource persistent = null;

        while (true)
        {
            if (s.overlap)
            {
                var src = SpawnSource(s, anySoloActive);
                if (!src) yield break;
                track.AddSource(src);
            }
            else
            {
                if (persistent == null)
                {
                    persistent = SpawnSource(s, anySoloActive);
                    if (!persistent) yield break;
                    track.AddSource(persistent);
                }

                string clipName = SatieUtil.ResolveClip(s.clip);
                var newClip = Resources.Load<AudioClip>(SatieParser.PathFor(clipName));
                if (!newClip) { Debug.LogWarning($"[Satie] Audio clip '{clipName}' missing."); yield break; }

                persistent.clip = newClip;

                if (s.pitchInterpolation == null)
                    persistent.pitch = s.pitch.Sample();

                float targetVol  = s.volume.Sample();

                // Handle initial volume based on interpolation type
                if (s.volumeInterpolation != null &&
                    s.volumeInterpolation.interpolationType == InterpolationType.Goto)
                {
                    // For goto, start at the min value to avoid clicks
                    persistent.volume = s.volumeInterpolation.minValue;
                }
                else if (s.volumeInterpolation == null && s.fade_in.isSet)
                    StartCoroutine(Fade(persistent, 0f, targetVol, s.fade_in.Sample()));
                else if (s.volumeInterpolation == null)
                    persistent.volume = targetVol;

                persistent.time = 0f;
                persistent.Play();

                float fadeOut = s.fade_out.Sample();
                if (fadeOut > 0f)
                    StartCoroutine(StopAfter(persistent, persistent.clip.length, fadeOut));
            }

            yield return new WaitForSeconds(s.every.Sample());
        }
    }
    
    AudioSource SpawnSource(Statement s, bool anySoloActive)
    {
        string clipName = SatieUtil.ResolveClip(s.clip);
        string fullPath = SatieParser.PathFor(clipName);

        var clip = Resources.Load<AudioClip>(fullPath);
        if (!clip)
        {
            Debug.LogWarning($"[Satie] Audio clip '{clipName}' not found. "
                             + $"Looked for Resources/{fullPath}.*");
            return null;
        }

        var go = new GameObject($"[SP] {clipName}");
        go.transform.SetParent(transform);

        var src = go.AddComponent<AudioSource>();

        src.clip = clip;
        src.loop = (s.kind == "loop");

        // Set mute state: explicit mute flag OR implicitly muted if solo is active and this isn't soloed
        bool shouldBeMuted = s.mute || (anySoloActive && !s.solo);
        src.mute = shouldBeMuted;

        // Initialize volume based on interpolation type to avoid clicks
        if (s.volumeInterpolation != null &&
            s.volumeInterpolation.interpolationType == InterpolationType.Goto)
        {
            src.volume = s.volumeInterpolation.minValue;
        }
        else
        {
            src.volume = 0f;  // Default to 0 for fade-ins or normal volume setting
        }

        // Initialize pitch based on interpolation type
        if (s.pitchInterpolation != null &&
            s.pitchInterpolation.interpolationType == InterpolationType.Goto)
        {
            src.pitch = s.pitchInterpolation.minValue;
        }
        else
        {
            src.pitch = s.pitch.Sample();
        }

        if (s.volumeInterpolation != null || s.pitchInterpolation != null)
        {
            var interpComp = go.AddComponent<InterpolatedAudioSource>();
            interpComp.SetupInterpolations(s);
        }

        // Configure spatial audio using the spatial audio component
        bool is3D = s.wanderType != Statement.WanderType.None;
        if (spatialAudio != null)
        {
            spatialAudio.ConfigureAudioSource(src, is3D);
        }
        else
        {
            // Fallback configuration if no spatial audio component
            src.spatialBlend = is3D ? 1f : 0f;
            if (is3D)
            {
                src.spatialize = true;
                src.spatializePostEffects = true;
                src.dopplerLevel = 0.5f;
                src.spread = 0f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 1f;
                src.maxDistance = 100f;
            }
        }
        
        src.Play();

        if (s.wanderType == Statement.WanderType.Walk ||
            s.wanderType == Statement.WanderType.Fly)
        {
            var mover = go.AddComponent<SSpatial>();
            mover.type = s.wanderType;
            mover.minPos = s.areaMin;
            mover.maxPos = s.areaMax;
            mover.hz = s.wanderHz.Sample();
        }
        else if (s.wanderType == Statement.WanderType.Fixed)
        {
            UnityEngine.Vector3 p = new UnityEngine.Vector3(
                Random.Range(s.areaMin.x, s.areaMax.x),
                Random.Range(s.areaMin.y, s.areaMax.y),
                Random.Range(s.areaMin.z, s.areaMax.z));
            go.transform.position = p;
        }

        AddVisuals(go, s);

        // Add Steam Audio components if available and source is spatialized
        if (spatialAudio != null && s.wanderType != Statement.WanderType.None)
        {
            spatialAudio.AddSteamAudioComponents(go);
        }

        // Add ambisonic encoder if recorder is present
        if (ambisonicRecorder != null)
        {
            go.AddComponent<AmbisonicSourceEncoder>();
            Debug.Log($"[SatieRuntime] Added AmbisonicSourceEncoder to {go.name}");
        }
        else
        {
            Debug.LogWarning($"[SatieRuntime] ambisonicRecorder is NULL, cannot add encoder to {go.name}");
        }

        // Handle initial volume based on interpolation type
        if (s.volumeInterpolation != null &&
            s.volumeInterpolation.interpolationType == InterpolationType.Goto)
        {
            // For goto, start at the min value to avoid clicks
            src.volume = s.volumeInterpolation.minValue;
        }
        else if (s.volumeInterpolation == null && s.fade_in.isSet)
        {
            StartCoroutine(Fade(src, 0f, s.volume.Sample(), s.fade_in.Sample()));
        }
        else if (s.volumeInterpolation == null)
        {
            src.volume = s.volume.Sample();
        }

        return src;
    }

    void AddVisuals(GameObject go, Statement s)
    {
        foreach (string visual in s.visual)
        {
            if (visual.StartsWith("object:"))
            {
                // Load prefab from Resources
                string prefabPath = visual.Substring(7); // Remove "object:" prefix
                string fullPath = $"Prefabs/{SatieUtil.ResolveClip(prefabPath)}";
                GameObject prefab = Resources.Load<GameObject>(fullPath);
                
                if (prefab != null)
                {
                    GameObject instance = Instantiate(prefab, go.transform);
                    instance.transform.localPosition = UnityEngine.Vector3.zero;
                }
                else
                {
                    Debug.LogWarning($"[Satie] Prefab '{fullPath}' not found in Resources.");
                }
            }
            else
            {
                // Handle primitive visuals
                switch (visual)
                {
                    case "trail":
                        AddTrail(go);
                        break;
                    case "sphere":
                        AddPrimitive(go, PrimitiveType.Sphere);
                        break;
                    case "cube":
                        AddPrimitive(go, PrimitiveType.Cube);
                        break;
                    case "cylinder":
                        AddPrimitive(go, PrimitiveType.Cylinder);
                        break;
                    case "capsule":
                        AddPrimitive(go, PrimitiveType.Capsule);
                        break;
                    case "plane":
                        AddPrimitive(go, PrimitiveType.Plane);
                        break;
                    case "quad":
                        AddPrimitive(go, PrimitiveType.Quad);
                        break;
                    default:
                        Debug.LogWarning($"[Satie] Unknown visual type: '{visual}'");
                        break;
                }
            }
        }
    }

    void AddTrail(GameObject go)
    {
        var tr = go.AddComponent<TrailRenderer>();
        tr.widthMultiplier = 0.1f;
        tr.time = 5f;
        tr.material = new UnityEngine.Material(Shader.Find("Sprites/Default"));
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Color start = new Color(Random.value, Random.value, Random.value, 1f);
        Color end   = new Color(start.r, start.g, start.b, 0f);

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),    new GradientAlphaKey(0f, 1f) }
        );
        tr.colorGradient = grad;
    }

    void AddPrimitive(GameObject go, PrimitiveType type)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.transform.SetParent(go.transform);
        primitive.transform.localPosition = UnityEngine.Vector3.zero;
        primitive.transform.localScale = UnityEngine.Vector3.one * 0.5f; // Scale down a bit
        
        // Remove collider as we don't need physics
        Collider col = primitive.GetComponent<Collider>();
        if (col) Destroy(col);
        
        // Add a random color to the material
        Renderer rend = primitive.GetComponent<Renderer>();
        if (rend)
        {
            rend.material = new UnityEngine.Material(Shader.Find("Standard"));
            rend.material.color = new Color(Random.value, Random.value, Random.value, 0.8f);
        }
    }

    IEnumerator StopAfter(AudioSource src, float secs, float fadeOut)
    {
        yield return new WaitForSeconds(secs - fadeOut);
        yield return Fade(src, src.volume, 0f, fadeOut);
        if (src) src.Stop();
    }

    IEnumerator Fade(AudioSource src, float from, float to, float dur)
    {
        if (dur <= 0f) { if (src) src.volume = to; yield break; }
        float t = 0f;
        while (t < dur && src)
        {
            src.volume = Mathf.Lerp(from, to, t / dur);
            t += Time.deltaTime;
            yield return null;
        }
        if (src) src.volume = to;
    }

    void HardReset()
    {
        // Safety check - trackManager might not be initialized yet
        if (trackManager == null) return;

        // Stop all non-persistent tracks
        trackManager.StopAllTracks(includePersistent: false);

        int persistentCount = trackManager.GetPersistentTrackCount();
        Debug.Log($"[SP] HardReset complete. {persistentCount} persistent tracks remain.");
    }

    // ===== Public API for Track Control =====

    /// <summary>
    /// Get the track manager for direct access to all tracks
    /// </summary>
    public SatieTrackManager GetTrackManager()
    {
        return trackManager;
    }

    /// <summary>
    /// Stop a specific track by its key
    /// </summary>
    public void StopTrack(string trackKey)
    {
        trackManager?.StopTrack(trackKey);
    }

    /// <summary>
    /// Mute/unmute a specific track
    /// </summary>
    public void SetTrackMute(string trackKey, bool muted)
    {
        trackManager?.SetTrackMute(trackKey, muted);
    }

    /// <summary>
    /// Set volume for a specific track
    /// </summary>
    public void SetTrackVolume(string trackKey, float volume)
    {
        trackManager?.SetTrackVolume(trackKey, volume);
    }

    /// <summary>
    /// Set pitch for a specific track
    /// </summary>
    public void SetTrackPitch(string trackKey, float pitch)
    {
        trackManager?.SetTrackPitch(trackKey, pitch);
    }

    /// <summary>
    /// Get a track by its key for more advanced control
    /// </summary>
    public SatieTrack GetTrack(string trackKey)
    {
        return trackManager?.GetTrack(trackKey);
    }

    /// <summary>
    /// Get all currently active tracks
    /// </summary>
    public IEnumerable<SatieTrack> GetAllTracks()
    {
        return trackManager?.GetAllTracks() ?? Enumerable.Empty<SatieTrack>();
    }

    /// <summary>
    /// Get all persistent tracks
    /// </summary>
    public IEnumerable<SatieTrack> GetPersistentTracks()
    {
        return trackManager?.GetPersistentTracks() ?? Enumerable.Empty<SatieTrack>();
    }

    /// <summary>
    /// Stop all tracks (optionally include persistent ones)
    /// </summary>
    public void StopAllTracks(bool includePersistent = true)
    {
        trackManager?.StopAllTracks(includePersistent);
    }

    /// <summary>
    /// Mute/unmute all tracks
    /// </summary>
    public void MuteAllTracks(bool muted)
    {
        trackManager?.MuteAllTracks(muted);
    }

    /// <summary>
    /// Get count of active tracks
    /// </summary>
    public int GetTrackCount()
    {
        return trackManager?.GetTrackCount() ?? 0;
    }

    /// <summary>
    /// Print debug info for all tracks
    /// </summary>
    public void PrintTrackDebugInfo()
    {
        trackManager?.PrintDebugInfo();
    }

    // ===== Mixer Group API =====

    /// <summary>
    /// Get all mixer groups
    /// </summary>
    public List<SatieMixerGroup> GetMixerGroups()
    {
        return mixerGroups;
    }

    /// <summary>
    /// Add a new mixer group
    /// </summary>
    public SatieMixerGroup AddMixerGroup(string name)
    {
        var group = new SatieMixerGroup(name);
        mixerGroups.Add(group);
        return group;
    }

    /// <summary>
    /// Remove a mixer group
    /// </summary>
    public void RemoveMixerGroup(SatieMixerGroup group)
    {
        mixerGroups.Remove(group);
    }

    /// <summary>
    /// Apply mixer group settings to all tracks
    /// </summary>
    public void ApplyMixerGroups()
    {
        if (trackManager == null) return;

        // Check if any group is soloed
        bool anyGroupSoloed = mixerGroups.Any(g => g.solo);

        // Apply each group's settings to its tracks
        foreach (var group in mixerGroups)
        {
            group.ApplyToTracks(trackManager, anyGroupSoloed);
        }

        // Apply master volume and mute to all tracks
        foreach (var track in trackManager.GetAllTracks())
        {
            if (track.Sources.Count > 0)
            {
                foreach (var src in track.Sources)
                {
                    if (src)
                    {
                        // Apply master volume on top of existing volume
                        src.volume *= masterVolume;
                        // Apply master mute
                        if (masterMute)
                            src.mute = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get master volume
    /// </summary>
    public float GetMasterVolume()
    {
        return masterVolume;
    }

    /// <summary>
    /// Set master volume
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        ApplyMixerGroups();
    }

    /// <summary>
    /// Get master mute state
    /// </summary>
    public bool GetMasterMute()
    {
        return masterMute;
    }

    /// <summary>
    /// Set master mute state
    /// </summary>
    public void SetMasterMute(bool muted)
    {
        masterMute = muted;
        ApplyMixerGroups();
    }
}
