using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Satie
{
    [System.Serializable]
    public class SatieAIConfig
    {
        [Header("Authentication")]
        public string apiKeyPath = "api_key.txt";
        public bool sendAuthorizationHeader = true;
        public string authorizationHeaderName = "Authorization";
        [TextArea(1, 3)]
        public string authorizationHeaderValueTemplate = "Bearer {API_KEY}";

        [Header("Endpoint")] 
        public string apiBaseUrl = "https://api.openai.com";
        public string chatCompletionsPath = "/v1/chat/completions";
        public string providerId = "openai";

        [Header("Model")] 
        public string model = "gpt-5"; // Options: gpt-5, gpt-4-turbo-preview, gpt-4, gpt-3.5-turbo
        public float temperature = 1.0f;
        public int maxTokens = 4000;

        [Header("Custom Headers")]
        public List<SatieAIRequestHeader> additionalHeaders = new List<SatieAIRequestHeader>();

        [Header("RLHF Settings")]
        public bool enableRLHF = true;
        public string rlhfDataPath = "rlhf_feedback.json";
    }

    [System.Serializable]
    public class SatieAIRequestHeader
    {
        public string name;
        [TextArea(1, 3)]
        public string value;
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
        private string cachedResourcePrompt;
        private AudioResourceSnapshot cachedResourceSnapshot;
        private readonly object resourceCacheLock = new object();
        private float lastResourceScanTime = -1f;
        private const float RESOURCE_CACHE_DURATION = 30f; // 30 seconds for faster updates after generation
        private string lastKnownScriptSnapshot = string.Empty;

#if !UNITY_WEBGL
        private FileSystemWatcher audioWatcher;
        private volatile bool pendingAudioCacheInvalidation;
        private readonly List<FileSystemWatcher> knowledgeWatchers = new List<FileSystemWatcher>();
        private volatile bool pendingKnowledgeCacheInvalidation;
#endif

        private static readonly JsonSerializerOptions ChatSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private static readonly JsonSerializerOptions KnowledgeSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private static readonly Regex KnowledgeTokenRegex = new Regex("[A-Za-z0-9_]+", RegexOptions.Compiled);

        private const int MAX_SCRIPT_CONTEXT_CHARS = 6000;
        private const int MAX_SUMMARY_LINES = 12;
        private const int MAX_CONVERSATION_MESSAGES = 6;
        private const int MAX_CONVERSATION_MESSAGE_CHARS = 1200;

        private const string KNOWLEDGE_INDEX_DIRECTORY_NAME = "AIIndex";
        private const string KNOWLEDGE_INDEX_FILE_NAME = "knowledge_index.json";
        private const int KNOWLEDGE_MAX_SNIPPETS = 4;
        private const int KNOWLEDGE_SNIPPET_MAX_CHARS = 800;
        private const int KNOWLEDGE_CHUNK_SIZE = 1200;
        private const int KNOWLEDGE_CHUNK_OVERLAP = 200;
        private const int KNOWLEDGE_QUERY_SCRIPT_CHARS = 1500;

        // Conversation context for follow-up editing
        private ConversationHistory currentConversation;
        private bool isEditMode = false;

        private const string SYSTEM_PROMPT_BASE = "Output ONLY valid Satie code. No markdown or explanations.\n\n" +
            "SYNTAX RULES:\n" +
            "- ALWAYS add colon: loop \"clip\": or oneshot \"clip\":\n" +
            "- Birds/footsteps/bicycles: oneshot with \"every\"\n" +
            "- Ambience/music: loop\n" +
            "- Move: walk,x,z,speed OR fly,x,y,z,speed OR pos,x,y,z\n" +
            "- Visual: sphere OR trail OR \"sphere and trail\" (NOT true/false)\n" +
            "- NO 'overlap' with multiplied oneshots\n\n";

        private string projectRootPath;
        private string knowledgeIndexDirectory;
        private string knowledgeIndexFilePath;
        private RagIndexCache cachedKnowledgeIndex;
        private Task<RagIndexCache> knowledgeIndexBuildTask;
        private readonly object knowledgeIndexLock = new object();
        private bool knowledgeIndexDirty = true;

        private sealed class AudioResourceSnapshot
        {
            public readonly List<AudioCategorySummary> Categories = new List<AudioCategorySummary>();

            public int TotalSamples
            {
                get
                {
                    int total = 0;
                    foreach (var category in Categories)
                    {
                        total += category.TotalSampleCount;
                    }
                    return total;
                }
            }

            public string ToPromptString()
            {
                var builder = new StringBuilder();
                builder.AppendLine("AUDIO LIBRARY SNAPSHOT:");

                if (Categories.Count == 0)
                {
                    builder.AppendLine("  (no audio files found)");
                    return builder.ToString();
                }

                foreach (var category in Categories
                    .OrderByDescending(c => c.TotalSampleCount)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append("- ");
                    builder.Append(category.Name);
                    builder.Append(" (");
                    builder.Append(category.TotalSampleCount);
                    builder.Append(category.TotalSampleCount == 1 ? " sample" : " samples");

                    if (!string.IsNullOrEmpty(category.SuggestedUsage))
                    {
                        builder.Append(", suggest ");
                        builder.Append(category.SuggestedUsage);
                    }

                    builder.AppendLine(")");

                    if (category.SequentialRanges.Count > 0)
                    {
                        builder.Append("    sequential: ");
                        builder.AppendLine(string.Join(", ", category.SequentialRanges.Select(r =>
                            FormatSampleRange(category.Name, r))));
                    }

                    if (category.NamedSamples.Count > 0)
                    {
                        var trimmed = category.NamedSamples
                            .Distinct()
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .Select(n => $"{category.Name}/{n}");
                        builder.Append("    named: ");
                        builder.AppendLine(string.Join(", ", trimmed));

                        if (category.NamedSamples.Count > 8)
                        {
                            builder.AppendLine($"    ... (+{category.NamedSamples.Count - 8} more)");
                        }
                    }
                }

                builder.AppendLine("Use folder/sample notation e.g., oneshot \"birds/1-4\" or loop \"ambience/forest\".");
                return builder.ToString();
            }
        }

        private sealed class AudioCategorySummary
        {
            public string Name { get; }
            public List<AudioRange> SequentialRanges { get; } = new List<AudioRange>();
            public List<string> NamedSamples { get; } = new List<string>();
            public string SuggestedUsage { get; set; }
            public int TotalSampleCount => SequentialRanges.Sum(r => r.Length) + NamedSamples.Count;

            public AudioCategorySummary(string name)
            {
                Name = name;
            }
        }

        private readonly struct AudioRange
        {
            public int Start { get; }
            public int End { get; }
            public int Length => End - Start + 1;

            public AudioRange(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        private sealed class KnowledgeSourceFile
        {
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public long LastWriteTicks { get; set; }
        }

        private sealed class RagChunkRecord
        {
            public string Source { get; set; }
            public string Content { get; set; }
            public Dictionary<string, float> TermWeights { get; set; }
        }

        private sealed class RagIndexCache
        {
            public List<RagChunkRecord> Chunks { get; set; } = new List<RagChunkRecord>();
            public Dictionary<string, long> SourceFileVersions { get; set; } = new Dictionary<string, long>();
        }

        private sealed class ChatCompletionMessage
        {
            [JsonPropertyName("role")] public string Role { get; set; }
            [JsonPropertyName("content")] public string Content { get; set; }

            public ChatCompletionMessage() { }

            public ChatCompletionMessage(string role, string content)
            {
                Role = role;
                Content = content;
            }
        }

        private sealed class ChatCompletionResponse
        {
            [JsonPropertyName("choices")] public ChatCompletionChoice[] Choices { get; set; }
        }

        private sealed class ChatCompletionChoice
        {
            [JsonPropertyName("message")] public ChatCompletionResponseMessage Message { get; set; }
            [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }
        }

        private sealed class ChatCompletionResponseMessage
        {
            [JsonPropertyName("role")] public string Role { get; set; }
            [JsonPropertyName("content")] public string Content { get; set; }
        }

        private sealed class ChatCompletionRequestData
        {
            public string Model { get; set; }
            public List<ChatCompletionMessage> Messages { get; set; }
            public float Temperature { get; set; }
            public int MaxTokens { get; set; }
        }

        private sealed class ChatCompletionResult
        {
            public string Content { get; set; }
            public string FinishReason { get; set; }
        }

        private interface IChatCompletionProviderAdapter
        {
            string Id { get; }
            HttpRequestMessage CreateRequest(SatieAIConfig config, ChatCompletionRequestData request, JsonSerializerOptions serializerOptions);
            ChatCompletionResult ParseResponse(string responseJson);
        }

        private sealed class OpenAIChatCompletionAdapter : IChatCompletionProviderAdapter
        {
            public string Id => "openai";

            public HttpRequestMessage CreateRequest(SatieAIConfig config, ChatCompletionRequestData request, JsonSerializerOptions serializerOptions)
            {
                if (config == null) throw new ArgumentNullException(nameof(config));
                if (request == null) throw new ArgumentNullException(nameof(request));

                var payload = new
                {
                    model = request.Model,
                    messages = request.Messages,
                    temperature = request.Temperature,
                    max_tokens = request.MaxTokens
                };

                string json = JsonSerializer.Serialize(payload, serializerOptions);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, ResolveEndpoint(config));
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return httpRequest;
            }

            public ChatCompletionResult ParseResponse(string responseJson)
            {
                var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, ChatSerializerOptions);
                var choice = parsed?.Choices?.FirstOrDefault();

                return new ChatCompletionResult
                {
                    Content = choice?.Message?.Content,
                    FinishReason = choice?.FinishReason
                };
            }

            private static Uri ResolveEndpoint(SatieAIConfig config)
            {
                string baseUrl = string.IsNullOrWhiteSpace(config.apiBaseUrl)
                    ? "https://api.openai.com"
                    : config.apiBaseUrl;
                string path = string.IsNullOrWhiteSpace(config.chatCompletionsPath)
                    ? "/v1/chat/completions"
                    : config.chatCompletionsPath;

                if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
                {
                    return absolute;
                }

                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
                {
                    baseUri = new Uri("https://api.openai.com/");
                }

                if (Uri.TryCreate(baseUri, path, out var combined))
                {
                    return combined;
                }

                string sanitized = path.TrimStart('/');
                return new Uri(baseUri, sanitized);
            }
        }

        private static readonly OpenAIChatCompletionAdapter OpenAIProvider = new OpenAIChatCompletionAdapter();
        private static readonly Dictionary<string, IChatCompletionProviderAdapter> ProviderAdapters =
            new Dictionary<string, IChatCompletionProviderAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai", OpenAIProvider },
                { "openai-compatible", OpenAIProvider }
            };

        private IChatCompletionProviderAdapter ResolveProviderAdapter()
        {
            string providerId = config?.providerId;
            if (!string.IsNullOrWhiteSpace(providerId) &&
                ProviderAdapters.TryGetValue(providerId, out var adapter))
            {
                return adapter;
            }

            if (!string.IsNullOrWhiteSpace(providerId))
            {
                Debug.LogWarning($"[AI] Unknown provider '{providerId}', defaulting to OpenAI-compatible adapter.");
            }

            return OpenAIProvider;
        }

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
            
            // Pre-cache resource info
            _ = RefreshResourceCache();

#if !UNITY_WEBGL
            InitializeAudioWatcher();
#endif

            projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            knowledgeIndexDirectory = Path.Combine(Application.dataPath, KNOWLEDGE_INDEX_DIRECTORY_NAME);
            knowledgeIndexFilePath = Path.Combine(knowledgeIndexDirectory, KNOWLEDGE_INDEX_FILE_NAME);

#if !UNITY_WEBGL
            InitializeKnowledgeWatchers();
#endif

            _ = GetKnowledgeIndexAsync();
        }

        // Force re-indexing when called (e.g., after audio generation)
        public void InvalidateAudioCache()
        {
            lastResourceScanTime = -1f;
            cachedResourcePrompt = null;
            Debug.Log("[AI] Audio cache invalidated - will re-index on next generation");
        }

#if !UNITY_WEBGL
        private void InitializeAudioWatcher()
        {
            try
            {
                string audioRoot = Path.Combine(Application.dataPath, "Resources", "Audio");
                if (!Directory.Exists(audioRoot))
                {
                    return;
                }

                audioWatcher = new FileSystemWatcher(audioRoot)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite
                };

                audioWatcher.Changed += OnAudioLibraryChanged;
                audioWatcher.Created += OnAudioLibraryChanged;
                audioWatcher.Deleted += OnAudioLibraryChanged;
                audioWatcher.Renamed += OnAudioLibraryRenamed;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Cache] Failed to watch audio library: {e.Message}");
            }
        }

        private void OnAudioLibraryChanged(object sender, FileSystemEventArgs e)
        {
            pendingAudioCacheInvalidation = true;
        }

        private void OnAudioLibraryRenamed(object sender, RenamedEventArgs e)
        {
            pendingAudioCacheInvalidation = true;
        }

        private void InitializeKnowledgeWatchers()
        {
            try
            {
                foreach (var target in EnumerateKnowledgeWatchTargets())
                {
                    string path = target.path;
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    {
                        continue;
                    }

                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = target.includeSubdirectories,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size |
                                       NotifyFilters.DirectoryName
                    };

                    watcher.Changed += OnKnowledgeSourceChanged;
                    watcher.Created += OnKnowledgeSourceChanged;
                    watcher.Deleted += OnKnowledgeSourceChanged;
                    watcher.Renamed += OnKnowledgeSourceRenamed;
                    watcher.EnableRaisingEvents = true;
                    knowledgeWatchers.Add(watcher);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to initialize watchers: {e.Message}");
            }
        }

        private void DisposeKnowledgeWatchers()
        {
            foreach (var watcher in knowledgeWatchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= OnKnowledgeSourceChanged;
                    watcher.Created -= OnKnowledgeSourceChanged;
                    watcher.Deleted -= OnKnowledgeSourceChanged;
                    watcher.Renamed -= OnKnowledgeSourceRenamed;
                    watcher.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AI Knowledge] Failed to dispose watcher: {e.Message}");
                }
            }

            knowledgeWatchers.Clear();
        }

        private void OnKnowledgeSourceChanged(object sender, FileSystemEventArgs e)
        {
            if (IsPotentialKnowledgePath(e.FullPath))
            {
                pendingKnowledgeCacheInvalidation = true;
            }
        }

        private void OnKnowledgeSourceRenamed(object sender, RenamedEventArgs e)
        {
            if (Directory.Exists(e.FullPath) || Directory.Exists(e.OldFullPath) ||
                IsPotentialKnowledgePath(e.FullPath) || IsPotentialKnowledgePath(e.OldFullPath))
            {
                pendingKnowledgeCacheInvalidation = true;
            }
        }

        private bool IsPotentialKnowledgePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

            try
            {
                if (Directory.Exists(fullPath))
                {
                    return false;
                }

                if (!IsSupportedKnowledgeFile(fullPath))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(knowledgeIndexFilePath))
                {
                    string normalizedIndex = Path.GetFullPath(knowledgeIndexFilePath);
                    string normalizedPath = Path.GetFullPath(fullPath);
                    if (string.Equals(normalizedIndex, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<(string path, bool includeSubdirectories)> EnumerateKnowledgeWatchTargets()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(Application.dataPath) && Directory.Exists(Application.dataPath))
            {
                string normalized = Path.GetFullPath(Application.dataPath);
                if (seen.Add(normalized))
                {
                    yield return (Application.dataPath, true);
                }
            }

            if (!string.IsNullOrEmpty(projectRootPath) && Directory.Exists(projectRootPath))
            {
                string rootNormalized = Path.GetFullPath(projectRootPath);
                if (seen.Add(rootNormalized))
                {
                    yield return (projectRootPath, false);
                }

                string[] optional =
                {
                    "Docs",
                    "docs",
                    "Documentation",
                    "documentation",
                    "Samples",
                    "Guides",
                    "SatieSyntaxVSCode"
                };

                foreach (var folder in optional)
                {
                    string candidate = Path.Combine(projectRootPath, folder);
                    if (!Directory.Exists(candidate))
                    {
                        continue;
                    }

                    string normalized = Path.GetFullPath(candidate);
                    if (seen.Add(normalized))
                    {
                        yield return (candidate, true);
                    }
                }
            }
        }
#endif

        void Update()
        {
#if !UNITY_WEBGL
            if (pendingAudioCacheInvalidation)
            {
                pendingAudioCacheInvalidation = false;
                InvalidateResourceCache();
            }

            if (pendingKnowledgeCacheInvalidation)
            {
                pendingKnowledgeCacheInvalidation = false;
                InvalidateKnowledgeIndex();
                _ = GetKnowledgeIndexAsync();
            }
#endif
        }

        private void OnDestroy()
        {
            httpClient?.Dispose();
#if !UNITY_WEBGL
            if (audioWatcher != null)
            {
                audioWatcher.EnableRaisingEvents = false;
                audioWatcher.Changed -= OnAudioLibraryChanged;
                audioWatcher.Created -= OnAudioLibraryChanged;
                audioWatcher.Deleted -= OnAudioLibraryChanged;
                audioWatcher.Renamed -= OnAudioLibraryRenamed;
                audioWatcher.Dispose();
                audioWatcher = null;
            }

            DisposeKnowledgeWatchers();
#endif

            if (instance == this)
            {
                instance = null;
            }
        }

        private async Task<string> GetDynamicAudioLibrary()
        {
            // Check if we need to rescan (cache expired or not cached)
            if (Time.time - lastResourceScanTime > RESOURCE_CACHE_DURATION || string.IsNullOrEmpty(cachedResourcePrompt))
            {
                cachedResourcePrompt = await ScanAudioResources();
                lastResourceScanTime = Time.time;
                Debug.Log($"[AI] Re-indexed audio library");
            }
            return cachedResourcePrompt;
        }

        private async Task<string> ScanAudioResources()
        {
            return await Task.Run(() =>
            {
                var snapshot = BuildAudioResourceSnapshot();
                lock (resourceCacheLock)
                {
                    cachedResourceSnapshot = snapshot;
                }

                return snapshot.ToPromptString();
            });
        }

        private AudioResourceSnapshot BuildAudioResourceSnapshot()
        {
            var snapshot = new AudioResourceSnapshot();
            string audioPath = Path.Combine(Application.dataPath, "Resources", "Audio");

            if (!Directory.Exists(audioPath))
            {
                return snapshot;
            }

            var directories = Directory.GetDirectories(audioPath);
            foreach (var directory in directories)
            {
                string categoryName = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(categoryName))
                {
                    continue;
                }

                var summary = new AudioCategorySummary(categoryName);
                PopulateCategorySummary(directory, summary);

                if (summary.TotalSampleCount > 0)
                {
                    summary.SuggestedUsage = InferUsageFromName(categoryName);
                    snapshot.Categories.Add(summary);
                }
            }

            return snapshot;
        }

        private void PopulateCategorySummary(string directory, AudioCategorySummary summary)
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(HasSupportedAudioExtension)
                .ToList();

            if (files.Count == 0)
            {
                return;
            }

            var numericIds = new List<int>();

            foreach (var file in files)
            {
                string relative = Path.GetRelativePath(directory, file);
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                relative = relative.Replace('\\', '/');
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(relative);

                if (!relative.Contains('/') && int.TryParse(nameWithoutExtension, out int number))
                {
                    numericIds.Add(number);
                }
                else
                {
                    summary.NamedSamples.Add(RemoveAudioExtension(relative));
                }
            }

            numericIds.Sort();
            foreach (var range in CollapseToRanges(numericIds))
            {
                summary.SequentialRanges.Add(range);
            }
        }

        private static IEnumerable<AudioRange> CollapseToRanges(List<int> sortedNumbers)
        {
            if (sortedNumbers == null || sortedNumbers.Count == 0)
            {
                yield break;
            }

            int start = sortedNumbers[0];
            int previous = start;

            for (int i = 1; i < sortedNumbers.Count; i++)
            {
                int current = sortedNumbers[i];
                if (current == previous + 1)
                {
                    previous = current;
                    continue;
                }

                yield return new AudioRange(start, previous);
                start = previous = current;
            }

            yield return new AudioRange(start, previous);
        }

        private static bool HasSupportedAudioExtension(string filePath)
        {
            return filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                   filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveAudioExtension(string relativePath)
        {
            int extensionIndex = relativePath.LastIndexOf('.');
            return extensionIndex >= 0 ? relativePath.Substring(0, extensionIndex) : relativePath;
        }

        private static string InferUsageFromName(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
            {
                return string.Empty;
            }

            string lower = categoryName.ToLowerInvariant();

            if (lower.Contains("amb") || lower.Contains("pad") || lower.Contains("drone") || lower.Contains("music"))
            {
                return "loop";
            }

            if (lower.Contains("step") || lower.Contains("foot") || lower.Contains("hit") || lower.Contains("impact") ||
                lower.Contains("fx") || lower.Contains("oneshot") || lower.Contains("whoosh"))
            {
                return "oneshot";
            }

            if (lower.Contains("voice") || lower.Contains("dialog") || lower.Contains("speech"))
            {
                return "oneshot (dialogue)";
            }

            if (lower.Contains("loop"))
            {
                return "loop";
            }

            return "loop or oneshot";
        }

        private static string FormatSampleRange(string categoryName, AudioRange range)
        {
            return range.Start == range.End
                ? $"{categoryName}/{range.Start}"
                : $"{categoryName}/{range.Start}-{range.End}";
        }

        private string ResolveCurrentScript(string explicitScript)
        {
            if (!string.IsNullOrWhiteSpace(explicitScript))
            {
                lastKnownScriptSnapshot = explicitScript;
                return explicitScript;
            }

            if (!string.IsNullOrWhiteSpace(lastKnownScriptSnapshot))
            {
                return lastKnownScriptSnapshot;
            }

            try
            {
                var runtime = FindObjectOfType<SatieRuntime>();
                if (runtime != null && runtime.ScriptFile != null)
                {
                    string runtimeScript = runtime.ScriptFile.text;
                    if (!string.IsNullOrWhiteSpace(runtimeScript))
                    {
                        lastKnownScriptSnapshot = runtimeScript;
                        return runtimeScript;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI] Unable to capture current script from runtime: {e.Message}");
            }

            return string.Empty;
        }

        private string BuildScriptContextBlock(string scriptText)
        {
            string normalized = NormalizeLineEndings(scriptText).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("CURRENT SCRIPT SNAPSHOT:");

            int summaryCount = 0;
            foreach (var summary in SummarizeScriptBlocks(normalized))
            {
                builder.AppendLine($"- {summary}");
                summaryCount++;
                if (summaryCount >= MAX_SUMMARY_LINES)
                {
                    builder.AppendLine("- ...");
                    break;
                }
            }

            builder.AppendLine("FULL SCRIPT (truncated if long):");
            builder.AppendLine(ClampForPrompt(normalized, MAX_SCRIPT_CONTEXT_CHARS));

            return builder.ToString();
        }

        private IEnumerable<string> SummarizeScriptBlocks(string scriptText)
        {
            if (string.IsNullOrEmpty(scriptText))
            {
                yield break;
            }

            var lines = scriptText.Split('\n');
            var pattern = new Regex(@"^\s*(?:(\d+)\s*\*\s*)?(loop|oneshot)\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                var match = pattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                string countText = match.Groups[1].Success ? match.Groups[1].Value : "1";
                string type = match.Groups[2].Value.ToLowerInvariant();
                string clip = match.Groups[3].Value;

                int indent = CountLeadingWhitespace(line);
                var details = new List<string>();

                for (int j = i + 1; j < lines.Length; j++)
                {
                    string nextLine = lines[j];
                    if (string.IsNullOrWhiteSpace(nextLine))
                    {
                        continue;
                    }

                    int nextIndent = CountLeadingWhitespace(nextLine);
                    if (nextIndent <= indent)
                    {
                        break;
                    }

                    string trimmed = nextLine.Trim();
                    if (trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    details.Add(trimmed);
                    if (details.Count >= 3)
                    {
                        break;
                    }
                }

                string detailText = details.Count > 0 ? $" [{string.Join("; ", details)}]" : string.Empty;
                yield return $"{countText} x {type} \"{clip}\"{detailText}";
            }
        }

        private async Task<string> BuildSystemPromptAsync(string scriptText, string userPrompt, bool includeEditInstructions)
        {
            string audioLibrary = await GetDynamicAudioLibrary();
            string knowledgeBlock = await BuildKnowledgeContextAsync(scriptText, userPrompt);

            var builder = new StringBuilder();
            builder.Append(SYSTEM_PROMPT_BASE);

            if (!string.IsNullOrEmpty(audioLibrary))
            {
                builder.AppendLine(audioLibrary.TrimEnd());
            }

            if (!string.IsNullOrEmpty(knowledgeBlock))
            {
                if (!string.IsNullOrEmpty(audioLibrary))
                {
                    builder.AppendLine();
                }

                builder.AppendLine(knowledgeBlock.TrimEnd());
            }

            string scriptContext = BuildScriptContextBlock(scriptText);
            if (!string.IsNullOrEmpty(scriptContext))
            {
                if (!string.IsNullOrEmpty(audioLibrary) || !string.IsNullOrEmpty(knowledgeBlock))
                {
                    builder.AppendLine();
                }

                builder.AppendLine(scriptContext.TrimEnd());
            }

            if (includeEditInstructions)
            {
                builder.AppendLine();
                builder.AppendLine("EDIT MODE: Make ONLY requested changes. Output COMPLETE modified script.");
            }

            return builder.ToString();
        }

        private static string NormalizeLineEndings(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string ClampForPrompt(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, maxChars) + "\n...";
        }

        private static int CountLeadingWhitespace(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return 0;
            }

            int count = 0;
            while (count < line.Length && char.IsWhiteSpace(line[count]))
            {
                count++;
            }

            return count;
        }

        private static string ResolveHeaderTemplate(string template, string apiKey)
        {
            string safeTemplate = template ?? string.Empty;
            return string.IsNullOrEmpty(apiKey)
                ? safeTemplate.Replace("{API_KEY}", string.Empty)
                : safeTemplate.Replace("{API_KEY}", apiKey);
        }

        private void ApplyConfiguredHeaders(HttpRequestMessage request)
        {
            if (request == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(apiKey) && config.sendAuthorizationHeader)
            {
                string headerName = string.IsNullOrWhiteSpace(config.authorizationHeaderName)
                    ? "Authorization"
                    : config.authorizationHeaderName.Trim();

                string headerValue = ResolveHeaderTemplate(config.authorizationHeaderValueTemplate, apiKey);

                if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(headerValue))
                {
                    request.Headers.Remove(headerName);
                    request.Headers.TryAddWithoutValidation(headerName, headerValue);
                }
            }

            if (config.additionalHeaders != null)
            {
                foreach (var header in config.additionalHeaders)
                {
                    if (header == null || string.IsNullOrWhiteSpace(header.name))
                    {
                        continue;
                    }

                    string resolved = ResolveHeaderTemplate(header.value, apiKey);
                    if (string.IsNullOrWhiteSpace(resolved))
                    {
                        continue;
                    }

                    request.Headers.Remove(header.name);
                    request.Headers.TryAddWithoutValidation(header.name, resolved);
                }
            }
        }

        private async Task<string> SendChatCompletionAsync(List<ChatCompletionMessage> messages, string logPrefix, CancellationToken cancellationToken = default)
        {
            const int maxRetries = 3;
            var adapter = ResolveProviderAdapter();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var requestData = new ChatCompletionRequestData
                    {
                        Model = config.model,
                        Messages = messages,
                        Temperature = config.temperature,
                        MaxTokens = config.maxTokens
                    };

                    using var httpRequest = adapter.CreateRequest(config, requestData, ChatSerializerOptions);
                    ApplyConfiguredHeaders(httpRequest);

                    Debug.Log($"[{logPrefix}] Attempt {attempt}/{maxRetries} using provider {adapter.Id} model {config.model} (temp={config.temperature}, max_tokens={config.maxTokens}) -> {httpRequest.RequestUri}");

                    using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                    string responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogError($"[{logPrefix}] API error ({response.StatusCode}): {responseText}");

                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            return $"# Error: API request failed - {response.StatusCode}\n# Details: {responseText}";
                        }

                        if (attempt == maxRetries)
                        {
                            return $"# Error: API request failed after {maxRetries} attempts - {response.StatusCode}\n# Details: {responseText}";
                        }

                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    ChatCompletionResult result;
                    try
                    {
                        result = adapter.ParseResponse(responseText);
                    }
                    catch (Exception parseEx)
                    {
                        Debug.LogError($"[{logPrefix}] Failed to parse response: {parseEx.Message}\nPayload: {responseText}");
                        return $"# Error: Failed to parse AI response - {parseEx.Message}";
                    }

                    string contentText = result?.Content;

                    if (!string.IsNullOrEmpty(result?.FinishReason) && result.FinishReason.Equals("length", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[{logPrefix}] Response hit token limit (attempt {attempt})");
                    }

                    if (!string.IsNullOrEmpty(contentText))
                    {
                        Debug.Log($"[{logPrefix}] Success - received {contentText.Length} characters");
                        return contentText;
                    }

                    Debug.LogWarning($"[{logPrefix}] Empty response content (attempt {attempt})");
                    if (attempt == maxRetries)
                    {
                        return "# Error: No response from AI";
                    }

                    await Task.Delay(500 * attempt, cancellationToken);
                }
                catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    Debug.LogWarning($"[{logPrefix}] Request timed out on attempt {attempt}: {e.Message}");
                    if (attempt == maxRetries)
                    {
                        return "# Error: Request timed out after multiple attempts. Try a shorter prompt or check your connection.";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{logPrefix}] Exception on attempt {attempt}: {e.Message}");
                    if (attempt == maxRetries)
                    {
                        return $"# Error: {e.Message}";
                    }

                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return "# Error: All retry attempts failed";
        }

        public async Task<string> TestAPIConnection()
        {
            if (!await LoadApiKey())
            {
                return "API key not found";
            }

            try
            {
                var adapter = ResolveProviderAdapter();
                Debug.Log($"[AI Test] Testing chat endpoint for provider '{adapter.Id}'...");

                var messages = new List<ChatCompletionMessage>
                {
                    new ChatCompletionMessage("user", "Say 'test'")
                };

                var requestData = new ChatCompletionRequestData
                {
                    Model = string.IsNullOrWhiteSpace(config.model) ? "gpt-3.5-turbo" : config.model,
                    Messages = messages,
                    Temperature = 0f,
                    MaxTokens = Mathf.Clamp(config.maxTokens > 0 ? config.maxTokens : 32, 16, 64)
                };

                using var request = adapter.CreateRequest(config, requestData, ChatSerializerOptions);
                ApplyConfiguredHeaders(request);

                using var response = await httpClient.SendAsync(request);
                string responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[AI Test] Chat endpoint failed ({response.StatusCode}): {responseText}");
                    return $"Chat endpoint failed: {response.StatusCode}";
                }

                ChatCompletionResult result;
                try
                {
                    result = adapter.ParseResponse(responseText);
                }
                catch (Exception parseEx)
                {
                    Debug.LogError($"[AI Test] Unable to parse chat response: {parseEx.Message}\nPayload: {responseText}");
                    return $"Chat response parse error: {parseEx.Message}";
                }

                string content = result?.Content ?? string.Empty;
                Debug.Log($"[AI Test] Chat endpoint status: {response.StatusCode}, content preview: {ClampForPrompt(content, 120)}");

                return $"Chat endpoint succeeded: {response.StatusCode}";
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

            try
            {
                string scriptSnapshot = ResolveCurrentScript(string.Empty);
                string systemPrompt = await BuildSystemPromptAsync(scriptSnapshot, prompt, includeEditInstructions: false);

                var messages = new List<ChatCompletionMessage>
                {
                    new ChatCompletionMessage("system", systemPrompt),
                    new ChatCompletionMessage("user", prompt)
                };

                Debug.Log($"[AI Debug] Using model: {config.model} (temp={config.temperature}, max_tokens={config.maxTokens})");
                Debug.Log($"[AI Debug] Prompt chars: {prompt.Length}, Script chars: {scriptSnapshot.Length}");

                return await SendChatCompletionAsync(messages, "AI Request");
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
            string scriptSnapshot = ResolveCurrentScript(string.Empty);
            string systemPrompt = await BuildSystemPromptAsync(scriptSnapshot, prompt, includeEditInstructions: false);
            return await GenerateSatieCodeWithCustomSystem(prompt, systemPrompt);
        }

        private async Task<string> GenerateSatieCodeWithCustomSystem(string prompt, string systemPrompt)
        {
            if (!await LoadApiKey())
            {
                return "# Error: API key not found. Please create Assets/api_key.txt with your OpenAI API key.";
            }

            var messages = new List<ChatCompletionMessage>
            {
                new ChatCompletionMessage("system", systemPrompt),
                new ChatCompletionMessage("user", prompt)
            };

            return await SendChatCompletionAsync(messages, "AI Request");
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
        
        private async Task RefreshResourceCache()
        {
            try
            {
                cachedResourcePrompt = await ScanAudioResources();
                lastResourceScanTime = Time.realtimeSinceStartup;
                int categoryCount = 0;
                lock (resourceCacheLock)
                {
                    categoryCount = cachedResourceSnapshot?.Categories.Count ?? 0;
                }
                Debug.Log($"[AI Cache] Resource cache refreshed ({categoryCount} categories)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Cache] Failed to refresh resource cache: {e.Message}");
            }
        }

        public void InvalidateResourceCache()
        {
            cachedResourcePrompt = null;
            lock (resourceCacheLock)
            {
                cachedResourceSnapshot = null;
            }
            lastResourceScanTime = -1f;
            Debug.Log("[AI Cache] Resource cache invalidated");
        }

        public void InvalidateKnowledgeIndex()
        {
            lock (knowledgeIndexLock)
            {
                knowledgeIndexDirty = true;
            }

            Debug.Log("[AI Knowledge] Knowledge index invalidated");
        }

        private async Task<RagIndexCache> GetKnowledgeIndexAsync()
        {
            Task<RagIndexCache> buildTask;
            RagIndexCache snapshot;

            lock (knowledgeIndexLock)
            {
                if (!knowledgeIndexDirty && cachedKnowledgeIndex != null)
                {
                    return cachedKnowledgeIndex;
                }

                if (knowledgeIndexBuildTask == null || knowledgeIndexBuildTask.IsCompleted)
                {
                    knowledgeIndexBuildTask = Task.Run(BuildKnowledgeIndex);
                }

                buildTask = knowledgeIndexBuildTask;
                snapshot = cachedKnowledgeIndex;
            }

            if (snapshot != null)
            {
                _ = FinalizeKnowledgeIndexBuildAsync(buildTask);
                return snapshot;
            }

            await FinalizeKnowledgeIndexBuildAsync(buildTask);

            lock (knowledgeIndexLock)
            {
                return cachedKnowledgeIndex ?? new RagIndexCache();
            }
        }

        private async Task FinalizeKnowledgeIndexBuildAsync(Task<RagIndexCache> buildTask)
        {
            try
            {
                var index = await buildTask.ConfigureAwait(false);

                lock (knowledgeIndexLock)
                {
                    cachedKnowledgeIndex = index ?? new RagIndexCache();
                    knowledgeIndexDirty = false;

                    if (knowledgeIndexBuildTask == buildTask)
                    {
                        knowledgeIndexBuildTask = null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Index build failed: {e.Message}");

                lock (knowledgeIndexLock)
                {
                    if (knowledgeIndexBuildTask == buildTask)
                    {
                        knowledgeIndexBuildTask = null;
                    }

                    knowledgeIndexDirty = true;
                }
            }
        }

        private RagIndexCache BuildKnowledgeIndex()
        {
            try
            {
                Directory.CreateDirectory(knowledgeIndexDirectory);

                var sources = CollectKnowledgeSourceFiles();
                var sourceMap = sources.ToDictionary(s => s.RelativePath, s => s.LastWriteTicks, StringComparer.OrdinalIgnoreCase);

                var existing = LoadKnowledgeIndexFromDisk();
                if (existing != null && IsIndexCurrent(existing, sourceMap))
                {
                    Debug.Log($"[AI Knowledge] Using cached index ({existing.Chunks.Count} chunks)");
                    return existing;
                }

                var index = new RagIndexCache();

                foreach (var source in sources)
                {
                    string text = SafeReadAllText(source.FullPath);
                    index.SourceFileVersions[source.RelativePath] = source.LastWriteTicks;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    foreach (var chunk in ChunkKnowledgeContent(source.RelativePath, text))
                    {
                        if (chunk != null)
                        {
                            index.Chunks.Add(chunk);
                        }
                    }
                }

                PersistKnowledgeIndex(index);
                Debug.Log($"[AI Knowledge] Rebuilt index with {index.Chunks.Count} chunks from {sources.Count} files");

                return index;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to build index: {e.Message}");
                return cachedKnowledgeIndex ?? new RagIndexCache();
            }
        }

        private RagIndexCache LoadKnowledgeIndexFromDisk()
        {
            try
            {
                if (string.IsNullOrEmpty(knowledgeIndexFilePath) || !File.Exists(knowledgeIndexFilePath))
                {
                    return null;
                }

                string json = File.ReadAllText(knowledgeIndexFilePath);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                var index = JsonSerializer.Deserialize<RagIndexCache>(json, KnowledgeSerializerOptions) ?? new RagIndexCache();
                index.Chunks ??= new List<RagChunkRecord>();
                index.SourceFileVersions ??= new Dictionary<string, long>();
                return index;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to load cached index: {e.Message}");
                return null;
            }
        }

        private void PersistKnowledgeIndex(RagIndexCache index)
        {
            try
            {
                if (string.IsNullOrEmpty(knowledgeIndexFilePath))
                {
                    return;
                }

                Directory.CreateDirectory(knowledgeIndexDirectory);
                string json = JsonSerializer.Serialize(index, KnowledgeSerializerOptions);
                File.WriteAllText(knowledgeIndexFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to persist index: {e.Message}");
            }
        }

        private static bool IsIndexCurrent(RagIndexCache existing, Dictionary<string, long> sourceMap)
        {
            if (existing == null)
            {
                return false;
            }

            foreach (var kvp in sourceMap)
            {
                if (!existing.SourceFileVersions.TryGetValue(kvp.Key, out long timestamp) || timestamp != kvp.Value)
                {
                    return false;
                }
            }

            foreach (var key in existing.SourceFileVersions.Keys)
            {
                if (!sourceMap.ContainsKey(key))
                {
                    return false;
                }
            }

            return existing.Chunks != null && existing.Chunks.Count > 0;
        }

        private List<KnowledgeSourceFile> CollectKnowledgeSourceFiles()
        {
            var results = new List<KnowledgeSourceFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFile(string filePath)
            {
                if (string.IsNullOrEmpty(filePath) || !IsSupportedKnowledgeFile(filePath))
                {
                    return;
                }

                string relative = GetProjectRelativePath(filePath);
                if (!seen.Add(relative))
                {
                    return;
                }

                try
                {
                    var info = new FileInfo(filePath);
                    results.Add(new KnowledgeSourceFile
                    {
                        FullPath = filePath,
                        RelativePath = relative,
                        LastWriteTicks = info.LastWriteTimeUtc.Ticks
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AI Knowledge] Unable to read metadata for {filePath}: {e.Message}");
                }
            }

            void Traverse(string root, bool recursive)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    return;
                }

                if (!recursive)
                {
                    foreach (var file in SafeGetFiles(root))
                    {
                        AddFile(file);
                    }
                    return;
                }

                var stack = new Stack<string>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();

                    foreach (var directory in SafeGetDirectories(current))
                    {
                        if (ShouldSkipKnowledgeDirectory(directory))
                        {
                            continue;
                        }

                        stack.Push(directory);
                    }

                    foreach (var file in SafeGetFiles(current))
                    {
                        AddFile(file);
                    }
                }
            }

            if (!string.IsNullOrEmpty(Application.dataPath))
            {
                Traverse(Application.dataPath, true);
            }

            if (!string.IsNullOrEmpty(projectRootPath))
            {
                string[] additionalFolders =
                {
                    "Docs",
                    "docs",
                    "Documentation",
                    "documentation",
                    "Samples",
                    "Guides",
                    "SatieSyntaxVSCode"
                };

                foreach (var folder in additionalFolders)
                {
                    Traverse(Path.Combine(projectRootPath, folder), true);
                }

                Traverse(projectRootPath, false);
            }

            if (!string.IsNullOrEmpty(knowledgeIndexFilePath))
            {
                string normalizedIndex = Path.GetFullPath(knowledgeIndexFilePath);
                results.RemoveAll(r => string.Equals(Path.GetFullPath(r.FullPath), normalizedIndex, StringComparison.OrdinalIgnoreCase));
            }

            return results;
        }

        private IEnumerable<RagChunkRecord> ChunkKnowledgeContent(string relativePath, string content)
        {
            string normalized = NormalizeLineEndings(content).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                yield break;
            }

            var nameTokens = TokenizeText(Path.GetFileName(relativePath));
            int start = 0;

            while (start < normalized.Length)
            {
                int length = Math.Min(KNOWLEDGE_CHUNK_SIZE, normalized.Length - start);
                int end = start + length;

                if (end < normalized.Length)
                {
                    int newline = normalized.LastIndexOf('\n', end - 1, length);
                    if (newline > start + KNOWLEDGE_CHUNK_SIZE / 2)
                    {
                        end = newline + 1;
                    }
                }

                string chunkText = normalized.Substring(start, end - start).Trim();
                if (!string.IsNullOrEmpty(chunkText))
                {
                    var tokens = TokenizeText(chunkText);
                    if (tokens.Count > 0)
                    {
                        tokens.AddRange(nameTokens);
                        var weights = BuildTermWeights(tokens);
                        yield return new RagChunkRecord
                        {
                            Source = relativePath.Replace('\\', '/'),
                            Content = chunkText,
                            TermWeights = weights
                        };
                    }
                }

                if (end >= normalized.Length)
                {
                    break;
                }

                int nextStart = Math.Max(end - KNOWLEDGE_CHUNK_OVERLAP, start + 1);
                if (nextStart <= start)
                {
                    nextStart = end;
                }

                start = nextStart;
            }
        }

        private static List<string> TokenizeText(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            foreach (Match match in KnowledgeTokenRegex.Matches(text))
            {
                string token = match.Value.ToLowerInvariant();
                if (token.Length <= 1)
                {
                    continue;
                }

                tokens.Add(token);
            }

            return tokens;
        }

        private static Dictionary<string, float> BuildTermWeights(IEnumerable<string> tokens)
        {
            var counts = new Dictionary<string, int>();
            int total = 0;

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (!counts.TryGetValue(token, out int count))
                {
                    count = 0;
                }

                counts[token] = count + 1;
                total++;
            }

            var weights = new Dictionary<string, float>(counts.Count);
            if (total <= 0)
            {
                return weights;
            }

            foreach (var kvp in counts)
            {
                weights[kvp.Key] = kvp.Value / (float)total;
            }

            return weights;
        }

        private static float ScoreChunkAgainstQuery(RagChunkRecord chunk, Dictionary<string, float> queryWeights)
        {
            if (chunk?.TermWeights == null || queryWeights == null || queryWeights.Count == 0)
            {
                return 0f;
            }

            float score = 0f;

            foreach (var kvp in queryWeights)
            {
                if (chunk.TermWeights.TryGetValue(kvp.Key, out float weight))
                {
                    score += weight * kvp.Value;
                }
            }

            return score;
        }

        private static IEnumerable<string> SafeGetDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeGetFiles(string path)
        {
            try
            {
                return Directory.GetFiles(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool ShouldSkipKnowledgeDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                return true;
            }

            string name = Path.GetFileName(directoryPath);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (name.StartsWith('.', StringComparison.Ordinal))
            {
                return true;
            }

            switch (name.ToLowerInvariant())
            {
                case "library":
                case "logs":
                case "obj":
                case "temp":
                case "build":
                case "builds":
                case "packages":
                case KNOWLEDGE_INDEX_DIRECTORY_NAME:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSupportedKnowledgeFile(string filePath)
        {
            string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return extension switch
            {
                ".sat" => true,
                ".satie" => true,
                ".cs" => true,
                ".uxml" => true,
                ".uss" => true,
                ".shader" => true,
                ".txt" => true,
                ".md" => true,
                _ => false
            };
        }

        private string GetProjectRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

            try
            {
                string projectRoot = projectRootPath ?? Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string normalized = Path.GetFullPath(fullPath);
                string relative = Path.GetRelativePath(projectRoot, normalized);
                return relative.Replace('\\', '/');
            }
            catch
            {
                return fullPath.Replace('\\', '/');
            }
        }

        private static string SafeReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to read {path}: {e.Message}");
                return string.Empty;
            }
        }

        private async Task<string> BuildKnowledgeContextAsync(string scriptText, string userPrompt)
        {
            try
            {
                var index = await GetKnowledgeIndexAsync();
                var chunks = index?.Chunks;
                if (chunks == null || chunks.Count == 0)
                {
                    return string.Empty;
                }

                var queryTokens = new List<string>();

                if (!string.IsNullOrWhiteSpace(userPrompt))
                {
                    var promptTokens = TokenizeText(userPrompt);
                    if (promptTokens.Count > 0)
                    {
                        // Weight the active request a little higher than passive script context.
                        queryTokens.AddRange(promptTokens);
                        queryTokens.AddRange(promptTokens);
                    }
                }

                string normalizedScript = NormalizeLineEndings(scriptText ?? string.Empty);
                if (!string.IsNullOrEmpty(normalizedScript))
                {
                    if (normalizedScript.Length > KNOWLEDGE_QUERY_SCRIPT_CHARS)
                    {
                        normalizedScript = normalizedScript.Substring(0, KNOWLEDGE_QUERY_SCRIPT_CHARS);
                    }

                    var scriptTokens = TokenizeText(normalizedScript);
                    if (scriptTokens.Count > 0)
                    {
                        queryTokens.AddRange(scriptTokens);
                    }
                }

                if (queryTokens.Count == 0)
                {
                    return string.Empty;
                }

                var queryWeights = BuildTermWeights(queryTokens);
                if (queryWeights == null || queryWeights.Count == 0)
                {
                    return string.Empty;
                }

                var scoredChunks = new List<(RagChunkRecord chunk, float score)>();
                foreach (var chunk in chunks)
                {
                    if (chunk == null || string.IsNullOrWhiteSpace(chunk.Content))
                    {
                        continue;
                    }

                    float score = ScoreChunkAgainstQuery(chunk, queryWeights);
                    if (score <= 0f)
                    {
                        continue;
                    }

                    scoredChunks.Add((chunk, score));
                }

                if (scoredChunks.Count == 0)
                {
                    return string.Empty;
                }

                scoredChunks.Sort((a, b) =>
                {
                    int scoreCompare = b.score.CompareTo(a.score);
                    if (scoreCompare != 0)
                    {
                        return scoreCompare;
                    }

                    string sourceA = a.chunk?.Source ?? string.Empty;
                    string sourceB = b.chunk?.Source ?? string.Empty;
                    return string.Compare(sourceA, sourceB, StringComparison.OrdinalIgnoreCase);
                });

                var builder = new StringBuilder();
                var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int appended = 0;

                foreach (var entry in scoredChunks)
                {
                    if (entry.chunk == null)
                    {
                        continue;
                    }

                    string source = entry.chunk.Source ?? string.Empty;
                    if (!seenSources.Add(source))
                    {
                        continue;
                    }

                    string snippet = NormalizeLineEndings(entry.chunk.Content ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(snippet))
                    {
                        continue;
                    }

                    snippet = ClampForPrompt(snippet, KNOWLEDGE_SNIPPET_MAX_CHARS)?.Trim();
                    if (string.IsNullOrEmpty(snippet))
                    {
                        continue;
                    }

                    if (appended == 0)
                    {
                        builder.AppendLine("PROJECT KNOWLEDGE SNIPPETS:");
                    }

                    builder.AppendLine($"[{appended + 1}] {source}");
                    builder.AppendLine("    " + snippet.Replace("\n", "\n    "));
                    builder.AppendLine();

                    appended++;
                    if (appended >= KNOWLEDGE_MAX_SNIPPETS)
                    {
                        break;
                    }
                }

                if (appended == 0)
                {
                    return string.Empty;
                }

                return builder.ToString().TrimEnd();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI Knowledge] Failed to build knowledge context: {e.Message}");
                return string.Empty;
            }
        }

        // Conversation context management
        public void StartNewConversation(string scriptSnapshot)
        {
            string resolvedScript = string.IsNullOrWhiteSpace(scriptSnapshot)
                ? ResolveCurrentScript(string.Empty)
                : scriptSnapshot;

            resolvedScript = NormalizeLineEndings(resolvedScript ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(resolvedScript))
            {
                lastKnownScriptSnapshot = resolvedScript;
            }

            currentConversation = new ConversationHistory
            {
                messages = Array.Empty<ConversationMessage>(),
                currentScript = resolvedScript
            };

            isEditMode = false;
            Debug.Log("[AI Conversation] Started new conversation");
        }

        private void UpdateConversationScript(string scriptSnapshot)
        {
            if (currentConversation == null)
            {
                return;
            }

            string normalized = NormalizeLineEndings(scriptSnapshot ?? string.Empty);
            currentConversation.currentScript = normalized;

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                lastKnownScriptSnapshot = normalized;
            }
        }

        public void SetEditMode(bool enabled, string currentScript = "")
        {
            string resolvedScript = ResolveCurrentScript(currentScript);

            if (enabled)
            {
                if (currentConversation == null)
                {
                    StartNewConversation(resolvedScript);
                }
                else
                {
                    UpdateConversationScript(resolvedScript);
                }
                isEditMode = true;
            }
            else
            {
                UpdateConversationScript(resolvedScript);
                isEditMode = false;
            }

            Debug.Log($"[AI Conversation] Edit mode: {enabled}");
        }

        private static bool IsErrorResponse(string response)
        {
            return !string.IsNullOrEmpty(response) &&
                   response.StartsWith("# Error", StringComparison.OrdinalIgnoreCase);
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

            var messagesList = new List<ConversationMessage>();
            if (currentConversation.messages != null)
            {
                messagesList.AddRange(currentConversation.messages);
            }
            messagesList.Add(newMessage);
            currentConversation.messages = messagesList.ToArray();
        }

        private List<ChatCompletionMessage> BuildConversationMessages(string systemPrompt)
        {
            var messages = new List<ChatCompletionMessage>
            {
                new ChatCompletionMessage("system", systemPrompt)
            };

            var history = currentConversation?.messages;
            if (history == null || history.Length == 0)
            {
                return messages;
            }

            int startIndex = Mathf.Max(0, history.Length - MAX_CONVERSATION_MESSAGES);
            for (int i = startIndex; i < history.Length; i++)
            {
                var historyMessage = history[i];
                string role = string.Equals(historyMessage.role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant"
                    : "user";

                string normalized = NormalizeLineEndings(historyMessage.content ?? string.Empty);
                string trimmed = ClampForPrompt(normalized, MAX_CONVERSATION_MESSAGE_CHARS);

                messages.Add(new ChatCompletionMessage(role, trimmed));
            }

            return messages;
        }

        public async Task<string> GenerateWithFollowUp(string prompt, string currentScript = "")
        {
            string scriptSnapshot = ResolveCurrentScript(currentScript);

            if (isEditMode && currentConversation != null)
            {
                UpdateConversationScript(scriptSnapshot);
                AddMessageToConversation("user", prompt);

                string result = await GenerateWithConversationContext(scriptSnapshot, prompt);

                if (!IsErrorResponse(result))
                {
                    AddMessageToConversation("assistant", result);
                    UpdateConversationScript(result);
                }

                return result;
            }

            StartNewConversation(scriptSnapshot);
            AddMessageToConversation("user", prompt);

            string generation = await GenerateWithResourceAwareness(prompt);

            if (!IsErrorResponse(generation))
            {
                AddMessageToConversation("assistant", generation);
                UpdateConversationScript(generation);
            }

            return generation;
        }

        private async Task<string> GenerateWithConversationContext(string scriptSnapshot, string latestUserPrompt)
        {
            if (!await LoadApiKey())
            {
                return "# Error: API key not found. Please create Assets/api_key.txt with your OpenAI API key.";
            }

            try
            {
                string workingScript = currentConversation?.currentScript;
                if (string.IsNullOrWhiteSpace(workingScript))
                {
                    workingScript = scriptSnapshot;
                }

                workingScript = NormalizeLineEndings(workingScript ?? string.Empty);

                string systemPrompt = await BuildSystemPromptAsync(workingScript, latestUserPrompt, includeEditInstructions: true);
                var messages = BuildConversationMessages(systemPrompt);

                int historyCount = Mathf.Max(0, messages.Count - 1);
                Debug.Log($"[AI Edit] Using model: {config.model} (temp={config.temperature}, max_tokens={config.maxTokens})");
                Debug.Log($"[AI Edit] Script chars: {workingScript.Length}, history messages: {historyCount}");

                return await SendChatCompletionAsync(messages, "AI Edit");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Follow-up generation error: {e.Message}");
                return $"# Error: {e.Message}";
            }
        }

    }
}