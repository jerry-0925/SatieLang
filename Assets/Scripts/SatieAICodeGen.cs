using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Satie
{
    [System.Serializable]
    public class SatieAIConfig
    {
        public string apiKeyPath = "api_key.txt";
        public string model = "gpt-5"; // Options: gpt-5, gpt-4-turbo-preview, gpt-4, gpt-3.5-turbo
        public float temperature = 1.0f;
        public int maxTokens = 3000;
        [Header("RLHF Settings")]
        public bool enableRLHF = true;
        public string rlhfDataPath = "rlhf_feedback.json";
    }

    [System.Serializable]
    public class RLHFFeedback
    {
        public string prompt;
        public string generatedCode;
        public bool wasCorrect;
        public string timestamp;
        public string feedbackNotes;
    }
    
    [System.Serializable]
    public class RLHFFeedbackWrapper
    {
        public RLHFFeedback[] items;
    }

    public class SatieAICodeGen : MonoBehaviour
    {
        private static SatieAICodeGen instance;
        public static SatieAICodeGen Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("SatieAICodeGen");
                    instance = go.AddComponent<SatieAICodeGen>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [SerializeField] public SatieAIConfig config = new SatieAIConfig();
        private string apiKey;
        private HttpClient httpClient;
        private string cachedResourceInfo;
        private float lastResourceScanTime = -1f;
        private const float RESOURCE_CACHE_DURATION = 300f; // 5 minutes

        private const string SYSTEM_PROMPT = "Output ONLY valid Satie code. Never add markdown code blocks or explanatory text.\n\n" +
            "CRITICAL SYNTAX RULES:\n" +
            "- ALWAYS add colon after statements: loop \"clip\": or oneshot \"clip\":\n" +
            "- NEVER use 'overlap' with multiplied oneshots (5 * oneshot)\n" +
            "- Move parameters for walk/fly: x, z, speed (NOT separate min/max for each axis)\n" +
            "- NO 'drive' move type - only walk, fly, or fixed\n" +
            "- Check available audio files before using them\n" +
            "- AUDIO TYPE RULES:\n" +
            "  - Birds: ALWAYS oneshot with \"every\" (birds chirp discretely)\n" +
            "  - Footsteps: ALWAYS oneshot with \"every\" (discrete steps)\n" +
            "  - Bicycles: ALWAYS oneshot with \"every\" (discrete sounds)\n" +
            "  - Ambience: ALWAYS loop (continuous background)\n" +
            "  - Music: ALWAYS loop (continuous playback)\n\n" +
            "SATIE SYNTAX:\n\n" +
            "Statements (MUST have colon):\n" +
            "loop \"audio/clip\":\n" +
            "oneshot \"audio/clip\":\n" +
            "oneshot \"audio/clip\" every 3to5:\n" +
            "5 * loop \"clip\":\n" +
            "5 * oneshot \"clip\" every 3to5:\n\n" +
            "Properties (indent under statements):\n" +
            "volume = 0.5 or volume = 0.1to0.8\n" +
            "pitch = 1.0 or pitch = 0.8to1.2\n" +
            "fade_in = 2\n" +
            "fade_out = 3\n" +
            "starts_at = 5\n" +
            "duration = 10\n" +
            "overlap (ONLY for single oneshot, NOT with 5 * oneshot)\n\n" +
            "Movement (CRITICAL - EXACT parameter counts required):\n" +
            "move = walk, x, z, speed (3 params: x and z coordinates, speed 0.1-2.0 is good)\n" +
            "move = fly, x, y, z, speed (4 params: x, y, z coordinates, speed 0.1-2.0 is good)\n" +
            "move = pos, x, y, z (3 params: fixed x, y, z coordinates)\n\n" +
            "Visual:\n" +
            "visual = sphere\n" +
            "visual = trail\n" +
            "visual = sphere and trail\n\n" +
            "Groups:\n" +
            "group mygroup:\n" +
            "    volume = 0.5\n" +
            "    pitch = 1.2\n" +
            "    loop \"clip1\":\n" +
            "    oneshot \"clip2\":\n\n" +
            "AVAILABLE AUDIO ONLY:\n" +
            "voice/1 to voice/40 (use oneshot)\n" +
            "conversation/people, conversation/hello (use oneshot or loop)\n" +
            "bird/1 to bird/7, bird/1to4, bird/1to7 (ALWAYS use oneshot with \"every\" timing)\n" +
            "ambience/forest, ambience/lab (use loop)\n" +
            "music/beat (use loop)\n" +
            "bicycle/1 to bicycle/37 (use oneshot with \"every\" timing)\n" +
            "footsteps/1 to footsteps/36 (use oneshot with \"every\" timing)\n\n" +
            "NO car sounds available!\n\n" +
            "Example:\n" +
            "# Forest scene\n" +
            "loop \"ambience/forest\":\n" +
            "    volume = 0.3\n\n" +
            "5 * oneshot \"bird/1to4\" every 2to5:\n" +
            "    volume = 0.1to0.3\n" +
            "    pitch = 0.8to1.2\n" +
            "    move = fly, -10to10, 0to10, -10to10, 0.5to2\n" +
            "    visual = sphere";

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Configure HttpClient for performance
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(120); // Increased for AI requests
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SatieLang/1.0");
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            
            // Force update model to GPT-5 if it's still using old models
            if (config.model != "gpt-5")
            {
                string oldModel = config.model;
                config.model = "gpt-5";
                Debug.Log($"Updated AI model from {oldModel} to gpt-5");
            }
            
            // Pre-cache resource info
            _ = RefreshResourceCache();
        }

        void OnDestroy()
        {
            httpClient?.Dispose();
        }

        public async Task<string> TestAPIConnection()
        {
            if (!await LoadApiKey())
            {
                return "API key not found";
            }

            try
            {
                Debug.Log("[AI Test] Testing API connection with models endpoint...");
                
                // Only update authorization header if needed
                if (!httpClient.DefaultRequestHeaders.Contains("Authorization"))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }
                
                // First test: List available models
                var modelsResponse = await httpClient.GetAsync("https://api.openai.com/v1/models");
                var modelsText = await modelsResponse.Content.ReadAsStringAsync();
                
                Debug.Log($"[AI Test] Models endpoint status: {modelsResponse.StatusCode}");
                
                if (!modelsResponse.IsSuccessStatusCode)
                {
                    Debug.LogError($"[AI Test] Models endpoint failed: {modelsText}");
                    return $"Models endpoint failed: {modelsResponse.StatusCode}";
                }
                
                // Log available models
                Debug.Log($"[AI Test] Available models response (first 1000 chars): {modelsText.Substring(0, Mathf.Min(1000, modelsText.Length))}");
                
                // Now test chat completions with a simple request
                string testJson = @"{
                    ""model"": ""gpt-3.5-turbo"",
                    ""messages"": [{""role"": ""user"", ""content"": ""Say 'test'""}],
                    ""max_tokens"": 10
                }";
                
                Debug.Log($"[AI Test] Testing chat endpoint with: {testJson}");
                
                var testContent = new StringContent(testJson, Encoding.UTF8, "application/json");
                var chatResponse = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", testContent);
                var chatText = await chatResponse.Content.ReadAsStringAsync();
                
                Debug.Log($"[AI Test] Chat endpoint status: {chatResponse.StatusCode}");
                Debug.Log($"[AI Test] Chat response: {chatText}");
                
                return $"Connection test complete. Models: {modelsResponse.StatusCode}, Chat: {chatResponse.StatusCode}";
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Test] Exception: {e}");
                return $"Test failed: {e.Message}";
            }
        }

        public async Task<string> GenerateSatieCode(string prompt)
        {
            if (!await LoadApiKey())
            {
                return "# Error: API key not found. Please create Assets/api_key.txt with your OpenAI API key.";
            }

            // Debug: Log API key info (safely)
            Debug.Log($"[AI Debug] API Key loaded: {apiKey.Substring(0, 7)}...{apiKey.Substring(apiKey.Length - 4)} (length: {apiKey.Length})");
            Debug.Log($"[AI Debug] Using model: {config.model}");

            try
            {
                // Standard chat completion format for GPT-5 and other models
                string systemPromptEscaped = EscapeJsonString(SYSTEM_PROMPT);
                string userPromptEscaped = EscapeJsonString(prompt);
                
                string jsonBody = $@"{{
                    ""model"": ""{config.model}"",
                    ""messages"": [
                        {{""role"": ""system"", ""content"": ""{systemPromptEscaped}""}},
                        {{""role"": ""user"", ""content"": ""{userPromptEscaped}""}}
                    ],
                    ""temperature"": {config.temperature},
                    ""max_completion_tokens"": {config.maxTokens}
                }}";
                
                Debug.Log($"[AI Debug] Using model: {config.model} (temperature: {config.temperature}, max_completion_tokens: {config.maxTokens})");

                Debug.Log($"[AI Debug] Request URL: https://api.openai.com/v1/chat/completions");
                Debug.Log($"[AI Debug] Request body (first 500 chars): {jsonBody.Substring(0, Mathf.Min(500, jsonBody.Length))}...");

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // Only update authorization header if needed
                if (!httpClient.DefaultRequestHeaders.Contains("Authorization"))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }
                
                Debug.Log($"[AI Debug] Sending request...");
                var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var responseText = await response.Content.ReadAsStringAsync();

                Debug.Log($"[AI Debug] Response Status: {response.StatusCode}");
                Debug.Log($"[AI Debug] Response Headers: {response.Headers}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[AI Debug] Full error response: {responseText}");
                    Debug.LogError($"OpenAI API Error: {response.StatusCode} - {responseText}");
                    return $"# Error: API request failed - {response.StatusCode}\n# Details: {responseText}";
                }

                // Extract content from response using regex
                var contentMatch = Regex.Match(responseText, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
                if (contentMatch.Success)
                {
                    string extractedContent = contentMatch.Groups[1].Value;
                    // Unescape the JSON string
                    extractedContent = Regex.Unescape(extractedContent);
                    return extractedContent;
                }

                return "# Error: No response from AI";
            }
            catch (Exception e)
            {
                Debug.LogError($"AI Generation Error: {e.Message}");
                return $"# Error: {e.Message}";
            }
        }

        public void RecordRLHFFeedback(string prompt, string generatedCode, bool wasCorrect, string notes = "")
        {
            if (!config.enableRLHF) return;

            var feedback = new RLHFFeedback
            {
                prompt = prompt,
                generatedCode = generatedCode,
                wasCorrect = wasCorrect,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                feedbackNotes = notes
            };

            try
            {
                string feedbackPath = Path.Combine(Application.dataPath, config.rlhfDataPath);
                List<RLHFFeedback> allFeedback = new List<RLHFFeedback>();

                if (File.Exists(feedbackPath))
                {
                    string existingData = File.ReadAllText(feedbackPath);
                    if (!string.IsNullOrEmpty(existingData))
                    {
                        try
                        {
                            // Simple JSON array parsing since Unity's JsonUtility doesn't support lists directly
                            string wrappedJson = "{\"items\":" + existingData + "}";
                            var feedbackWrapper = JsonUtility.FromJson<RLHFFeedbackWrapper>(wrappedJson);
                            if (feedbackWrapper?.items != null)
                            {
                                allFeedback = new List<RLHFFeedback>();
                                foreach (var item in feedbackWrapper.items)
                                    allFeedback.Add(item);
                            }
                        }
                        catch (System.Exception parseEx)
                        {
                            Debug.LogWarning($"Could not parse existing RLHF data: {parseEx.Message}");
                        }
                    }
                }

                allFeedback.Add(feedback);
                var feedbackArray = new RLHFFeedback[allFeedback.Count];
                for (int i = 0; i < allFeedback.Count; i++)
                    feedbackArray[i] = allFeedback[i];
                var wrapper = new RLHFFeedbackWrapper { items = feedbackArray };
                string jsonData = JsonUtility.ToJson(wrapper, true);
                // Extract just the items array from the wrapper
                var match = System.Text.RegularExpressions.Regex.Match(jsonData, @"""items""\s*:\s*(\[.*\])");
                if (match.Success)
                    jsonData = match.Groups[1].Value;
                else
                    jsonData = "[]";
                File.WriteAllText(feedbackPath, jsonData);
                
                Debug.Log($"[RLHF] Recorded feedback: {(wasCorrect ? "Correct" : "Incorrect")} for prompt: {prompt.Substring(0, Mathf.Min(50, prompt.Length))}...");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RLHF] Failed to record feedback: {e.Message}");
            }
        }

        public async Task<string> GenerateWithResourceAwareness(string prompt)
        {
            // Use cached resource info or scan if needed
            string resourceInfo = await GetCachedResourceInfo();
            
            // Enhanced system prompt with current resource info
            string enhancedSystemPrompt = SYSTEM_PROMPT.Replace(
                "AVAILABLE AUDIO ONLY:\nvoice/1 to voice/40\nconversation/people, conversation/hello\nbird/1 to bird/7, bird/1to4, bird/1to7\nambience/forest, ambience/lab\nmusic/beat\nbicycle/1 to bicycle/37\nfootsteps/1 to footsteps/36",
                $"AVAILABLE AUDIO (CURRENT SCAN):\n{resourceInfo}"
            );
            
            // Use enhanced prompt for generation
            return await GenerateSatieCodeWithCustomSystem(prompt, enhancedSystemPrompt);
        }

        private async Task<string> ScanAvailableResources()
        {
            return await Task.Run(() =>
            {
                var resourceInfo = new System.Text.StringBuilder();
                string audioPath = Path.Combine(Application.dataPath, "Resources", "Audio");
                
                if (!Directory.Exists(audioPath))
                {
                    return "No audio resources found";
                }

                var categories = Directory.GetDirectories(audioPath);
                foreach (var categoryPath in categories)
                {
                    string categoryName = Path.GetFileName(categoryPath);
                    var wavFiles = Directory.GetFiles(categoryPath, "*.wav");
                    var audioNumbers = new List<int>();
                    
                    foreach (var file in wavFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (int.TryParse(fileName, out int number))
                            audioNumbers.Add(number);
                    }
                    
                    audioNumbers.Sort();

                    if (audioNumbers.Count > 0)
                    {
                        int min = audioNumbers[0];
                        int max = audioNumbers[audioNumbers.Count - 1];
                        bool isSequential = true;
                        for (int i = 0; i < audioNumbers.Count - 1; i++)
                        {
                            if (audioNumbers[i + 1] != audioNumbers[i] + 1)
                            {
                                isSequential = false;
                                break;
                            }
                        }
                        
                        if (isSequential && audioNumbers.Count == max - min + 1)
                        {
                            resourceInfo.AppendLine($"{categoryName}/{min} to {categoryName}/{max}");
                        }
                        else
                        {
                            var numberStrings = new List<string>();
                            foreach (var num in audioNumbers)
                                numberStrings.Add(num.ToString());
                            resourceInfo.AppendLine($"{categoryName}: {string.Join(", ", numberStrings.ToArray())}");
                        }
                    }

                    // Also check for named files
                    var namedFiles = new List<string>();
                    foreach (var file in wavFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (!int.TryParse(fileName, out _))
                            namedFiles.Add(fileName);
                    }

                    if (namedFiles.Count > 0)
                    {
                        resourceInfo.AppendLine($"{categoryName}: {string.Join(", ", namedFiles.ToArray())}");
                    }
                }

                return resourceInfo.ToString().Trim();
            });
        }

        private async Task<string> GenerateSatieCodeWithCustomSystem(string prompt, string systemPrompt)
        {
            if (!await LoadApiKey())
            {
                return "# Error: API key not found. Please create Assets/api_key.txt with your OpenAI API key.";
            }

            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Debug.Log($"[AI Request] Attempt {attempt}/{maxRetries} - Starting generation...");
                    
                    string systemPromptEscaped = EscapeJsonString(systemPrompt);
                    string userPromptEscaped = EscapeJsonString(prompt);
                    
                    string jsonBody = $@"{{
                        ""model"": ""{config.model}"",
                        ""messages"": [
                            {{""role"": ""system"", ""content"": ""{systemPromptEscaped}""}},
                            {{""role"": ""user"", ""content"": ""{userPromptEscaped}""}}
                        ],
                        ""temperature"": {config.temperature},
                        ""max_completion_tokens"": {config.maxTokens}
                    }}";

                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    // Only update authorization header if needed
                    if (!httpClient.DefaultRequestHeaders.Contains("Authorization"))
                    {
                        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    }
                    
                    Debug.Log($"[AI Request] Sending request to OpenAI...");
                    var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                    var responseText = await response.Content.ReadAsStringAsync();
                    
                    Debug.Log($"[AI Request] Response status: {response.StatusCode}");

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogError($"OpenAI API Error (attempt {attempt}): {response.StatusCode} - {responseText}");
                        
                        // Don't retry on client errors (4xx), only on server errors or timeouts
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return $"# Error: API request failed - {response.StatusCode}\n# Details: {responseText}";
                        }
                        
                        if (attempt == maxRetries)
                        {
                            return $"# Error: API request failed after {maxRetries} attempts - {response.StatusCode}\n# Details: {responseText}";
                        }
                        
                        await Task.Delay(1000 * attempt); // Progressive backoff
                        continue;
                    }

                    // Log the first part of response to debug parsing issues
                    Debug.Log($"[AI Debug] Response preview: {responseText.Substring(0, Math.Min(500, responseText.Length))}...");
                    
                    var contentMatch = Regex.Match(responseText, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
                    if (contentMatch.Success)
                    {
                        string extractedContent = contentMatch.Groups[1].Value;
                        extractedContent = Regex.Unescape(extractedContent);
                        Debug.Log($"[AI Request] Success! Generated {extractedContent.Length} characters");
                        if (extractedContent.Length > 0)
                        {
                            Debug.Log($"[AI Debug] Content preview: {extractedContent.Substring(0, Math.Min(200, extractedContent.Length))}...");
                        }
                        return extractedContent;
                    }
                    else
                    {
                        // Try alternative parsing methods
                        Debug.LogWarning($"[AI Debug] Primary regex failed, trying alternatives...");
                        
                        // Try simpler content extraction
                        var simpleMatch = Regex.Match(responseText, @"""content"":\s*""([^""]+)""");
                        if (simpleMatch.Success)
                        {
                            string alternativeContent = simpleMatch.Groups[1].Value;
                            alternativeContent = Regex.Unescape(alternativeContent);
                            Debug.Log($"[AI Request] Alternative parsing successful! Generated {alternativeContent.Length} characters");
                            return alternativeContent;
                        }
                        
                        // Log full response if parsing completely fails
                        Debug.LogError($"[AI Debug] Content parsing failed completely. Full response: {responseText}");
                    }

                    Debug.LogWarning($"[AI Request] No content found in response (attempt {attempt})");
                    if (attempt == maxRetries)
                    {
                        return "# Error: No response from AI";
                    }
                }
                catch (TaskCanceledException e) when (e.InnerException is TimeoutException || e.CancellationToken.IsCancellationRequested)
                {
                    Debug.LogWarning($"[AI Request] Timeout on attempt {attempt}/{maxRetries}");
                    if (attempt == maxRetries)
                    {
                        return "# Error: Request timed out after multiple attempts. Try a shorter prompt or check your internet connection.";
                    }
                    await Task.Delay(2000 * attempt); // Longer delay for timeouts
                }
                catch (HttpRequestException e)
                {
                    Debug.LogWarning($"[AI Request] Network error on attempt {attempt}/{maxRetries}: {e.Message}");
                    if (attempt == maxRetries)
                    {
                        return $"# Error: Network error after {maxRetries} attempts: {e.Message}";
                    }
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AI Request] Unexpected error on attempt {attempt}/{maxRetries}: {e.Message}");
                    if (attempt == maxRetries)
                    {
                        return $"# Error: {e.Message}";
                    }
                    await Task.Delay(1000 * attempt);
                }
            }
            
            return "# Error: All retry attempts failed";
        }

        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
                
            str = str.Replace("\\", "\\\\");
            str = str.Replace("\"", "\\\"");
            str = str.Replace("\n", "\\n");
            str = str.Replace("\r", "\\r");
            str = str.Replace("\t", "\\t");
            str = str.Replace("\b", "\\b");
            str = str.Replace("\f", "\\f");
            
            return str;
        }

        private async Task<bool> LoadApiKey()
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                Debug.Log($"[AI Debug] Using cached API key");
                return true;
            }

            string keyPath = Path.Combine(Application.dataPath, config.apiKeyPath);
            Debug.Log($"[AI Debug] Looking for API key at: {keyPath}");
            
            if (!File.Exists(keyPath))
            {
                Debug.LogError($"[AI Debug] API key file not found at: {keyPath}");
                Debug.Log("Please create the file and add your OpenAI API key.");
                return false;
            }

            try
            {
                apiKey = await Task.Run(() => File.ReadAllText(keyPath).Trim());
                
                // Validate API key format
                if (string.IsNullOrEmpty(apiKey))
                {
                    Debug.LogError("[AI Debug] API key file is empty!");
                    return false;
                }
                
                if (!apiKey.StartsWith("sk-"))
                {
                    Debug.LogWarning($"[AI Debug] API key doesn't start with 'sk-'. Got: {apiKey.Substring(0, Mathf.Min(10, apiKey.Length))}...");
                }
                
                Debug.Log($"[AI Debug] API key loaded successfully. Format: {apiKey.Substring(0, 3)}... Length: {apiKey.Length}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Debug] Failed to read API key: {e.Message}");
                return false;
            }
        }
        
        private async Task<string> GetCachedResourceInfo()
        {
            float currentTime = Time.realtimeSinceStartup;
            
            // Check if we need to refresh cache
            if (string.IsNullOrEmpty(cachedResourceInfo) || 
                currentTime - lastResourceScanTime > RESOURCE_CACHE_DURATION)
            {
                await RefreshResourceCache();
            }
            
            return cachedResourceInfo ?? "No audio resources found";
        }
        
        private async Task RefreshResourceCache()
        {
            try
            {
                cachedResourceInfo = await ScanAvailableResources();
                lastResourceScanTime = Time.realtimeSinceStartup;
                Debug.Log($"[AI Cache] Resource cache refreshed with {cachedResourceInfo.Split('\n').Length} entries");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Cache] Failed to refresh resource cache: {e.Message}");
                // Keep using old cache if available
            }
        }
        
        public void InvalidateResourceCache()
        {
            cachedResourceInfo = null;
            lastResourceScanTime = -1f;
            Debug.Log("[AI Cache] Resource cache invalidated");
        }

    }
}