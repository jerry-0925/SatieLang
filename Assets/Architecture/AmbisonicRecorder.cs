using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Records all AudioSources in the scene as First-Order Ambisonic (FOA) B-format audio.
/// Spatially encodes each source based on its position relative to the AudioListener.
/// Outputs a 4-channel WAV file (W, X, Y, Z channels).
/// </summary>
[RequireComponent(typeof(AudioListener))]
public class AmbisonicRecorder : MonoBehaviour
{
    [Header("Recording Controls")]
    [Tooltip("Start/stop recording")]
    public bool isRecording = false;

    [Header("Recording Settings")]
    [Tooltip("Output file name (without extension)")]
    public string outputFileName = "ambisonic_recording";

    [Tooltip("Sample rate for recording (must match Unity's audio settings)")]
    public int sampleRate = 48000;

    [Tooltip("Auto-add encoders to all AudioSources in scene on start (not needed with Satie)")]
    public bool autoAddEncoders = false;

    [Header("Status (Read-Only)")]
    [SerializeField] private float recordingDuration = 0f;
    [SerializeField] private string lastSavedPath = "";
    [SerializeField] private int activeEncoders = 0;

    // Recording state
    private bool wasRecording = false;
    private List<float> recordedSamplesW = new List<float>(); // Omni channel
    private List<float> recordedSamplesX = new List<float>(); // Front-back
    private List<float> recordedSamplesY = new List<float>(); // Left-right
    private List<float> recordedSamplesZ = new List<float>(); // Up-down

    private object lockObject = new object();
    private double recordingStartTime = 0; // DSP time when recording started

    // All encoders in the scene
    private List<AmbisonicSourceEncoder> encoders = new List<AmbisonicSourceEncoder>();

    void Start()
    {
        // Ensure sample rate matches Unity's audio settings
        sampleRate = AudioSettings.outputSampleRate;

        if (autoAddEncoders)
        {
            AddEncodersToAllSources();
        }
    }

    void Update()
    {
        // Detect recording start
        if (isRecording && !wasRecording)
        {
            StartRecording();
        }

        // Detect recording stop
        if (!isRecording && wasRecording)
        {
            StopRecording();
        }

        wasRecording = isRecording;

        // Update recording duration based on actual buffer size
        if (isRecording)
        {
            lock (lockObject)
            {
                recordingDuration = (float)recordedSamplesW.Count / sampleRate;
            }
        }

        // Update encoder count
        RefreshEncoders();
    }

    /// <summary>
    /// Automatically add AmbisonicSourceEncoder to all AudioSources in the scene.
    /// </summary>
    void AddEncodersToAllSources()
    {
        var allSources = FindObjectsOfType<AudioSource>();
        int added = 0;

        foreach (var source in allSources)
        {
            // Skip the AudioListener's AudioSource if it has one
            if (source.GetComponent<AudioListener>() != null)
                continue;

            // Add encoder if it doesn't have one
            if (source.GetComponent<AmbisonicSourceEncoder>() == null)
            {
                source.gameObject.AddComponent<AmbisonicSourceEncoder>();
                added++;
            }
        }

        Debug.Log($"[AmbisonicRecorder] Auto-added encoders to {added} AudioSources");
        RefreshEncoders();
    }

    /// <summary>
    /// Refresh the list of active encoders.
    /// </summary>
    void RefreshEncoders()
    {
        encoders.Clear();
        encoders.AddRange(FindObjectsOfType<AmbisonicSourceEncoder>());
        activeEncoders = encoders.Count;
    }

    void StartRecording()
    {
        RefreshEncoders();
        Debug.Log($"[AmbisonicRecorder] Recording started with {activeEncoders} encoders");
        lock (lockObject)
        {
            recordedSamplesW.Clear();
            recordedSamplesX.Clear();
            recordedSamplesY.Clear();
            recordedSamplesZ.Clear();
            recordingStartTime = AudioSettings.dspTime;
        }
        recordingDuration = 0f;
    }

    void StopRecording()
    {
        int sampleCount = recordedSamplesW.Count;
        Debug.Log($"[AmbisonicRecorder] Recording stopped. Duration: {recordingDuration:F2}s, Samples: {sampleCount}");

        if (sampleCount > 0)
        {
            SaveAmbisonicWAV();
        }
        else
        {
            Debug.LogWarning($"[AmbisonicRecorder] No audio data recorded. Samples: {sampleCount}");
        }
    }

    /// <summary>
    /// Register encoded audio from an encoder.
    /// Called by AmbisonicSourceEncoder from the audio thread.
    /// </summary>
    public void SubmitEncodedAudio(float[] w, float[] x, float[] y, float[] z, int frames, double dspTime)
    {
        if (!isRecording) return;

        // Calculate the sample offset based on DSP time
        double elapsedTime = dspTime - recordingStartTime;
        int sampleOffset = (int)(elapsedTime * sampleRate);

        // Add these samples to our recording buffers
        lock (lockObject)
        {
            // Ensure buffers are large enough
            int requiredSize = sampleOffset + frames;
            while (recordedSamplesW.Count < requiredSize)
            {
                recordedSamplesW.Add(0f);
                recordedSamplesX.Add(0f);
                recordedSamplesY.Add(0f);
                recordedSamplesZ.Add(0f);
            }

            // Mix in this encoder's contribution at the correct time position
            for (int i = 0; i < frames; i++)
            {
                int index = sampleOffset + i;
                if (index >= 0 && index < recordedSamplesW.Count)
                {
                    recordedSamplesW[index] += w[i];
                    recordedSamplesX[index] += x[i];
                    recordedSamplesY[index] += y[i];
                    recordedSamplesZ[index] += z[i];
                }
            }
        }
    }

    /// <summary>
    /// Saves the recorded ambisonic audio as a 4-channel WAV file (B-format).
    /// </summary>
    void SaveAmbisonicWAV()
    {
        try
        {
            Debug.Log($"[AmbisonicRecorder] Starting save process...");

            // Create output directory if it doesn't exist
            string outputDir = Path.Combine(Application.dataPath, "Recordings");
            Debug.Log($"[AmbisonicRecorder] Output directory: {outputDir}");

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                Debug.Log($"[AmbisonicRecorder] Created directory: {outputDir}");
            }

            // Generate timestamped filename
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"{outputFileName}_{timestamp}.wav";
            string fullPath = Path.Combine(outputDir, filename);
            Debug.Log($"[AmbisonicRecorder] Full path: {fullPath}");

            // Interleave the 4 channels (W, X, Y, Z)
            int totalSamples = recordedSamplesW.Count;
            Debug.Log($"[AmbisonicRecorder] Total samples to write: {totalSamples}");

            float[] interleavedData = new float[totalSamples * 4];

            for (int i = 0; i < totalSamples; i++)
            {
                interleavedData[i * 4 + 0] = recordedSamplesW[i]; // W
                interleavedData[i * 4 + 1] = recordedSamplesX[i]; // X
                interleavedData[i * 4 + 2] = recordedSamplesY[i]; // Y
                interleavedData[i * 4 + 3] = recordedSamplesZ[i]; // Z
            }

            Debug.Log($"[AmbisonicRecorder] Calling WriteWAVFile...");
            // Write WAV file
            WriteWAVFile(fullPath, interleavedData, 4, sampleRate);

            lastSavedPath = fullPath;
            Debug.Log($"[AmbisonicRecorder] *** SUCCESS *** Saved to: {fullPath}");
            Debug.Log($"[AmbisonicRecorder] Format: 4-channel B-format FOA, {sampleRate}Hz, {recordingDuration:F2}s");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AmbisonicRecorder] Failed to save WAV: {e.Message}");
            Debug.LogError($"[AmbisonicRecorder] Stack trace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Writes a WAV file with the given audio data.
    /// </summary>
    void WriteWAVFile(string filepath, float[] audioData, int channels, int sampleRate)
    {
        using (FileStream fileStream = new FileStream(filepath, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(fileStream))
        {
            int numSamples = audioData.Length;
            int bytesPerSample = 2; // 16-bit PCM
            int byteRate = sampleRate * channels * bytesPerSample;
            int blockAlign = channels * bytesPerSample;
            int dataSize = numSamples * bytesPerSample;

            // RIFF header
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataSize); // File size - 8
            writer.Write("WAVE".ToCharArray());

            // fmt chunk
            writer.Write("fmt ".ToCharArray());
            writer.Write(16); // fmt chunk size
            writer.Write((short)1); // Audio format (1 = PCM)
            writer.Write((short)channels); // Number of channels
            writer.Write(sampleRate); // Sample rate
            writer.Write(byteRate); // Byte rate
            writer.Write((short)blockAlign); // Block align
            writer.Write((short)(bytesPerSample * 8)); // Bits per sample

            // data chunk
            writer.Write("data".ToCharArray());
            writer.Write(dataSize);

            // Write audio samples as 16-bit PCM
            foreach (float sample in audioData)
            {
                // Clamp to [-1, 1] and convert to 16-bit
                short pcmSample = (short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
                writer.Write(pcmSample);
            }
        }
    }

    /// <summary>
    /// Manually trigger recording start via code.
    /// </summary>
    public void StartRecordingManual()
    {
        isRecording = true;
    }

    /// <summary>
    /// Manually trigger recording stop via code.
    /// </summary>
    public void StopRecordingManual()
    {
        isRecording = false;
    }

    /// <summary>
    /// Manually add encoders to all AudioSources (useful if sources are created dynamically).
    /// </summary>
    public void RefreshAllEncoders()
    {
        AddEncodersToAllSources();
    }
}
