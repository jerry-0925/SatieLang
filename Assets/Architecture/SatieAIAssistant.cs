using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Satie
{
    /// <summary>
    /// Modern implementation using OpenAI Assistants API v2 with File Search
    /// Replaces the old TF-IDF based knowledge indexing system
    /// </summary>
    public class SatieAIAssistant : MonoBehaviour
    {
        private static SatieAIAssistant instance;
        public static SatieAIAssistant Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("SatieAIAssistant");
                    instance = go.AddComponent<SatieAIAssistant>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("Assistant Configuration")]
        [SerializeField] private string assistantId;
        [SerializeField] private string vectorStoreId;
        [SerializeField] private string assistantName = "Satie Code Generator";
        [SerializeField] private string model = "gpt-4-turbo-preview";

        private HttpClient httpClient;
        private string apiKey;
        private string currentThreadId;
        private Dictionary<string, string> fileIdMap = new Dictionary<string, string>();
        private Dictionary<string, long> fileHashCache = new Dictionary<string, long>();
        private string cacheFilePath;

        private const string BASE_URL = "https://api.openai.com/v1";

        #region Data Models
        [System.Serializable]
        public class AssistantRequest
        {
            public string model;
            public string name;
            public string instructions;
            public Tool[] tools;
            public ToolResources tool_resources;
        }

        [System.Serializable]
        public class Tool
        {
            public string type;
        }

        [System.Serializable]
        public class ToolResources
        {
            public FileSearchResource file_search;
        }

        [System.Serializable]
        public class FileSearchResource
        {
            public string[] vector_store_ids;
        }

        [System.Serializable]
        public class AssistantResponse
        {
            public string id;
            public string @object;
            public long created_at;
            public string name;
            public string model;
            public string instructions;
            public Tool[] tools;
            public ToolResources tool_resources;
        }

        [System.Serializable]
        public class VectorStoreRequest
        {
            public string name;
            public FileChunkingStrategy chunking_strategy;
        }

        [System.Serializable]
        public class FileChunkingStrategy
        {
            public string type = "auto";
        }

        [System.Serializable]
        public class VectorStoreResponse
        {
            public string id;
            public string @object;
            public long created_at;
            public string name;
            public int usage_bytes;
            public FileCounts file_counts;
            public string status;
        }

        [System.Serializable]
        public class FileCounts
        {
            public int in_progress;
            public int completed;
            public int failed;
            public int cancelled;
            public int total;
        }

        [System.Serializable]
        public class ThreadRequest
        {
            public Message[] messages;
            public ToolResources tool_resources;
        }

        [System.Serializable]
        public class Message
        {
            public string role;
            public string content;
        }

        [System.Serializable]
        public class ThreadResponse
        {
            public string id;
            public string @object;
            public long created_at;
        }

        [System.Serializable]
        public class RunRequest
        {
            public string assistant_id;
            public string instructions;
            public Tool[] tools;
            public float temperature;
            public int max_completion_tokens;
        }

        [System.Serializable]
        public class RunResponse
        {
            public string id;
            public string @object;
            public long created_at;
            public string thread_id;
            public string assistant_id;
            public string status;
            public Usage usage;
        }

        [System.Serializable]
        public class Usage
        {
            public int prompt_tokens;
            public int completion_tokens;
            public int total_tokens;
        }

        [System.Serializable]
        public class MessageListResponse
        {
            public MessageResponse[] data;
        }

        [System.Serializable]
        public class MessageResponse
        {
            public string id;
            public string @object;
            public long created_at;
            public string thread_id;
            public string role;
            public ContentItem[] content;
        }

        [System.Serializable]
        public class ContentItem
        {
            public string type;
            public TextContent text;
        }

        [System.Serializable]
        public class TextContent
        {
            public string value;
        }

        [System.Serializable]
        public class FileUploadResponse
        {
            public string id;
            public string @object;
            public int bytes;
            public long created_at;
            public string filename;
            public string purpose;
        }

        [System.Serializable]
        public class FileAttachRequest
        {
            public string file_id;
        }

        [System.Serializable]
        public class AssistantUpdateRequest
        {
            public ToolResources tool_resources;
        }

        [System.Serializable]
        public class FileCacheData
        {
            public List<FileCacheEntry> entries = new List<FileCacheEntry>();
        }

        [System.Serializable]
        public class FileCacheEntry
        {
            public string filePath;
            public long lastModified;
            public string fileId;
        }
        #endregion

        private async void Start()
        {
            // Initialize cache file path
            cacheFilePath = Path.Combine(Application.persistentDataPath, "satie_file_cache.json");
            LoadFileCache();

            await Initialize();
        }

        public async Task<bool> Initialize()
        {
            try
            {
                // Load API key
                if (!await LoadApiKey())
                {
                    Debug.LogError("[AI Assistant] Failed to load API key");
                    return false;
                }

                // Initialize HTTP client
                httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
                httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

                // Create or retrieve assistant
                if (string.IsNullOrEmpty(assistantId))
                {
                    await CreateAssistant();
                }
                else
                {
                    await RetrieveAssistant();
                }

                // Create or retrieve vector store
                if (string.IsNullOrEmpty(vectorStoreId))
                {
                    await CreateVectorStore();
                }

                // Upload project files to vector store
                await UploadProjectFiles();

                Debug.Log($"[AI Assistant] Initialized successfully. Assistant: {assistantId}, Vector Store: {vectorStoreId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Initialization failed: {e.Message}");
                return false;
            }
        }

        private async Task<bool> LoadApiKey()
        {
            try
            {
                apiKey = SatieAPIKeyManager.GetKey(SatieAPIKeyManager.Provider.OpenAI);

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Debug.LogError("[AI Assistant] No OpenAI API key configured. Please use Satie > API Key Manager to set it up.");
                    return false;
                }

                Debug.Log($"[AI Assistant] API key loaded successfully from centralized manager");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to load API key: {e.Message}");
                return false;
            }
        }

        private async Task CreateAssistant()
        {
            try
            {
                string audioLibrary = GetDynamicAudioLibrary();
                var instructions = $@"Output ONLY valid Satie code. No explanations, no markdown, no text before or after the code.

STRICT RULES:
- Your response must be pure Satie code only
- NO explanations or descriptions
- NO markdown code blocks
- NO ""Here's your code"" or similar text
- Start directly with the Satie code
- End directly with the Satie code

CRITICAL SYNTAX RULES (NO EXCEPTIONS):
- loop ""audio/file"": or oneshot ""audio/file"": (ALWAYS add colon)
- Move commands: ONLY walk, fly, or pos (NO spaces after commas):
  * move = walk,x,z,speed (4 params - example: move = walk,-10to10,5to15,1to2)
  * move = fly,x,y,z,speed (5 params - example: move = fly,-15to15,0to10,-10to10,1to3)
  * move = pos,x,y,z (4 params - example: move = pos,0,5,10)
- Visual commands: ONLY sphere, trail, cube, or combinations:
  * visual = sphere
  * visual = trail
  * visual = cube
  * visual = ""sphere and trail""
  * visual = object ""1to3""
- NO spaces after commas in move commands
- NO invalid move types like ""random"" or ""circle""
- NO invalid visual types like ""flock"" or ""sparkle""
- FLY MUST HAVE 5 PARAMETERS: x,y,z,speed
- WALK MUST HAVE 4 PARAMETERS: x,z,speed
- Ranges: 1to5, -10to10, 0.1to0.5

{audioLibrary}

EXAMPLE OF CORRECT SYNTAX:
group birds:
    2 * oneshot ""bird/1to4"" every 10to15:
        volume = 0.01to0.05
        move = fly,-20to10,0to10,-5to6,1to2
        visual = sphere

CORRECT MOVE EXAMPLES:
- move = walk,-10to10,5to15,1to2 (walk: x,z,speed - 4 params)
- move = fly,-15to15,0to10,-10to10,1to3 (fly: x,y,z,speed - 5 params)
- move = pos,0,5,10 (pos: x,y,z - 4 params)

IMPORTANT: ONLY use audio files from the above list. Do NOT make up file paths.

Generate valid Satie code following these exact syntax rules.";

                var request = new AssistantRequest
                {
                    model = model,
                    name = assistantName,
                    instructions = instructions,
                    tools = new[] { new Tool { type = "file_search" } },
                    tool_resources = new ToolResources
                    {
                        file_search = new FileSearchResource
                        {
                            vector_store_ids = string.IsNullOrEmpty(vectorStoreId)
                                ? new string[0]
                                : new[] { vectorStoreId }
                        }
                    }
                };

                string json = JsonUtility.ToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{BASE_URL}/assistants", content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[AI Assistant] Failed to create assistant: {responseJson}");
                    return;
                }

                var assistantResponse = JsonUtility.FromJson<AssistantResponse>(responseJson);
                assistantId = assistantResponse.id;

                Debug.Log($"[AI Assistant] Created assistant: {assistantId}");
                SaveConfiguration();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to create assistant: {e.Message}");
            }
        }

        private async Task RetrieveAssistant()
        {
            try
            {
                var response = await httpClient.GetAsync($"{BASE_URL}/assistants/{assistantId}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning($"[AI Assistant] Assistant {assistantId} not found, creating new one");
                    assistantId = null;
                    await CreateAssistant();
                }
                else
                {
                    Debug.Log($"[AI Assistant] Retrieved existing assistant: {assistantId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to retrieve assistant: {e.Message}");
            }
        }

        private async Task CreateVectorStore()
        {
            try
            {
                var request = new VectorStoreRequest
                {
                    name = "Satie Project Knowledge",
                    chunking_strategy = new FileChunkingStrategy { type = "auto" }
                };

                string json = JsonUtility.ToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{BASE_URL}/vector_stores", content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[AI Assistant] Failed to create vector store: {responseJson}");
                    return;
                }

                var vectorStoreResponse = JsonUtility.FromJson<VectorStoreResponse>(responseJson);
                vectorStoreId = vectorStoreResponse.id;

                Debug.Log($"[AI Assistant] Created vector store: {vectorStoreId}");
                SaveConfiguration();

                // Attach vector store to assistant
                await AttachVectorStoreToAssistant();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to create vector store: {e.Message}");
            }
        }

        private async Task AttachVectorStoreToAssistant()
        {
            try
            {
                var request = new AssistantUpdateRequest
                {
                    tool_resources = new ToolResources
                    {
                        file_search = new FileSearchResource
                        {
                            vector_store_ids = new[] { vectorStoreId }
                        }
                    }
                };

                string json = JsonUtility.ToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{BASE_URL}/assistants/{assistantId}", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"[AI Assistant] Failed to attach vector store: {error}");
                }
                else
                {
                    Debug.Log($"[AI Assistant] Attached vector store to assistant");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to attach vector store: {e.Message}");
            }
        }

        public async Task UploadProjectFiles()
        {
            try
            {
                // Find supported file types for context (skip .sat and .cs since they're not supported)
                string[] patterns = new[] { "*.md", "*.txt", "*.json" };
                List<string> filesToUpload = new List<string>();

                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(Application.dataPath, pattern, SearchOption.AllDirectories)
                        .Where(f => !f.Contains("/Editor/") && !f.Contains("/Plugins/"))
                        .Take(50); // Limit to avoid overwhelming the API

                    filesToUpload.AddRange(files);
                }

                Debug.Log($"[AI Assistant] Found {filesToUpload.Count} supported files (.md, .txt, .json)");

                // Check which files need uploading
                var filesToActuallyUpload = new List<string>();
                var skippedFiles = 0;

                foreach (var filePath in filesToUpload)
                {
                    if (!HasFileChanged(filePath) && fileIdMap.ContainsKey(filePath))
                    {
                        skippedFiles++;
                        continue; // File hasn't changed and we have its file ID
                    }

                    filesToActuallyUpload.Add(filePath);
                }

                Debug.Log($"[AI Assistant] Uploading {filesToActuallyUpload.Count} changed files, skipping {skippedFiles} unchanged files");
                Debug.Log("[AI Assistant] Note: .sat and .cs files are passed directly in conversation context");

                foreach (var filePath in filesToActuallyUpload)
                {
                    await UploadFileToVectorStore(filePath);

                    // Small delay to avoid rate limiting
                    await Task.Delay(100);
                }

                // Save cache after uploads
                if (filesToActuallyUpload.Count > 0)
                {
                    SaveFileCache();

                    // Wait for vector store processing only if we uploaded new files
                    await WaitForVectorStoreProcessing();
                }
                else
                {
                    Debug.Log("[AI Assistant] No files needed uploading - vector store is up to date");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to upload files: {e.Message}");
            }
        }

        private async Task UploadFileToVectorStore(string filePath)
        {
            try
            {
                var originalFileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(filePath).ToLower();

                // Map unsupported extensions to supported ones
                string uploadFileName = originalFileName;
                if (fileExtension == ".sat" || fileExtension == ".cs")
                {
                    // Change extension to .txt for upload (OpenAI doesn't support .sat or .cs)
                    uploadFileName = Path.ChangeExtension(originalFileName, ".txt");
                }

                var fileBytes = await File.ReadAllBytesAsync(filePath);

                using var formData = new MultipartFormDataContent();
                formData.Add(new ByteArrayContent(fileBytes), "file", uploadFileName);
                formData.Add(new StringContent("assistants"), "purpose");

                var response = await httpClient.PostAsync($"{BASE_URL}/files", formData);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.LogWarning($"[AI Assistant] Failed to upload {originalFileName}: {error}");
                    return;
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var fileResponse = JsonUtility.FromJson<FileUploadResponse>(responseJson);
                string fileId = fileResponse.id;

                // Add file to vector store
                var attachRequestData = new FileAttachRequest { file_id = fileId };
                var attachRequest = new StringContent(
                    JsonUtility.ToJson(attachRequestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var attachResponse = await httpClient.PostAsync(
                    $"{BASE_URL}/vector_stores/{vectorStoreId}/files",
                    attachRequest
                );

                if (attachResponse.IsSuccessStatusCode)
                {
                    UpdateFileCache(filePath, fileId);
                    Debug.Log($"[AI Assistant] Uploaded {originalFileName} (as {uploadFileName}) to vector store");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Failed to upload file {filePath}: {e.Message}");
            }
        }

        private async Task WaitForVectorStoreProcessing()
        {
            try
            {
                int maxAttempts = 30;
                int attempt = 0;

                while (attempt < maxAttempts)
                {
                    var response = await httpClient.GetAsync($"{BASE_URL}/vector_stores/{vectorStoreId}");

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var vectorStore = JsonUtility.FromJson<VectorStoreResponse>(json);

                        if (vectorStore.status == "completed" &&
                            vectorStore.file_counts.in_progress == 0)
                        {
                            Debug.Log($"[AI Assistant] Vector store ready: {vectorStore.file_counts.completed} files processed");
                            break;
                        }

                        Debug.Log($"[AI Assistant] Processing files: {vectorStore.file_counts.in_progress} in progress, {vectorStore.file_counts.completed} completed");
                    }

                    await Task.Delay(2000);
                    attempt++;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Error checking vector store status: {e.Message}");
            }
        }

        public async Task<string> GenerateCode(string prompt, string currentScript = null)
        {
            try
            {
                // Create enhanced prompt with current script context
                string enhancedPrompt = CreateEnhancedPrompt(prompt, currentScript);

                // Create a new thread for this conversation
                currentThreadId = await CreateThread(enhancedPrompt, null);

                if (string.IsNullOrEmpty(currentThreadId))
                {
                    Debug.LogError("[AI Assistant] Failed to create thread");
                    return null;
                }

                // Run the assistant
                string runId = await CreateRun(currentThreadId, null);

                if (string.IsNullOrEmpty(runId))
                {
                    Debug.LogError("[AI Assistant] Failed to create run");
                    return null;
                }

                // Wait for completion
                await WaitForRunCompletion(currentThreadId, runId);

                // Get the response
                string response = await GetAssistantResponse(currentThreadId);

                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to generate code: {e.Message}");
                return null;
            }
        }

        private string CreateEnhancedPrompt(string userPrompt, string currentScript)
        {
            var promptBuilder = new StringBuilder();

            // Include critical syntax rules
            promptBuilder.AppendLine("CRITICAL SYNTAX RULES:");
            promptBuilder.AppendLine("- Move commands: NO spaces after commas!");
            promptBuilder.AppendLine("  * move = walk,x,z,speed (4 params - NOT walk, x, z, speed)");
            promptBuilder.AppendLine("  * move = fly,x,y,z,speed (5 params - NOT fly, x, y, z, speed)");
            promptBuilder.AppendLine("  * FLY MUST HAVE 5 PARAMETERS: x,y,z,speed");
            promptBuilder.AppendLine("  * WALK MUST HAVE 4 PARAMETERS: x,z,speed");
            promptBuilder.AppendLine("- ONLY use: walk, fly, pos for move commands");
            promptBuilder.AppendLine("- ONLY use: sphere, trail, cube for visual commands");
            promptBuilder.AppendLine();

            // Include dynamic audio library
            string audioLibrary = GetDynamicAudioLibrary();
            promptBuilder.AppendLine(audioLibrary);
            promptBuilder.AppendLine();

            if (!string.IsNullOrEmpty(currentScript))
            {
                promptBuilder.AppendLine("CURRENT SCRIPT:");
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine(currentScript);
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("USER REQUEST:");
                promptBuilder.AppendLine(userPrompt);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Modify the current script according to the user request. Output only the complete modified script with correct syntax (NO SPACES after commas in move commands).");
            }
            else
            {
                promptBuilder.AppendLine("USER REQUEST:");
                promptBuilder.AppendLine(userPrompt);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Generate Satie code for this request using correct syntax (NO SPACES after commas in move commands).");
            }

            return promptBuilder.ToString();
        }

        private async Task<string> CreateThread(string userPrompt, string currentScript)
        {
            try
            {
                var messages = new List<Message>();

                if (!string.IsNullOrEmpty(currentScript))
                {
                    messages.Add(new Message
                    {
                        role = "user",
                        content = $"Current script:\n```satie\n{currentScript}\n```\n\n{userPrompt}"
                    });
                }
                else
                {
                    messages.Add(new Message { role = "user", content = userPrompt });
                }

                var request = new ThreadRequest
                {
                    messages = messages.ToArray(),
                    tool_resources = new ToolResources
                    {
                        file_search = new FileSearchResource
                        {
                            vector_store_ids = new[] { vectorStoreId }
                        }
                    }
                };

                string json = JsonUtility.ToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{BASE_URL}/threads", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"[AI Assistant] Failed to create thread: {error}");
                    return null;
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var threadResponse = JsonUtility.FromJson<ThreadResponse>(responseJson);

                Debug.Log($"[AI Assistant] Created thread: {threadResponse.id}");
                return threadResponse.id;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to create thread: {e.Message}");
                return null;
            }
        }

        private async Task<string> CreateRun(string threadId, string additionalInstructions = null)
        {
            try
            {
                var request = new RunRequest
                {
                    assistant_id = assistantId,
                    instructions = additionalInstructions,
                    tools = new[] { new Tool { type = "file_search" } },
                    temperature = 0.7f,
                    max_completion_tokens = 4000
                };

                string json = JsonUtility.ToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{BASE_URL}/threads/{threadId}/runs", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"[AI Assistant] Failed to create run: {error}");
                    return null;
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var runResponse = JsonUtility.FromJson<RunResponse>(responseJson);

                Debug.Log($"[AI Assistant] Created run: {runResponse.id}");
                return runResponse.id;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to create run: {e.Message}");
                return null;
            }
        }

        private async Task WaitForRunCompletion(string threadId, string runId)
        {
            try
            {
                int maxAttempts = 60;
                int attempt = 0;

                while (attempt < maxAttempts)
                {
                    var response = await httpClient.GetAsync($"{BASE_URL}/threads/{threadId}/runs/{runId}");

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var runResponse = JsonUtility.FromJson<RunResponse>(json);

                        Debug.Log($"[AI Assistant] Run status: {runResponse.status}");

                        if (runResponse.status == "completed")
                        {
                            Debug.Log($"[AI Assistant] Run completed. Tokens used: {runResponse.usage?.total_tokens ?? 0}");
                            return;
                        }
                        else if (runResponse.status == "failed" || runResponse.status == "cancelled" || runResponse.status == "expired")
                        {
                            Debug.LogError($"[AI Assistant] Run failed with status: {runResponse.status}");
                            return;
                        }
                    }

                    await Task.Delay(1000);
                    attempt++;
                }

                Debug.LogError("[AI Assistant] Run timed out");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Error waiting for run completion: {e.Message}");
            }
        }

        private async Task<string> GetAssistantResponse(string threadId)
        {
            try
            {
                var response = await httpClient.GetAsync($"{BASE_URL}/threads/{threadId}/messages");

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"[AI Assistant] Failed to get messages: {error}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var messages = JsonUtility.FromJson<MessageListResponse>(json);

                // Find the latest assistant message
                var assistantMessage = messages.data
                    .Where(m => m.role == "assistant")
                    .OrderByDescending(m => m.created_at)
                    .FirstOrDefault();

                if (assistantMessage?.content?.Length > 0 &&
                    assistantMessage.content[0].type == "text")
                {
                    return assistantMessage.content[0].text.value;
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to get response: {e.Message}");
                return null;
            }
        }

        private void SaveConfiguration()
        {
            // Save assistant and vector store IDs to PlayerPrefs or a config file
            PlayerPrefs.SetString("SatieAI_AssistantId", assistantId);
            PlayerPrefs.SetString("SatieAI_VectorStoreId", vectorStoreId);
            PlayerPrefs.Save();
        }

        private void LoadConfiguration()
        {
            assistantId = PlayerPrefs.GetString("SatieAI_AssistantId", "");
            vectorStoreId = PlayerPrefs.GetString("SatieAI_VectorStoreId", "");
        }

        public string GetAssistantId() => assistantId;
        public string GetVectorStoreId() => vectorStoreId;

        private void LoadFileCache()
        {
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    string json = File.ReadAllText(cacheFilePath);
                    var cacheData = JsonUtility.FromJson<FileCacheData>(json);

                    fileHashCache.Clear();
                    fileIdMap.Clear();

                    foreach (var entry in cacheData.entries)
                    {
                        fileHashCache[entry.filePath] = entry.lastModified;
                        fileIdMap[entry.filePath] = entry.fileId;
                    }

                    Debug.Log($"[AI Assistant] Loaded file cache with {cacheData.entries.Count} entries");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Failed to load file cache: {e.Message}");
                fileHashCache.Clear();
                fileIdMap.Clear();
            }
        }

        private void SaveFileCache()
        {
            try
            {
                if (string.IsNullOrEmpty(cacheFilePath))
                {
                    Debug.LogWarning("[AI Assistant] Cache file path not initialized, skipping save");
                    return;
                }

                var cacheData = new FileCacheData();

                foreach (var kvp in fileHashCache)
                {
                    cacheData.entries.Add(new FileCacheEntry
                    {
                        filePath = kvp.Key,
                        lastModified = kvp.Value,
                        fileId = fileIdMap.ContainsKey(kvp.Key) ? fileIdMap[kvp.Key] : ""
                    });
                }

                string json = JsonUtility.ToJson(cacheData, true);
                File.WriteAllText(cacheFilePath, json);

                Debug.Log($"[AI Assistant] Saved file cache with {cacheData.entries.Count} entries");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Failed to save file cache: {e.Message}");
            }
        }

        private bool HasFileChanged(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                long currentModified = fileInfo.LastWriteTime.Ticks;

                if (fileHashCache.TryGetValue(filePath, out long cachedModified))
                {
                    return currentModified != cachedModified;
                }

                return true; // File not in cache, consider it changed
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Error checking file modification: {e.Message}");
                return true; // On error, assume changed
            }
        }

        private void UpdateFileCache(string filePath, string fileId)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                fileHashCache[filePath] = fileInfo.LastWriteTime.Ticks;
                fileIdMap[filePath] = fileId;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Assistant] Error updating file cache: {e.Message}");
            }
        }

        private string GetDynamicAudioLibrary()
        {
            try
            {
                string audioPath = Path.Combine(Application.dataPath, "Resources", "Audio");

                if (!Directory.Exists(audioPath))
                {
                    return "AVAILABLE AUDIO FILES: None found (no Resources/Audio directory)";
                }

                var audioFiles = new List<string>();
                var supportedExtensions = new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };

                foreach (string file in Directory.GetFiles(audioPath, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file).ToLower();
                    if (supportedExtensions.Contains(extension))
                    {
                        // Get relative path from Resources/Audio
                        string relativePath = Path.GetRelativePath(audioPath, file);
                        // Remove extension and normalize path separators
                        string audioName = Path.ChangeExtension(relativePath, null).Replace('\\', '/');
                        audioFiles.Add(audioName);
                    }
                }

                if (audioFiles.Count == 0)
                {
                    return "AVAILABLE AUDIO FILES: None found";
                }

                // Sort and organize by directory
                audioFiles.Sort();

                var groupedFiles = audioFiles
                    .GroupBy(f => f.Contains('/') ? f.Substring(0, f.LastIndexOf('/')) : "root")
                    .OrderBy(g => g.Key);

                var result = new StringBuilder();
                result.AppendLine("AVAILABLE AUDIO FILES (use EXACT paths):");

                foreach (var group in groupedFiles)
                {
                    if (group.Key == "root")
                    {
                        // Files in root directory
                        result.AppendLine(string.Join(", ", group));
                    }
                    else
                    {
                        // Files in subdirectories
                        result.AppendLine($"{group.Key}/: {string.Join(", ", group.Select(f => f.Substring(f.LastIndexOf('/') + 1)))}");
                    }
                }

                Debug.Log($"[AI Assistant] Found {audioFiles.Count} audio files for AI context");
                return result.ToString().TrimEnd();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI Assistant] Failed to scan audio library: {e.Message}");
                return "AVAILABLE AUDIO FILES: Error scanning audio directory";
            }
        }

        private void OnApplicationQuit()
        {
            SaveFileCache();
            httpClient?.Dispose();
        }
    }
}