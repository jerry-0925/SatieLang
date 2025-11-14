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

    // Temporary buffers for collecting encoder contributions per frame
    private float[] frameBufferW;
    private float[] frameBufferX;
    private float[] frameBufferY;
    private float[] frameBufferZ;
    private int currentFrameSize = 0;

    // All encoders in the scene
    private List<AmbisonicSourceEncoder> encoders = new List<AmbisonicSourceEncoder>();
    private AmbisonicSourceEncoder[] encodersSnapshot = new AmbisonicSourceEncoder[0]; // Thread-safe snapshot for audio thread

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
    /// Called continuously by Unity's audio thread while this component is on an AudioListener.
    /// This ensures we capture every audio frame, even when individual sources aren't playing.
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isRecording) return;

        int frames = data.Length / channels;

        // Ensure frame buffers are allocated
        if (frameBufferW == null || frameBufferW.Length < frames)
        {
            currentFrameSize = frames;
            frameBufferW = new float[frames];
            frameBufferX = new float[frames];
            frameBufferY = new float[frames];
            frameBufferZ = new float[frames];
        }

        // Clear frame buffers
        System.Array.Clear(frameBufferW, 0, frames);
        System.Array.Clear(frameBufferX, 0, frames);
        System.Array.Clear(frameBufferY, 0, frames);
        System.Array.Clear(frameBufferZ, 0, frames);

        // Collect contributions from all encoders (use snapshot to avoid threading issues)
        var snapshot = encodersSnapshot; // Read the reference atomically
        foreach (var encoder in snapshot)
        {
            if (encoder != null)
            {
                encoder.GetEncodedOutput(frameBufferW, frameBufferX, frameBufferY, frameBufferZ, frames);
            }
        }

        // Write the mixed frame to our recording buffer
        lock (lockObject)
        {
            int startIndex = recordedSamplesW.Count;
            for (int i = 0; i < frames; i++)
            {
                recordedSamplesW.Add(frameBufferW[i]);
                recordedSamplesX.Add(frameBufferX[i]);
                recordedSamplesY.Add(frameBufferY[i]);
                recordedSamplesZ.Add(frameBufferZ[i]);
            }
        }

        // Don't modify the data - we're just recording, not filtering
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

        // Create a thread-safe snapshot for the audio thread
        encodersSnapshot = encoders.ToArray();
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

            // Find the peak amplitude across all channels for normalization
            int totalSamples = recordedSamplesW.Count;
            Debug.Log($"[AmbisonicRecorder] Total samples to write: {totalSamples}");

            float maxAmplitude = 0f;
            for (int i = 0; i < totalSamples; i++)
            {
                maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(recordedSamplesW[i]));
                maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(recordedSamplesX[i]));
                maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(recordedSamplesY[i]));
                maxAmplitude = Mathf.Max(maxAmplitude, Mathf.Abs(recordedSamplesZ[i]));
            }

            // Calculate normalization factor (leave some headroom to prevent clipping)
            float normalizationFactor = 1f;
            if (maxAmplitude > 0.95f) // Only normalize if we're close to clipping
            {
                normalizationFactor = 0.95f / maxAmplitude; // 0.95 gives us 5% headroom
                Debug.Log($"[AmbisonicRecorder] Normalizing audio: peak {maxAmplitude:F3} -> factor {normalizationFactor:F3}");
            }
            else
            {
                Debug.Log($"[AmbisonicRecorder] No normalization needed, peak amplitude: {maxAmplitude:F3}");
            }

            // Interleave and normalize the 4 channels (W, X, Y, Z)
            float[] interleavedData = new float[totalSamples * 4];

            for (int i = 0; i < totalSamples; i++)
            {
                interleavedData[i * 4 + 0] = recordedSamplesW[i] * normalizationFactor; // W
                interleavedData[i * 4 + 1] = recordedSamplesX[i] * normalizationFactor; // X
                interleavedData[i * 4 + 2] = recordedSamplesY[i] * normalizationFactor; // Y
                interleavedData[i * 4 + 3] = recordedSamplesZ[i] * normalizationFactor; // Z
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
