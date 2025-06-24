using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Satie;

public class SatieRuntime : MonoBehaviour
{
    [Tooltip(".sp script (TextAsset)")]
    [SerializeField] private TextAsset scriptFile;

    private readonly List<AudioSource> spawned  = new();
    private readonly List<Coroutine>   schedulers = new();
    
    void Start()
    {
        if (!scriptFile)
        {
            Debug.LogError("SonicPrompterRuntime: TextAsset missing.");
            return;
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
    
    void Sync(bool fullReset)
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
            Vector3 p = new Vector3(
                Random.Range(s.areaMin.x, s.areaMax.x),
                Random.Range(s.areaMin.y, s.areaMax.y),
                Random.Range(s.areaMin.z, s.areaMax.z));
            go.transform.position = p;
        }

        if (s.visualize) AddTrail(go);

        StartCoroutine(Fade(src, 0f, s.volume.Sample(), s.fade_in.Sample()));
        return src;
    }

    void AddTrail(GameObject go)
    {
        var tr = go.AddComponent<TrailRenderer>();
        tr.widthMultiplier = 0.1f;
        tr.time = 5f;
        tr.material = new Material(Shader.Find("Sprites/Default"));
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
