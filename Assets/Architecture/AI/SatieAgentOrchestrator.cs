using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Satie.AI
{
    /// <summary>
    /// Hierarchical orchestrator that coordinates specialist agents
    /// for fast, correct Satie code generation
    ///
    /// Architecture:
    /// - Orchestrator (Sonnet 4.5): Main code generation
    /// - Syntax Validator (Haiku 4.5): Parallel syntax checking
    /// - Library Checker (Haiku 4.5): Parallel sample validation
    /// - Compilation Verifier (Haiku 4.5): Post-generation error fixing
    /// </summary>
    public class SatieAgentOrchestrator : MonoBehaviour
    {
        private static SatieAgentOrchestrator _instance;
        public static SatieAgentOrchestrator Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SatieAgentOrchestrator");
                    _instance = go.AddComponent<SatieAgentOrchestrator>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("Model Configuration")]
        [SerializeField] private string orchestratorModel = "claude-sonnet-4-5-20250929";
        [SerializeField] private string specialistModel = "claude-haiku-4-5-20251001";

        private ILLMProvider _orchestrator;
        private ILLMProvider _specialist;

        private SyntaxValidatorAgent _syntaxValidator;
        private LibraryCheckerAgent _libraryChecker;
        private CompilationVerifierAgent _compilationVerifier;

        private bool _initialized = false;

        // Event for streaming updates
        public event Action<string> OnStreamUpdate;
        public event Action<GenerationMetrics> OnGenerationComplete;

        private void Start()
        {
            Initialize();
        }

        public async void Initialize()
        {
            if (_initialized) return;

            try
            {
                UnityEngine.Debug.Log("[Orchestrator] Initializing multi-agent system...");

                // Create providers
                _orchestrator = new AnthropicProvider(orchestratorModel);
                _specialist = new AnthropicProvider(specialistModel);

                // Create specialist agents
                _syntaxValidator = new SyntaxValidatorAgent(_specialist);
                _libraryChecker = new LibraryCheckerAgent(_specialist);
                _compilationVerifier = new CompilationVerifierAgent(_specialist);

                // Health check
                bool orchestratorHealthy = await _orchestrator.IsHealthyAsync();
                bool specialistHealthy = await _specialist.IsHealthyAsync();

                if (!orchestratorHealthy || !specialistHealthy)
                {
                    UnityEngine.Debug.LogError("[Orchestrator] Health check failed. Please configure Anthropic API key.");
                    return;
                }

                _initialized = true;
                UnityEngine.Debug.Log("[Orchestrator] Initialization complete!");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Orchestrator] Initialization failed: {e.Message}");
            }
        }

        /// <summary>
        /// Generate Satie code with full orchestration pipeline
        /// </summary>
        public async Task<CodeGenerationResult> GenerateCodeAsync(string userPrompt, string currentScript = null)
        {
            if (!_initialized)
            {
                UnityEngine.Debug.LogError("[Orchestrator] Not initialized. Call Initialize() first.");
                return new CodeGenerationResult { Success = false, Error = "Not initialized" };
            }

            var overallStopwatch = Stopwatch.StartNew();
            var metrics = new GenerationMetrics();

            try
            {
                // Step 1: Immediate acknowledgment
                OnStreamUpdate?.Invoke("Analyzing your request...");
                await Task.Delay(50); // Small delay for UI responsiveness

                // Step 2: Parallel specialist validation (FAST!)
                var validationStopwatch = Stopwatch.StartNew();

                var syntaxTask = _syntaxValidator.ValidateAsync(userPrompt);
                var libraryTask = _libraryChecker.CheckAsync(userPrompt);

                await Task.WhenAll(syntaxTask, libraryTask);

                var syntaxResult = await syntaxTask;
                var libraryResult = await libraryTask;

                validationStopwatch.Stop();
                metrics.ValidationLatencyMs = validationStopwatch.ElapsedMilliseconds;

                UnityEngine.Debug.Log($"[Orchestrator] Validation complete in {metrics.ValidationLatencyMs}ms");

                // Step 3: Build enriched prompt with constraints
                OnStreamUpdate?.Invoke("Generating code...");

                string enrichedPrompt = BuildEnrichedPrompt(
                    userPrompt,
                    currentScript,
                    syntaxResult,
                    libraryResult
                );

                // Step 4: Generate code with streaming
                var generationStopwatch = Stopwatch.StartNew();
                var codeBuilder = new StringBuilder();

                await foreach (var chunk in _orchestrator.StreamAsync(new GenerateRequest
                {
                    Prompt = enrichedPrompt,
                    SystemPrompt = BuildSystemPrompt(libraryResult),
                    Temperature = 0.7f,
                    MaxTokens = 4000,
                    UseCache = true
                }))
                {
                    codeBuilder.Append(chunk);
                    OnStreamUpdate?.Invoke(chunk);
                }

                generationStopwatch.Stop();
                metrics.GenerationLatencyMs = generationStopwatch.ElapsedMilliseconds;

                string generatedCode = CleanGeneratedCode(codeBuilder.ToString());

                // Step 5: Verify compilation (with self-correction)
                OnStreamUpdate?.Invoke("\n\nVerifying code...");

                var verificationStopwatch = Stopwatch.StartNew();
                var verificationResult = await VerifyAndRepairAsync(generatedCode);
                verificationStopwatch.Stop();

                metrics.VerificationLatencyMs = verificationStopwatch.ElapsedMilliseconds;
                metrics.RepairAttempts = verificationResult.attemptCount;

                overallStopwatch.Stop();
                metrics.TotalLatencyMs = overallStopwatch.ElapsedMilliseconds;

                // Step 6: Return result
                var result = new CodeGenerationResult
                {
                    Success = verificationResult.success,
                    Code = verificationResult.code,
                    Explanation = BuildExplanation(syntaxResult, libraryResult),
                    MissingSamples = libraryResult.MissingSamples?.ToList() ?? new List<string>(),
                    Metrics = metrics,
                    Error = verificationResult.error
                };

                OnGenerationComplete?.Invoke(metrics);

                UnityEngine.Debug.Log($"[Orchestrator] Generation complete! Total: {metrics.TotalLatencyMs}ms");
                return result;
            }
            catch (Exception e)
            {
                overallStopwatch.Stop();
                UnityEngine.Debug.LogError($"[Orchestrator] Generation failed: {e.Message}");

                return new CodeGenerationResult
                {
                    Success = false,
                    Error = e.Message,
                    Metrics = new GenerationMetrics { TotalLatencyMs = overallStopwatch.ElapsedMilliseconds }
                };
            }
        }

        #region Helper Methods

        private string BuildSystemPrompt(LibraryCheckResult libraryResult)
        {
            var availableAudio = _libraryChecker.GetAvailableAudio();
            var audioLibrary = FormatAudioLibrary(availableAudio);

            // Load language spec
            string langSpec = LoadLanguageSpec();

            return $@"{langSpec}

Output ONLY valid Satie code. No explanations, no markdown, no text before or after the code.

STRICT RULES:
- Your response must be pure Satie code only
- NO explanations or descriptions
- NO markdown code blocks
- NO ""Here's your code"" or similar text
- Start directly with the Satie code
- End directly with the Satie code

CRITICAL SYNTAX RULES (NO COLONS, NO QUOTES, NO EQUALS):
- Statements: loop audio/file (NOT loop ""audio/file"": or loop = ""audio/file"")
- Statements: oneshot audio/file every 2to5 (NOT oneshot ""audio/file"": every 2to5)
- Properties: volume 0.5 (NOT volume = 0.5 or volume: 0.5)
- Properties: pitch 0.8to1.2 (space-separated, NO equals)
- Move: move walk (NOT move = walk)
- Move: move fly (NOT move = fly)
- Move: move pos 5 0 10 (space-separated)
- Visual: visual trail (NOT visual = trail)
- Visual: visual sphere (NOT visual = sphere)
- Visual: visual cube (NOT visual = cube)
- Ranges: 0.5to1.0 (NO SPACES around 'to')
- Numbers: Use dots not commas (0.5 not 0,5)

{audioLibrary}

IMPORTANT: ONLY use audio files from the above list. Do NOT make up file paths.

Generate valid Satie code following these exact syntax rules.";
        }

        private string BuildEnrichedPrompt(string userPrompt, string currentScript, SyntaxValidationResult syntaxResult, LibraryCheckResult libraryResult)
        {
            var promptBuilder = new StringBuilder();

            // Add syntax requirements
            promptBuilder.AppendLine("CRITICAL SYNTAX RULES (NO COLONS, NO QUOTES, NO EQUALS):");
            promptBuilder.AppendLine("- Statements: loop audio/file (NOT loop = audio/file)");
            promptBuilder.AppendLine("- Statements: oneshot audio/file every 2to5");
            promptBuilder.AppendLine("- Properties: volume 0.5 (NOT volume = 0.5)");
            promptBuilder.AppendLine("- Properties: pitch 0.8to1.2 (space-separated)");
            promptBuilder.AppendLine("- Move: move walk (NOT move = walk)");
            promptBuilder.AppendLine("- Move: move fly (NOT move = fly)");
            promptBuilder.AppendLine("- Visual: visual trail (NOT visual = trail)");
            promptBuilder.AppendLine("- Ranges: 0.5to1.0 (NO SPACES around to)");
            promptBuilder.AppendLine();

            // Add available samples info
            if (libraryResult.AvailableSamples != null && libraryResult.AvailableSamples.Length > 0)
            {
                promptBuilder.AppendLine("AVAILABLE SAMPLES FOR THIS REQUEST:");
                foreach (var sample in libraryResult.AvailableSamples.Take(10))
                {
                    promptBuilder.AppendLine($"  - {sample}");
                }
                promptBuilder.AppendLine();
            }

            // Add current script context if exists
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

        private string FormatAudioLibrary(HashSet<string> audioFiles)
        {
            var grouped = audioFiles
                .GroupBy(f => f.Contains('/') ? f.Substring(0, f.LastIndexOf('/')) : "root")
                .OrderBy(g => g.Key);

            var result = new StringBuilder();
            result.AppendLine("AVAILABLE AUDIO FILES (use EXACT paths):");

            foreach (var group in grouped)
            {
                if (group.Key == "root")
                {
                    result.AppendLine(string.Join(", ", group));
                }
                else
                {
                    result.AppendLine($"{group.Key}/: {string.Join(", ", group.Select(f => f.Substring(f.LastIndexOf('/') + 1)))}");
                }
            }

            return result.ToString().TrimEnd();
        }

        private string BuildExplanation(SyntaxValidationResult syntaxResult, LibraryCheckResult libraryResult)
        {
            var explanation = new StringBuilder();

            if (libraryResult.AllSamplesAvailable)
            {
                explanation.AppendLine("All requested samples are available in your library.");
            }
            else if (libraryResult.MissingSamples?.Length > 0)
            {
                explanation.AppendLine($"Note: Some samples were not found: {string.Join(", ", libraryResult.MissingSamples)}");
                if (libraryResult.SuggestedAlternatives?.Length > 0)
                {
                    explanation.AppendLine($"Used alternatives: {string.Join(", ", libraryResult.SuggestedAlternatives)}");
                }
            }

            return explanation.ToString();
        }

        private async Task<(bool success, string code, string error, int attemptCount)> VerifyAndRepairAsync(string code)
        {
            int maxAttempts = 2;
            string currentCode = code;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // Try to parse the code
                var parseResult = SatieParser.TryParseScript(currentCode, out var statements, out var errors);

                if (parseResult)
                {
                    UnityEngine.Debug.Log($"[Orchestrator] Code verified successfully on attempt {attempt}");
                    return (true, currentCode, null, attempt);
                }

                // If this was the last attempt, return with error
                if (attempt == maxAttempts)
                {
                    UnityEngine.Debug.LogWarning($"[Orchestrator] Max repair attempts reached. Final errors:\n{errors}");
                    return (false, currentCode, errors, attempt);
                }

                // Try to repair
                UnityEngine.Debug.Log($"[Orchestrator] Attempting repair {attempt}/{maxAttempts}...");
                OnStreamUpdate?.Invoke($"\n\nFixing errors (attempt {attempt})...");

                currentCode = await _compilationVerifier.RepairCodeAsync(currentCode, errors, attempt);
            }

            return (false, code, "Failed to verify code", maxAttempts);
        }

        private string CleanGeneratedCode(string code)
        {
            code = code.Trim();

            // Remove markdown code blocks if present
            if (code.StartsWith("```"))
            {
                var lines = code.Split('\n');
                code = string.Join("\n", lines.Skip(1).Take(lines.Length - 2));
            }

            return code.Trim();
        }

        private string LoadLanguageSpec()
        {
            try
            {
                var specAsset = UnityEngine.Resources.Load<TextAsset>("AI/SATIE_LANGUAGE_SPEC");
                if (specAsset != null)
                {
                    return specAsset.text;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Orchestrator] Could not load language spec: {e.Message}");
            }

            // Fallback: inline minimal spec
            return @"SATIE SYNTAX:
Commands (NO colons): loop audio/file, oneshot audio/file every 2to5
Properties (space-separated): volume 0.5, pitch 0.8to1.2
Ranges: Use 'to' with NO SPACES (0.5to1.0)
Numbers: Use dots not commas (0.5 not 0,5)";
        }

        #endregion
    }

    #region Data Models

    [System.Serializable]
    public class CodeGenerationResult
    {
        public bool Success;
        public string Code;
        public string Explanation;
        public List<string> MissingSamples;
        public GenerationMetrics Metrics;
        public string Error;
    }

    [System.Serializable]
    public class GenerationMetrics
    {
        public long ValidationLatencyMs;
        public long GenerationLatencyMs;
        public long VerificationLatencyMs;
        public long TotalLatencyMs;
        public int RepairAttempts;

        public override string ToString()
        {
            return $@"Generation Metrics:
- Validation: {ValidationLatencyMs}ms
- Generation: {GenerationLatencyMs}ms
- Verification: {VerificationLatencyMs}ms
- Total: {TotalLatencyMs}ms
- Repair Attempts: {RepairAttempts}";
        }
    }

    #endregion
}
