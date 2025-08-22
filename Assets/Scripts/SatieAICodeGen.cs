using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        public string model = "gpt-4-turbo-preview"; // Options: gpt-4-turbo-preview, gpt-4, gpt-3.5-turbo
        public float temperature = 1.0f;
        public int maxTokens = 3000;
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

        [SerializeField] private SatieAIConfig config = new SatieAIConfig();
        private string apiKey;
        private HttpClient httpClient;

        private const string SYSTEM_PROMPT = @"Output ONLY valid Satie code. Never add markdown code blocks or explanatory text.

CRITICAL SYNTAX RULES:
- ALWAYS add colon after statements: loop ""clip"": or oneshot ""clip"":
- NEVER use 'overlap' with multiplied oneshots (5 * oneshot)
- Move parameters for walk/fly: x, z, speed (NOT separate min/max for each axis)
- NO 'drive' move type - only walk, fly, or fixed
- Check available audio files before using them

SATIE SYNTAX:

Statements (MUST have colon):
loop ""audio/clip"":
oneshot ""audio/clip"":
oneshot ""audio/clip"" every 3to5:
5 * loop ""clip"":
5 * oneshot ""clip"" every 3to5:

Properties (indent under statements):
volume = 0.5 or volume = 0.1to0.8
pitch = 1.0 or pitch = 0.8to1.2
fade_in = 2
fade_out = 3
starts_at = 5
duration = 10
overlap (ONLY for single oneshot, NOT with 5 * oneshot)

Movement:
move = walk, x, z, speed
move = fly, x, z, speed
move = fixed, x, y, z

Visual:
visual = sphere
visual = trail
visual = sphere and trail

Groups:
group mygroup:
    volume = 0.5
    pitch = 1.2
    loop ""clip1"":
    oneshot ""clip2"":

AVAILABLE AUDIO ONLY:
voice/1 to voice/40
conversation/people, conversation/hello
bird/1 to bird/7, bird/1to4, bird/1to7
ambience/forest, ambience/lab
music/beat
bicycle/1 to bicycle/50
footsteps/1 to footsteps/36

NO car sounds available!

Example:
# Forest scene
loop ""ambience/forest"":
    volume = 0.3

5 * loop ""bird/1to4"":
    volume = 0.1to0.3
    pitch = 0.8to1.2
    move = fly, -10to10, -10to10, 0.5to2
    visual = sphere";

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            httpClient = new HttpClient();
            
            // Force update model if it's still using old o1-preview
            if (config.model == "o1-preview")
            {
                config.model = "gpt-4-turbo-preview";
                Debug.Log("Updated AI model from o1-preview to gpt-4-turbo-preview");
            }
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
                
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                
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
            Debug.Log($"[AI Debug] Temperature: {config.temperature}, Max Tokens: {config.maxTokens}");

            try
            {
                string systemPromptEscaped = EscapeJsonString(SYSTEM_PROMPT);
                string userPromptEscaped = EscapeJsonString(prompt);
                
                string jsonBody = $@"{{
                    ""model"": ""{config.model}"",
                    ""messages"": [
                        {{""role"": ""system"", ""content"": ""{systemPromptEscaped}""}},
                        {{""role"": ""user"", ""content"": ""{userPromptEscaped}""}}
                    ],
                    ""temperature"": {config.temperature},
                    ""max_tokens"": {config.maxTokens}
                }}";

                Debug.Log($"[AI Debug] Request URL: https://api.openai.com/v1/chat/completions");
                Debug.Log($"[AI Debug] Request body (first 500 chars): {jsonBody.Substring(0, Mathf.Min(500, jsonBody.Length))}...");

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                
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

    }
}