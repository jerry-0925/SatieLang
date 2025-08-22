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
        public int maxTokens = 4000;
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

    [System.Serializable]
    public class ConversationMessage
    {
        public string role; // "user" or "assistant"
        public string content;
        public string timestamp;
    }

    [System.Serializable]
    public class ConversationHistory
    {
        public ConversationMessage[] messages;
        public string currentScript;
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
        
        // Conversation context for follow-up editing
        private ConversationHistory currentConversation;
        private bool isEditMode = false;

        private const string SYSTEM_PROMPT = "Output ONLY valid Satie code. No markdown or explanations.\n\n" +
            "SYNTAX RULES:\n" +
            "- ALWAYS add colon: loop \"clip\": or oneshot \"clip\":\n" +
            "- Birds/footsteps/bicycles: oneshot with \"every\"\n" +
            "- Ambience/music: loop\n" +
            "- Move: walk,x,z,speed OR fly,x,y,z,speed OR pos,x,y,z\n" +
            "- Visual: sphere OR trail OR \"sphere and trail\" (NOT true/false)\n" +
            "- NO 'overlap' with multiplied oneshots\n\n" +
            "AVAILABLE AUDIO:\n" +
            "voice/1-40, conversation/people, bird/1-7, ambience/forest, music/beat, bicycle/1-37, footsteps/1-36\n\n" +
            "EXAMPLE:\n" +
            "loop \"ambience/forest\":\n" +
            "    volume = 0.3\n" +
            "5 * oneshot \"bird/1to4\" every 2to5:\n" +
            "    volume = 0.1to0.3\n" +
            "    move = fly, -10to10, 0to10, -10to10, 0.5\n" +
            "    visual = trail";

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
                    
                    // Check if response indicates token limit issue
                    bool isLengthLimit = responseText.Contains("\"finish_reason\":\"length\"") || responseText.Contains("\"finish_reason\": \"length\"");
                    if (isLengthLimit)
                    {
                        Debug.LogWarning($"[AI Request] Response hit token limit (attempt {attempt})");
                        
                        // On first attempt with length limit, retry immediately
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(500); // Short delay before retry
                            continue;
                        }
                    }
                    
                    var contentMatch = Regex.Match(responseText, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
                    if (contentMatch.Success)
                    {
                        string extractedContent = contentMatch.Groups[1].Value;
                        extractedContent = Regex.Unescape(extractedContent);
                        
                        // Only return if we actually have content, otherwise retry
                        if (extractedContent.Length > 0)
                        {
                            Debug.Log($"[AI Request] Success! Generated {extractedContent.Length} characters");
                            Debug.Log($"[AI Debug] Content preview: {extractedContent.Substring(0, Math.Min(200, extractedContent.Length))}...");
                            return extractedContent;
                        }
                        else
                        {
                            Debug.LogWarning($"[AI Request] Empty content received (attempt {attempt}), retrying...");
                        }
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
                            
                            // Only return if we actually have content
                            if (alternativeContent.Length > 0)
                            {
                                Debug.Log($"[AI Request] Alternative parsing successful! Generated {alternativeContent.Length} characters");
                                return alternativeContent;
                            }
                            else
                            {
                                Debug.LogWarning($"[AI Request] Alternative parsing found empty content (attempt {attempt}), retrying...");
                            }
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

        // Conversation context management
        public void StartNewConversation()
        {
            currentConversation = new ConversationHistory
            {
                messages = new ConversationMessage[0],
                currentScript = ""
            };
            isEditMode = false;
            Debug.Log("[AI Conversation] Started new conversation");
        }

        public void SetEditMode(bool enabled, string currentScript = "")
        {
            isEditMode = enabled;
            if (enabled && currentConversation == null)
            {
                StartNewConversation();
            }
            if (currentConversation != null)
            {
                currentConversation.currentScript = currentScript ?? "";
            }
            Debug.Log($"[AI Conversation] Edit mode: {enabled}");
        }

        public bool IsInEditMode()
        {
            return isEditMode && currentConversation != null;
        }

        public ConversationHistory GetCurrentConversation()
        {
            return currentConversation;
        }

        private void AddMessageToConversation(string role, string content)
        {
            if (currentConversation == null) return;

            var newMessage = new ConversationMessage
            {
                role = role,
                content = content,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var messagesList = new System.Collections.Generic.List<ConversationMessage>();
            if (currentConversation.messages != null)
            {
                messagesList.AddRange(currentConversation.messages);
            }
            messagesList.Add(newMessage);
            currentConversation.messages = messagesList.ToArray();
        }

        public async Task<string> GenerateWithFollowUp(string prompt, string currentScript = "")
        {
            if (isEditMode && currentConversation != null)
            {
                // Add user message to conversation
                AddMessageToConversation("user", prompt);
                
                // Update current script if provided
                if (!string.IsNullOrEmpty(currentScript))
                {
                    currentConversation.currentScript = currentScript;
                }

                // Generate with conversation context
                string result = await GenerateWithConversationContext(prompt, currentScript);
                
                // Add assistant response to conversation
                if (!result.StartsWith("# Error"))
                {
                    AddMessageToConversation("assistant", result);
                }
                
                return result;
            }
            else
            {
                // Regular generation, but store in conversation for potential follow-up
                StartNewConversation();
                AddMessageToConversation("user", prompt);
                
                string result = await GenerateWithResourceAwareness(prompt);
                
                if (!result.StartsWith("# Error"))
                {
                    AddMessageToConversation("assistant", result);
                    currentConversation.currentScript = result;
                }
                
                return result;
            }
        }

        private async Task<string> GenerateWithConversationContext(string prompt, string currentScript)
        {
            if (!await LoadApiKey())
            {
                return "# Error: API key not found. Please create Assets/api_key.txt with your OpenAI API key.";
            }

            try
            {
                // Get resource info
                string resourceInfo = await GetCachedResourceInfo();
                
                // Build conversation context
                string conversationContext = BuildConversationContext();
                
                // Create edit-specific system prompt
                string editSystemPrompt = CreateEditSystemPrompt(resourceInfo, currentScript);
                
                // Generate with conversation context
                return await GenerateSatieCodeWithConversation(prompt, editSystemPrompt, conversationContext);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Follow-up generation error: {e.Message}");
                return $"# Error: {e.Message}";
            }
        }

        private string BuildConversationContext()
        {
            if (currentConversation?.messages == null || currentConversation.messages.Length == 0)
                return "";

            var context = new System.Text.StringBuilder();
            context.AppendLine("PREVIOUS:");
            
            // Only include the last 4 messages to save tokens
            int startIndex = Mathf.Max(0, currentConversation.messages.Length - 4);
            for (int i = startIndex; i < currentConversation.messages.Length - 1; i++) // Exclude the current prompt
            {
                var msg = currentConversation.messages[i];
                // Truncate long messages
                string content = msg.content.Length > 150 ? msg.content.Substring(0, 150) + "..." : msg.content;
                context.AppendLine($"{(msg.role == "user" ? "USER" : "AI")}: {content}");
            }
            
            return context.ToString();
        }

        private string CreateEditSystemPrompt(string resourceInfo, string currentScript)
        {
            string baseSystemPrompt = SYSTEM_PROMPT.Replace(
                "voice/1-40, conversation/people, bird/1-7, ambience/forest, music/beat, bicycle/1-37, footsteps/1-36",
                resourceInfo
            );

            string editPrompt = baseSystemPrompt + "\n\n" +
                "EDIT MODE: Make ONLY requested changes. Output COMPLETE modified script.\n\n";

            if (!string.IsNullOrEmpty(currentScript))
            {
                editPrompt += $"CURRENT SCRIPT:\n{currentScript}\n\n";
            }

            return editPrompt;
        }

        private async Task<string> GenerateSatieCodeWithConversation(string prompt, string systemPrompt, string conversationContext)
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Debug.Log($"[AI Edit] Attempt {attempt}/{maxRetries} - Generating with conversation context...");
                    
                    string systemPromptEscaped = EscapeJsonString(systemPrompt);
                    string contextEscaped = EscapeJsonString(conversationContext);
                    string userPromptEscaped = EscapeJsonString($"{contextEscaped}\n\nUSER REQUEST: {prompt}");
                    
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
                    
                    if (!httpClient.DefaultRequestHeaders.Contains("Authorization"))
                    {
                        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    }
                    
                    Debug.Log($"[AI Edit] Sending edit request...");
                    var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                    var responseText = await response.Content.ReadAsStringAsync();
                    
                    Debug.Log($"[AI Edit] Response status: {response.StatusCode}");

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogError($"OpenAI API Error (edit attempt {attempt}): {response.StatusCode} - {responseText}");
                        
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return $"# Error: API request failed - {response.StatusCode}\n# Details: {responseText}";
                        }
                        
                        if (attempt == maxRetries)
                        {
                            return $"# Error: API request failed after {maxRetries} attempts - {response.StatusCode}\n# Details: {responseText}";
                        }
                        
                        await Task.Delay(1000 * attempt);
                        continue;
                    }

                    // Check if response indicates token limit issue
                    bool isLengthLimit = responseText.Contains("\"finish_reason\":\"length\"") || responseText.Contains("\"finish_reason\": \"length\"");
                    if (isLengthLimit)
                    {
                        Debug.LogWarning($"[AI Edit] Response hit token limit (attempt {attempt})");
                        
                        // On first attempt with length limit, retry immediately
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(500); // Short delay before retry
                            continue;
                        }
                    }

                    var contentMatch = Regex.Match(responseText, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
                    if (contentMatch.Success)
                    {
                        string extractedContent = contentMatch.Groups[1].Value;
                        extractedContent = Regex.Unescape(extractedContent);
                        
                        // Only return if we actually have content, otherwise retry
                        if (extractedContent.Length > 0)
                        {
                            Debug.Log($"[AI Edit] Success! Generated {extractedContent.Length} characters");
                            return extractedContent;
                        }
                        else
                        {
                            Debug.LogWarning($"[AI Edit] Empty content received (attempt {attempt}), retrying...");
                        }
                    }

                    Debug.LogWarning($"[AI Edit] No content found in response (attempt {attempt})");
                    if (attempt == maxRetries)
                    {
                        return "# Error: No response from AI";
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AI Edit] Error on attempt {attempt}/{maxRetries}: {e.Message}");
                    if (attempt == maxRetries)
                    {
                        return $"# Error: {e.Message}";
                    }
                    await Task.Delay(1000 * attempt);
                }
            }
            
            return "# Error: All retry attempts failed";
        }

    }
}