using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Satie;
#if STEAMAUDIO_ENABLED
using SteamAudio;
#endif

public class SatieRuntime : MonoBehaviour
{
    [Tooltip(".sp script (TextAsset)")]
    [SerializeField] private TextAsset scriptFile;
    public TextAsset ScriptFile => scriptFile;
    
    [Header("Spatial Audio")]
    [Tooltip("Enable Steam Audio HRTF if available")]
    [SerializeField] private bool useHRTF = true;

    private readonly List<AudioSource> spawned  = new();
    private readonly List<Coroutine>   schedulers = new();
    
    void Start()
    {
        if (!scriptFile)
        {
            Debug.LogError("SonicPrompterRuntime: TextAsset missing.");
            return;
        }
        
        // Auto-setup Steam Audio if enabled and missing
        if (useHRTF)
        {
            SetupSteamAudio();
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

        foreach (var stmt in SatieParser.Parse(scriptFile.text))
            for (int i = 0; i < Mathf.Max(1, stmt.count); ++i)
                schedulers.Add(StartCoroutine(RunStmt(stmt)));

        Debug.Log($"[SP] Synced ({(fullReset ? "full" : "delta")}).");
    }

    IEnumerator RunStmt(Statement s)
    {
        yield return new WaitForSeconds(s.starts_at.Sample());
        if (s.kind == "loop")  yield return HandleLoop(s);
        else yield return HandleOneShot(s);
    }
    
    IEnumerator HandleLoop(Statement s)
    {
        var src = SpawnSource(s);
        if (!src) yield break;

        if (s.duration.isSet)
        {
            float fadeOut = s.fade_out.Sample();
            yield return StopAfter(src, s.duration.Sample(), fadeOut);
        }
    }
    
    IEnumerator HandleOneShot(Statement s)
    {
        AudioSource persistent = null;

        while (true)
        {
            if (s.overlap)
            {
                var src = SpawnSource(s);
                if (!src) yield break;
            }
            else
            {
                if (persistent == null)
                {
                    persistent = SpawnSource(s);
                    if (!persistent) yield break;
                }

                string clipName = SatieUtil.ResolveClip(s.clip);
                var newClip = Resources.Load<AudioClip>(SatieParser.PathFor(clipName));
                if (!newClip) { Debug.LogWarning($"[Satie] Audio clip '{clipName}' missing."); yield break; }

                persistent.clip = newClip;
                persistent.pitch = s.pitch.Sample();
                float targetVol  = s.volume.Sample();

                StartCoroutine(Fade(persistent, 0f, targetVol, s.fade_in.Sample()));

                persistent.time = 0f;
                persistent.Play();

                float fadeOut = s.fade_out.Sample();
                if (fadeOut > 0f)
                    StartCoroutine(StopAfter(persistent, persistent.clip.length, fadeOut));
            }

            yield return new WaitForSeconds(s.every.Sample());
        }
    }
    
    AudioSource SpawnSource(Statement s)
    {
        string clipName = SatieUtil.ResolveClip(s.clip);
        var clip = Resources.Load<AudioClip>(SatieParser.PathFor(clipName));
        if (!clip)
        {
            Debug.LogWarning($"[Satie] Audio clip '{clipName}' not found. "
                             + $"Looked for Resources/{SatieParser.PathFor(clipName)}.*");
            return null;
        }
        
        var go = new GameObject($"[SP] {clipName}");
        go.transform.SetParent(transform);

        var src = go.AddComponent<AudioSource>();
        spawned.Add(src);

        src.clip = clip;
        src.loop = (s.kind == "loop");
        src.volume = 0f;
        src.pitch = s.pitch.Sample();
        src.spatialBlend = (s.wanderType == Statement.WanderType.None) ? 0f : 1f;
        
        // Enable spatializer plugin if this is a 3D source
        if (s.wanderType != Statement.WanderType.None)
        {
            src.spatialize = true;  // This enables the spatializer plugin (Steam Audio if configured)
            src.spatializePostEffects = true;  // Apply spatialization after effects
            
            // Set 3D sound settings for better spatialization
            src.dopplerLevel = 0.5f;
            src.spread = 0f;  // 0 = point source, better for HRTF
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1f;
            src.maxDistance = 100f;
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
        
        // Add Steam Audio HRTF support if available and source is spatialized
        if (useHRTF && s.wanderType != Statement.WanderType.None)
        {
            AddSteamAudioHRTF(go);
        }

        StartCoroutine(Fade(src, 0f, s.volume.Sample(), s.fade_in.Sample()));
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
    
    void SetupSteamAudio()
    {
#if STEAMAUDIO_ENABLED
        // Check if Steam Audio Manager exists, create if missing
        var manager = FindObjectOfType<SteamAudioManager>();
        if (manager == null)
        {
            Debug.Log("[Satie] Creating Steam Audio Manager for HRTF support.");
            var managerGO = new GameObject("Steam Audio Manager");
            manager = managerGO.AddComponent<SteamAudioManager>();
        }
        
        // Check if main camera has Steam Audio Listener
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var steamListener = mainCamera.GetComponent<SteamAudioListener>();
            var audioListener = mainCamera.GetComponent<AudioListener>();
            
            // Ensure Unity's AudioListener exists (required by Steam Audio)
            if (audioListener == null)
            {
                Debug.Log("[Satie] Adding Unity AudioListener to Main Camera (required by Steam Audio).");
                audioListener = mainCamera.gameObject.AddComponent<AudioListener>();
            }
            
            // Add Steam Audio Listener component if missing
            if (steamListener == null)
            {
                Debug.Log("[Satie] Adding Steam Audio Listener to Main Camera for HRTF support.");
                steamListener = mainCamera.gameObject.AddComponent<SteamAudioListener>();
                
                // Configure Steam Audio Listener for baked reverb if needed
                steamListener.applyReverb = false; // Start with reverb disabled for performance
                steamListener.reverbType = ReverbType.Realtime;
            }
        }
        else
        {
            Debug.LogWarning("[Satie] No Main Camera found. Please ensure a camera with AudioListener exists in the scene.");
        }
#else
        Debug.Log("[Satie] Steam Audio not enabled. Using Unity's default spatializer.");
#endif
    }
    
    void AddSteamAudioHRTF(GameObject go)
    {
#if STEAMAUDIO_ENABLED
        // Steam Audio Manager should exist by now (created in SetupSteamAudio)
        var manager = FindObjectOfType<SteamAudioManager>();
        if (manager == null)
        {
            Debug.LogWarning("[Satie] Steam Audio Manager still not found. Basic spatialization will be used.");
            return;
        }
        
        try
        {
            // The Steam Audio Source component provides additional features beyond basic HRTF
            // HRTF is actually handled by the spatializer plugin via AudioSource.spatialize = true
            var steamSource = go.AddComponent<SteamAudioSource>();
            
            // Configure for best spatial quality
            steamSource.directivity = false;          // Omnidirectional by default
            steamSource.dipoleWeight = 0.0f;          // No dipole pattern  
            steamSource.dipolePower = 1.0f;
            
            // Advanced features (optional, can impact performance)
            steamSource.occlusion = false;            // Start with occlusion off for performance
            steamSource.occlusionType = OcclusionType.Raycast;
            steamSource.occlusionRadius = 0.1f;
            
            steamSource.transmission = false;         // Start with transmission off
            steamSource.transmissionType = TransmissionType.FrequencyDependent;
            
            steamSource.reflections = false;          // Start with reflections off
            steamSource.reflectionsType = ReflectionsType.Realtime;
            
            // Note: The actual HRTF processing happens through Unity's spatializer system
            // when AudioSource.spatialize = true and Steam Audio Spatializer is selected
            // in Project Settings. The SteamAudioSource component adds extra features.
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Satie] Failed to add Steam Audio Source component: {e.Message}. Using basic spatialization.");
        }
#else
        // Steam Audio not available - Unity will use its default spatializer
        // The AudioSource.spatialize flag will still work with Unity's built-in spatializer
        if (Application.isPlaying && useHRTF)
        {
            Debug.Log("[Satie] Using Unity's default spatializer. For HRTF support:" +
                     "\n1. Steam Audio package is already in manifest.json" +
                     "\n2. Add STEAMAUDIO_ENABLED to Scripting Define Symbols" +
                     "\n3. Set Project Settings > Audio > Spatializer Plugin to 'Steam Audio Spatializer'");
        }
#endif
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
        foreach (var co in schedulers)
            if (co != null) StopCoroutine(co);
        schedulers.Clear();

        foreach (var src in spawned)
            if (src) Destroy(src.gameObject);
        spawned.Clear();
    }
}
