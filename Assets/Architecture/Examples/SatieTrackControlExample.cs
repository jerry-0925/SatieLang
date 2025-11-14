using System.Collections;
using UnityEngine;
using Satie;

/// <summary>
/// Example script demonstrating how to control Satie tracks programmatically.
/// Attach this to a GameObject with a SatieRuntime component.
/// </summary>
public class SatieTrackControlExample : MonoBehaviour
{
    [SerializeField] private SatieRuntime satieRuntime;

    void Start()
    {
        if (!satieRuntime)
        {
            satieRuntime = GetComponent<SatieRuntime>();
        }
    }

    void Update()
    {
        // Example 1: Print all active tracks when pressing 'T'
        if (Input.GetKeyDown(KeyCode.T))
        {
            PrintAllTracks();
        }

        // Example 2: Mute all persistent tracks when pressing 'M'
        if (Input.GetKeyDown(KeyCode.M))
        {
            MutePersistentTracks();
        }

        // Example 3: Stop a specific track by key (if you know it)
        if (Input.GetKeyDown(KeyCode.S))
        {
            StopFirstPersistentTrack();
        }

        // Example 4: Change volume of all tracks when pressing Up/Down
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            AdjustAllVolumes(0.1f);
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            AdjustAllVolumes(-0.1f);
        }
    }

    void PrintAllTracks()
    {
        Debug.Log("=== Active Tracks ===");
        var tracks = satieRuntime.GetAllTracks();

        foreach (var track in tracks)
        {
            Debug.Log(track.GetDebugInfo());
        }

        Debug.Log($"Total: {satieRuntime.GetTrackCount()} tracks");
    }

    void MutePersistentTracks()
    {
        var persistentTracks = satieRuntime.GetPersistentTracks();

        foreach (var track in persistentTracks)
        {
            bool currentMute = track.Sources.Count > 0 ? track.Sources[0].mute : false;
            satieRuntime.SetTrackMute(track.Key, !currentMute);
            Debug.Log($"Toggled mute for track: {track.Key}");
        }
    }

    void StopFirstPersistentTrack()
    {
        var persistentTracks = satieRuntime.GetPersistentTracks();

        foreach (var track in persistentTracks)
        {
            Debug.Log($"Stopping track: {track.Key}");
            satieRuntime.StopTrack(track.Key);
            return; // Only stop the first one
        }

        Debug.Log("No persistent tracks to stop");
    }

    void AdjustAllVolumes(float delta)
    {
        var tracks = satieRuntime.GetAllTracks();

        foreach (var track in tracks)
        {
            if (track.Sources.Count > 0)
            {
                float currentVol = track.Sources[0].volume;
                float newVol = Mathf.Clamp01(currentVol + delta);
                satieRuntime.SetTrackVolume(track.Key, newVol);
            }
        }

        Debug.Log($"Adjusted all volumes by {delta}");
    }

    // ===== Advanced Examples =====

    /// <summary>
    /// Example: Find a track by clip name
    /// </summary>
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

    /// <summary>
    /// Example: Fade out a specific track over time
    /// </summary>
    public void FadeOutTrack(string trackKey, float duration)
    {
        var track = satieRuntime.GetTrack(trackKey);
        if (track != null)
        {
            StartCoroutine(FadeOutCoroutine(track, duration));
        }
    }

    private IEnumerator FadeOutCoroutine(SatieTrack track, float duration)
    {
        float startTime = Time.time;
        float startVolume = track.Sources.Count > 0 ? track.Sources[0].volume : 1f;

        while (Time.time - startTime < duration)
        {
            float t = (Time.time - startTime) / duration;
            float newVolume = Mathf.Lerp(startVolume, 0f, t);
            track.SetVolume(newVolume);
            yield return null;
        }

        track.SetVolume(0f);
        satieRuntime.StopTrack(track.Key);
    }

    /// <summary>
    /// Example: Stop all tracks of a specific kind (loop or oneshot)
    /// </summary>
    public void StopTracksByKind(string kind)
    {
        var tracks = satieRuntime.GetAllTracks();
        var tracksToStop = new System.Collections.Generic.List<string>();

        foreach (var track in tracks)
        {
            if (track.Statement.kind == kind)
            {
                tracksToStop.Add(track.Key);
            }
        }

        foreach (var key in tracksToStop)
        {
            satieRuntime.StopTrack(key);
            Debug.Log($"Stopped {kind} track: {key}");
        }
    }

    /// <summary>
    /// Example: Get track statistics
    /// </summary>
    public void PrintTrackStatistics()
    {
        var allTracks = satieRuntime.GetAllTracks();
        var persistentTracks = satieRuntime.GetPersistentTracks();

        int loopCount = 0;
        int oneshotCount = 0;
        int playingCount = 0;
        int persistentCount = 0;

        foreach (var track in allTracks)
        {
            if (track.Statement.kind == "loop") loopCount++;
            else oneshotCount++;

            if (track.IsPlaying) playingCount++;
        }

        foreach (var track in persistentTracks)
        {
            persistentCount++;
        }

        Debug.Log($"=== Track Statistics ===");
        Debug.Log($"Total: {satieRuntime.GetTrackCount()}");
        Debug.Log($"Persistent: {persistentCount}");
        Debug.Log($"Loops: {loopCount}");
        Debug.Log($"OneShots: {oneshotCount}");
        Debug.Log($"Currently Playing: {playingCount}");
    }
}
