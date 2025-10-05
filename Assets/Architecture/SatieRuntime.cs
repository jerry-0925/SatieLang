using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Satie;

public class SatieRuntime : MonoBehaviour
{
    [Tooltip(".sp script (TextAsset)")]
    [SerializeField] private TextAsset scriptFile;
    public TextAsset ScriptFile => scriptFile;

    private readonly List<AudioSource> spawned  = new();
    private readonly List<Coroutine>   schedulers = new();

    // Components
    private SatieSpatialAudio spatialAudio;
    
    void Start()
    {
        if (!scriptFile)
        {
            Debug.LogError("SatieRuntime: TextAsset missing.");
            return;
        }

        // Get spatial audio component
        spatialAudio = GetComponent<SatieSpatialAudio>();

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
        Debug.Log($"[HandleOneShot] clip={s.clip}, every.isSet={s.every.isSet}, every.min={s.every.min}, every.max={s.every.max}");

        // If no 'every' is set, play once and exit
        if (!s.every.isSet)
        {
            Debug.Log($"[HandleOneShot] Playing once and exiting");
            var src = SpawnSource(s);
            yield break;
        }

        Debug.Log($"[HandleOneShot] Entering repeat loop");
        // Repeating oneshot logic
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
        foreach (var co in schedulers)
            if (co != null) StopCoroutine(co);
        schedulers.Clear();

        foreach (var src in spawned)
            if (src) Destroy(src.gameObject);
        spawned.Clear();
    }
}
