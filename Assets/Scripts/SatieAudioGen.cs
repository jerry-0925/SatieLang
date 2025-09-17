using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Satie
{
    public enum AudioProvider
    {
        AudioLDM2,
        ElevenLabs,
        Test
    }

    [System.Serializable]
    public class AudioGenerationResult
    {
        public string prompt;
        public string[] audioFilePaths;
        public byte[][] audioData;
        public int selectedIndex = -1;
        public string timestamp;
        public AudioProvider provider;
    }

    public class SatieAudioGen : MonoBehaviour
    {
        private static SatieAudioGen instance;
        public static SatieAudioGen Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("SatieAudioGen");
                    instance = go.AddComponent<SatieAudioGen>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        // API Configuration
        [Header("Server Configuration")]
        [SerializeField] private string apiUrl = "http://localhost:5001/generate"; // Multi-provider audio server
        [SerializeField] private int sampleRate = 44100;
        [SerializeField] private int numOptions = 2;
        [SerializeField] private AudioProvider defaultProvider = AudioProvider.AudioLDM2;

        [Header("Eleven Labs Settings")]
        [SerializeField] [Range(1f, 30f)] private float elevenLabsDuration = 10f;
        [SerializeField] [Range(0f, 1f)] private float elevenLabsPromptInfluence = 0.3f;

        [Header("AudioLDM2 Settings")]
        [SerializeField] [Range(50, 500)] private int audioldm2InferenceSteps = 200;
        [SerializeField] [Range(1f, 30f)] private float audioldm2Duration = 10f;

        // Cache for generated audio
        private Dictionary<string, AudioGenerationResult> generationCache = new Dictionary<string, AudioGenerationResult>();

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async Task<AudioGenerationResult> GenerateAudioOptions(string prompt, int numOptions = 2, AudioProvider? provider = null, System.Action<AudioGenerationResult, int> onOptionGenerated = null)
        {
            return await GenerateAudioOptionsInternal(prompt, numOptions, provider ?? defaultProvider, onOptionGenerated);
        }

        private async Task<AudioGenerationResult> GenerateAudioOptionsInternal(string prompt, int numOptions, AudioProvider provider, System.Action<AudioGenerationResult, int> onOptionGenerated)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Debug.LogError("Cannot generate audio with empty prompt");
                return null;
            }

            // Check cache first (cache key includes provider)
            string cacheKey = $"{provider}_{prompt}";
            if (generationCache.ContainsKey(cacheKey))
            {
                Debug.Log($"Returning cached audio for prompt: {prompt} (provider: {provider})");
                return generationCache[cacheKey];
            }

            var result = new AudioGenerationResult
            {
                prompt = prompt,
                audioFilePaths = new string[numOptions],
                audioData = new byte[numOptions][],
                timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                provider = provider
            };

            try
            {
                Debug.Log($"[AudioGen] Generating {numOptions} audio options for prompt: {prompt} using {provider}");

                for (int i = 0; i < numOptions; i++)
                {
                    // Generate audio using selected provider
                    byte[] audioData = await CallAudioGenerationAPI(prompt, i, provider);

                    if (audioData != null && audioData.Length > 0)
                    {
                        result.audioData[i] = audioData;
                        Debug.Log($"[AudioGen] Successfully generated option {i + 1}/{numOptions}");

                        // Notify callback that this option is ready
                        onOptionGenerated?.Invoke(result, i);
                    }
                    else
                    {
                        Debug.LogWarning($"[AudioGen] Failed to generate option {i + 1}/{numOptions}");
                    }
                }

                // Cache the result with provider-specific key
                generationCache[cacheKey] = result;
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioGen] Error generating audio: {e.Message}");
                return null;
            }
        }

        private async Task<byte[]> CallAudioGenerationAPI(string prompt, int seed, AudioProvider provider)
        {
            try
            {
                // Create JSON request object
                var requestData = new AudioGenerationRequest
                {
                    prompt = prompt,
                    seed = seed,
                    sample_rate = sampleRate,
                    num_inference_steps = audioldm2InferenceSteps,
                    audio_length_in_s = audioldm2Duration,
                    provider = provider.ToString().ToLower(),
                    // Eleven Labs specific parameters
                    duration_seconds = elevenLabsDuration,
                    prompt_influence = elevenLabsPromptInfluence
                };

                string jsonRequest = JsonUtility.ToJson(requestData);
                Debug.Log($"[AudioGen] Sending request to {apiUrl} with seed {seed} using provider {provider}");

                using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonRequest);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    // Send request
                    var operation = request.SendWebRequest();

                    // Wait for completion
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[AudioGen] API request failed: {request.error}");
                        Debug.LogError($"[AudioGen] Response code: {request.responseCode}");

                        // Check if server is running
                        if (request.error.Contains("Cannot connect"))
                        {
                            Debug.LogError("[AudioGen] Cannot connect to audio generation server. Please ensure the server is running:");
                            Debug.LogError("[AudioGen] 1. Install requirements: pip install -r requirements.txt");
                            Debug.LogError("[AudioGen] 2. Run server: python audio_generation_server.py");
                        }
                        return null;
                    }

                    Debug.Log($"[AudioGen] Successfully received audio data ({request.downloadHandler.data.Length} bytes)");
                    return request.downloadHandler.data;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioGen] API call error: {e.Message}");
                return null;
            }
        }

        [System.Serializable]
        private class AudioGenerationRequest
        {
            public string prompt;
            public int seed;
            public int sample_rate;
            public int num_inference_steps;
            public float audio_length_in_s;
            public string provider;
            // Eleven Labs specific
            public float duration_seconds;
            public float prompt_influence;
        }

        public async Task<string> SaveSelectedAudio(AudioGenerationResult result, int selectedIndex)
        {
            if (result == null || selectedIndex < 0 || selectedIndex >= result.audioData.Length)
            {
                Debug.LogError("Invalid audio selection");
                return null;
            }

            if (result.audioData[selectedIndex] == null || result.audioData[selectedIndex].Length == 0)
            {
                Debug.LogError("No audio data to save");
                return null;
            }

            try
            {
                // Create filename based on prompt, provider and timestamp
                string sanitizedPrompt = SanitizeFileName(result.prompt);
                string fileName = $"{sanitizedPrompt}_{result.provider}_{result.timestamp}_{selectedIndex}.wav";
                string relativePath = Path.Combine("Assets", "Resources", "Audio", "generation", fileName);
                string fullPath = Path.Combine(Application.dataPath, "Resources", "Audio", "generation", fileName);

                // Ensure directory exists
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write audio data to file
                await File.WriteAllBytesAsync(fullPath, result.audioData[selectedIndex]);

                Debug.Log($"[AudioGen] Saved audio to: {relativePath}");

                // Store the selected index
                result.selectedIndex = selectedIndex;
                result.audioFilePaths[selectedIndex] = relativePath;

                // Refresh Unity asset database
                #if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
                #endif

                return relativePath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioGen] Error saving audio: {e.Message}");
                return null;
            }
        }

        public AudioClip ConvertBytesToAudioClip(byte[] audioData, string name = "GeneratedAudio")
        {
            if (audioData == null || audioData.Length == 0)
            {
                Debug.LogError("Cannot convert null or empty audio data");
                return null;
            }

            try
            {
                // Parse WAV file header and extract PCM data
                WAVData wavData = ParseWAVData(audioData);
                if (wavData == null)
                {
                    Debug.LogError("Failed to parse WAV data");
                    return null;
                }

                // Create AudioClip
                AudioClip audioClip = AudioClip.Create(
                    name,
                    wavData.samples.Length / wavData.channels,
                    wavData.channels,
                    wavData.sampleRate,
                    false
                );

                audioClip.SetData(wavData.samples, 0);
                return audioClip;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioGen] Error converting audio: {e.Message}");
                return null;
            }
        }

        private class WAVData
        {
            public int channels;
            public int sampleRate;
            public float[] samples;
        }

        private WAVData ParseWAVData(byte[] wavFile)
        {
            try
            {
                // Basic WAV file parsing
                // Skip to format chunk
                int pos = 12; // Skip RIFF header

                while (pos < wavFile.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavFile, pos, 4);
                    int chunkSize = BitConverter.ToInt32(wavFile, pos + 4);

                    if (chunkId == "fmt ")
                    {
                        // Parse format chunk
                        var wavData = new WAVData();
                        wavData.channels = BitConverter.ToInt16(wavFile, pos + 10);
                        wavData.sampleRate = BitConverter.ToInt32(wavFile, pos + 12);
                        int bitDepth = BitConverter.ToInt16(wavFile, pos + 22);

                        // Find data chunk
                        pos += 8 + chunkSize;
                        while (pos < wavFile.Length - 8)
                        {
                            chunkId = System.Text.Encoding.ASCII.GetString(wavFile, pos, 4);
                            chunkSize = BitConverter.ToInt32(wavFile, pos + 4);

                            if (chunkId == "data")
                            {
                                // Parse audio samples
                                int sampleCount = chunkSize / (bitDepth / 8);
                                wavData.samples = new float[sampleCount];

                                int dataPos = pos + 8;
                                if (bitDepth == 16)
                                {
                                    for (int i = 0; i < sampleCount; i++)
                                    {
                                        short sample = BitConverter.ToInt16(wavFile, dataPos + i * 2);
                                        wavData.samples[i] = sample / 32768f;
                                    }
                                }
                                else if (bitDepth == 24)
                                {
                                    for (int i = 0; i < sampleCount; i++)
                                    {
                                        int sample = (wavFile[dataPos + i * 3] |
                                                     (wavFile[dataPos + i * 3 + 1] << 8) |
                                                     (wavFile[dataPos + i * 3 + 2] << 16));
                                        if ((sample & 0x800000) != 0)
                                            sample |= unchecked((int)0xFF000000);
                                        wavData.samples[i] = sample / 8388608f;
                                    }
                                }

                                return wavData;
                            }

                            pos += 8 + chunkSize;
                        }

                        break;
                    }

                    pos += 8 + chunkSize;
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing WAV: {e.Message}");
                return null;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove invalid characters for file names
            string invalid = new string(Path.GetInvalidFileNameChars());
            string sanitized = fileName;

            foreach (char c in invalid)
            {
                sanitized = sanitized.Replace(c.ToString(), "_");
            }

            // Limit length
            if (sanitized.Length > 50)
            {
                sanitized = sanitized.Substring(0, 50);
            }

            return sanitized.Replace(" ", "_").ToLower();
        }

        public void ClearCache()
        {
            generationCache.Clear();
            Debug.Log("[AudioGen] Cache cleared");
        }

        public List<string> GetGeneratedAudioFiles()
        {
            string generationPath = Path.Combine(Application.dataPath, "Resources", "Audio", "generation");

            if (!Directory.Exists(generationPath))
            {
                return new List<string>();
            }

            var files = Directory.GetFiles(generationPath, "*.wav")
                .Select(f => Path.GetFileName(f))
                .OrderByDescending(f => File.GetCreationTime(Path.Combine(generationPath, f)))
                .ToList();

            return files;
        }

        public void SetDefaultProvider(AudioProvider provider)
        {
            defaultProvider = provider;
            Debug.Log($"[AudioGen] Default provider set to: {provider}");
        }

        public AudioProvider GetDefaultProvider()
        {
            return defaultProvider;
        }

        public async Task<bool> CheckServerHealth()
        {
            try
            {
                string healthUrl = apiUrl.Replace("/generate", "/health");
                using (UnityWebRequest request = UnityWebRequest.Get(healthUrl))
                {
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[AudioGen] Server health check: {request.downloadHandler.text}");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[AudioGen] Server health check failed: {request.error}");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioGen] Health check error: {e.Message}");
                return false;
            }
        }

        public void SetApiUrl(string url)
        {
            apiUrl = url;
            Debug.Log($"[AudioGen] API URL set to: {url}");
        }

        public string GetApiUrl()
        {
            return apiUrl;
        }
    }
}