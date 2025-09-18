using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Satie;

[CustomEditor(typeof(SatieAIAssistant))]
public class SatieAIAssistantEditor : Editor
{
    private string userPrompt = "";
    private string generatedCode = "";
    private bool isGenerating = false;
    private bool showAdvanced = false;
    private Vector2 scrollPosition;
    private Vector2 historyScrollPosition;

    // History management
    private List<string> codeHistory = new List<string>();
    private List<string> promptHistory = new List<string>();
    private int currentHistoryIndex = -1;

    private GUIStyle headerStyle;
    private GUIStyle codeStyle;

    private void OnEnable()
    {
        var assistant = target as SatieAIAssistant;
        if (assistant != null)
        {
            _ = assistant.Initialize();
        }
    }

    public override void OnInspectorGUI()
    {
        InitializeStyles();

        var assistant = target as SatieAIAssistant;

        EditorGUILayout.Space(10);

        // Header
        EditorGUILayout.LabelField("sAtIe", headerStyle);
        EditorGUILayout.HelpBox("Using OpenAI Assistants API v2 with File Search for state-of-the-art code generation", MessageType.Info);

        EditorGUILayout.Space(10);

        // Prompt Section
        EditorGUILayout.LabelField("Prompt", EditorStyles.boldLabel);
        userPrompt = EditorGUILayout.TextArea(userPrompt, GUILayout.Height(80));

        EditorGUILayout.Space(10);

        // Generate Button
        EditorGUI.BeginDisabledGroup(isGenerating || string.IsNullOrEmpty(userPrompt));
        if (GUILayout.Button(isGenerating ? "Generating..." : "✨ Generate Code", GUILayout.Height(35)))
        {
            GenerateCode(assistant);
        }
        EditorGUI.EndDisabledGroup();

        // History Section
        if (codeHistory.Count > 0)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField($"Generation History ({codeHistory.Count} items)", EditorStyles.boldLabel);

            historyScrollPosition = EditorGUILayout.BeginScrollView(historyScrollPosition, GUILayout.Height(200));

            for (int i = codeHistory.Count - 1; i >= 0; i--) // Newest first
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Show prompt that generated this code
                EditorGUILayout.LabelField($"#{codeHistory.Count - i}: \"{promptHistory[i]}\"", EditorStyles.miniLabel);

                // Show code preview (first few lines)
                string preview = GetCodePreview(codeHistory[i]);
                EditorGUILayout.LabelField(preview, codeStyle, GUILayout.Height(40));

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Restore", GUILayout.Width(60)))
                {
                    RestoreFromHistory(i);
                }

                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                {
                    GUIUtility.systemCopyBuffer = codeHistory[i];
                    Debug.Log($"[AI Assistant] History item #{codeHistory.Count - i} copied to clipboard");
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear History"))
            {
                if (EditorUtility.DisplayDialog("Clear History", "Are you sure you want to clear the generation history?", "Clear", "Cancel"))
                {
                    codeHistory.Clear();
                    promptHistory.Clear();
                    currentHistoryIndex = -1;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(10);

        // Advanced Settings
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Settings");
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

            var serializedObject = new SerializedObject(target);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("assistantId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("vectorStoreId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("model"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("assistantName"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reinitialize Assistant"))
            {
                _ = assistant.Initialize();
            }
            if (GUILayout.Button("Upload New Files"))
            {
                _ = UploadFiles(assistant);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        // Status
        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Status: " + (isGenerating ? "Generating code..." : "Ready") +
            "\nAssistant: " + (!string.IsNullOrEmpty(assistant.GetAssistantId()) ? "Connected" : "Not initialized") +
            "\nVector Store: " + (!string.IsNullOrEmpty(assistant.GetVectorStoreId()) ? "Active" : "Not created"),
            isGenerating ? MessageType.Info : MessageType.None
        );
    }

    private async void GenerateCode(SatieAIAssistant assistant)
    {
        isGenerating = true;
        generatedCode = "";

        try
        {
            Debug.Log($"[AI Assistant] Generating code with prompt: {userPrompt}");

            // Ensure assistant is initialized
            if (string.IsNullOrEmpty(assistant.GetAssistantId()))
            {
                Debug.Log("[AI Assistant] Initializing assistant...");
                bool initialized = await assistant.Initialize();
                if (!initialized)
                {
                    Debug.LogError("[AI Assistant] Failed to initialize");
                    isGenerating = false;
                    return;
                }
            }

            // Get current script from Satie runtime
            string currentScript = GetCurrentSatieScript();

            // Generate code
            generatedCode = await assistant.GenerateCode(userPrompt, currentScript);

            if (string.IsNullOrEmpty(generatedCode))
            {
                Debug.LogError("[AI Assistant] No code generated");
            }
            else
            {
                Debug.Log("[AI Assistant] Code generated successfully");

                // Clean up the response (remove markdown if present)
                generatedCode = CleanGeneratedCode(generatedCode);

                // Add to history before applying
                AddToHistory(userPrompt, generatedCode);

                // Auto-apply the generated code (no buttons needed)
                ApplyGeneratedCodeAuto();

                // Clear prompt for next use
                userPrompt = "";
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AI Assistant] Generation error: {e.Message}");
            generatedCode = $"// Error: {e.Message}";
        }
        finally
        {
            isGenerating = false;
            Repaint();
        }
    }

    private async Task UploadFiles(SatieAIAssistant assistant)
    {
        try
        {
            Debug.Log("[AI Assistant] Uploading project files...");
            await assistant.UploadProjectFiles();
            Debug.Log("[AI Assistant] Files uploaded successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AI Assistant] Upload error: {e.Message}");
        }
    }

    private string CleanGeneratedCode(string code)
    {
        // Remove markdown code blocks if present
        if (code.Contains("```"))
        {
            var lines = code.Split('\n');
            bool inCodeBlock = false;
            var cleanedLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock || !line.Trim().StartsWith("```"))
                {
                    cleanedLines.Add(line);
                }
            }

            code = string.Join("\n", cleanedLines);
        }

        return code.Trim();
    }

    private void AddToHistory(string prompt, string code)
    {
        codeHistory.Add(code);
        promptHistory.Add(prompt);
        currentHistoryIndex = codeHistory.Count - 1;

        // Limit history to 20 items to keep UI manageable
        if (codeHistory.Count > 20)
        {
            codeHistory.RemoveAt(0);
            promptHistory.RemoveAt(0);
            currentHistoryIndex--;
        }
    }

    private string GetCodePreview(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";

        var lines = code.Split('\n');
        var preview = "";
        int lineCount = 0;

        foreach (var line in lines)
        {
            if (lineCount >= 2) break;
            if (!string.IsNullOrWhiteSpace(line))
            {
                preview += line.Trim() + "\n";
                lineCount++;
            }
        }

        if (lines.Length > 2)
        {
            preview += "...";
        }

        return preview.Trim();
    }

    private void RestoreFromHistory(int index)
    {
        if (index >= 0 && index < codeHistory.Count)
        {
            string codeToRestore = codeHistory[index];
            string promptUsed = promptHistory[index];

            Debug.Log($"[AI Assistant] Restoring from history: \"{promptUsed}\"");

            // Apply the historical code
            ApplyCodeToScript(codeToRestore);

            // Add to history as a new entry (restoration)
            AddToHistory($"Restored: {promptUsed}", codeToRestore);
        }
    }

    private void ApplyGeneratedCodeAuto()
    {
        if (string.IsNullOrEmpty(generatedCode))
        {
            Debug.LogError("[AI Assistant] No generated code to apply");
            return;
        }

        ApplyCodeToScript(generatedCode);
    }

    private void ApplyCodeToScript(string code)
    {
        try
        {
            // Find the SatieRuntime in the scene
            var satieRuntime = FindObjectOfType<SatieRuntime>();
            if (satieRuntime == null)
            {
                Debug.LogError("[AI Assistant] No SatieRuntime found in scene. Please add one to apply the code.");
                return;
            }

            // Get the current TextAsset
            if (satieRuntime.ScriptFile == null)
            {
                Debug.LogError("[AI Assistant] No script file assigned to SatieRuntime. Please assign a TextAsset first.");
                return;
            }

            // Get asset path and write the code
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(satieRuntime.ScriptFile);

            if (!string.IsNullOrEmpty(assetPath))
            {
                // Write the code to the file
                System.IO.File.WriteAllText(assetPath, code);

                // Refresh the asset database
                UnityEditor.AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                UnityEditor.AssetDatabase.Refresh();

                // Auto Shift+R: Sync if in play mode (like the original SatieAICodeGen)
                if (Application.isPlaying)
                {
                    Debug.Log("[AI Assistant] Auto-syncing runtime (Shift+R)...");
                    satieRuntime.Sync(fullReset: true);
                }

                Debug.Log($"[AI Assistant] Code auto-applied to {System.IO.Path.GetFileName(assetPath)}");
            }
            else
            {
                Debug.LogError("[AI Assistant] Could not find asset path for current script file");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AI Assistant] Failed to apply code: {e.Message}");
        }
    }

    private string GetCurrentSatieScript()
    {
        try
        {
            // Try to find the SatieRuntime in the scene
            var satieRuntime = FindObjectOfType<SatieRuntime>();
            if (satieRuntime != null)
            {
                // Get the script text from SatieRuntime's TextAsset
                var scriptProperty = typeof(SatieRuntime).GetProperty("ScriptFile");
                if (scriptProperty != null)
                {
                    var textAsset = scriptProperty.GetValue(satieRuntime) as TextAsset;
                    if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
                    {
                        Debug.Log($"[AI Assistant] Found current script: {textAsset.text.Length} characters from {textAsset.name}");
                        return textAsset.text;
                    }
                }

                // Fallback: try to access private field directly
                var scriptField = typeof(SatieRuntime).GetField("scriptFile",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (scriptField != null)
                {
                    var textAsset = scriptField.GetValue(satieRuntime) as TextAsset;
                    if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
                    {
                        Debug.Log($"[AI Assistant] Found current script via fallback: {textAsset.text.Length} characters from {textAsset.name}");
                        return textAsset.text;
                    }
                }
            }

            Debug.LogWarning("[AI Assistant] No SatieRuntime found or script is empty. Make sure you have a SatieRuntime component with a script file assigned.");
            return "";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AI Assistant] Failed to get current script: {e.Message}");
            return "";
        }
    }


    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (codeStyle == null)
        {
            codeStyle = new GUIStyle(EditorStyles.textArea)
            {
                font = Font.CreateDynamicFontFromOSFont("Courier New", 12),
                wordWrap = true
            };
        }
    }
}