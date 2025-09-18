using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Satie;

[CustomEditor(typeof(SatieAICodeGen))]
public class SatieAICodeGenEditor : Editor
{
    private SatieAICodeGen aiGen;

    // UI State
    private string aiPrompt = "";
    private bool isGeneratingCode = false;
    private string lastGeneratedCode = "";
    private string lastPrompt = "";
    private bool editMode = false;

    // Editor foldouts
    private bool showAdvancedSettings = false;
    private bool showConversationHistory = false;

    void OnEnable()
    {
        aiGen = (SatieAICodeGen)target;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("AI Code Generation", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Generation section (prompt first)
        DrawGenerationSection();

        // Edit mode and conversation history
        if (editMode)
        {
            DrawEditModeSection();
        }

        // Results section
        if (!string.IsNullOrEmpty(lastGeneratedCode))
        {
            DrawResultsSection();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space(5);

        // Configuration section (moved to end)
        DrawConfigurationSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawConfigurationSection()
    {
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Configuration", true);

        if (showAdvancedSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("config.model"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("config.temperature"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("config.maxTokens"));

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("config.enableRLHF"));

            if (aiGen.config.enableRLHF)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("config.rlhfDataPath"));
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("config.apiKeyPath"));
            if (GUILayout.Button("Test API", GUILayout.Width(80)))
            {
                TestAPIConnection();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }
    }

    private void DrawGenerationSection()
    {
        EditorGUILayout.LabelField("Generate Satie Code", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Edit Mode:", GUILayout.Width(80));
        bool newEditMode = EditorGUILayout.Toggle(editMode);
        if (newEditMode != editMode)
        {
            SetEditMode(newEditMode);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Prompt:");
        aiPrompt = EditorGUILayout.TextArea(aiPrompt, GUILayout.Height(60));

        EditorGUI.BeginDisabledGroup(isGeneratingCode || string.IsNullOrWhiteSpace(aiPrompt));

        Color bgColor = GUI.backgroundColor;
        GUI.backgroundColor = isGeneratingCode ? Color.yellow : Color.green;

        string buttonText = isGeneratingCode ? "Generating..." : "Generate Code";
        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            GenerateCode();
        }

        GUI.backgroundColor = bgColor;
        EditorGUI.EndDisabledGroup();

        if (!string.IsNullOrEmpty(lastPrompt))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox($"Last prompt: {lastPrompt}", MessageType.Info);
        }
    }

    private void DrawEditModeSection()
    {
        if (!aiGen.IsInEditMode()) return;

        EditorGUILayout.Space(10);

        showConversationHistory = EditorGUILayout.Foldout(showConversationHistory, "Conversation History", true);

        if (showConversationHistory)
        {
            EditorGUI.indentLevel++;
            DrawConversationHistory();
            EditorGUI.indentLevel--;
        }
    }

    private void DrawResultsSection()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Generated Code:", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical("box");
        Vector2 scrollPos = Vector2.zero;
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(200));

        GUIStyle codeStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            fontStyle = FontStyle.Normal
        };

        EditorGUILayout.TextArea(lastGeneratedCode, codeStyle);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Copy to Clipboard", GUILayout.Height(25)))
        {
            GUIUtility.systemCopyBuffer = lastGeneratedCode;
            Debug.Log("Generated code copied to clipboard");
        }

        if (GUILayout.Button("Apply to Runtime", GUILayout.Height(25)))
        {
            ApplyGeneratedCode();
        }

        EditorGUILayout.EndHorizontal();

        // RLHF Feedback
        if (aiGen.config.enableRLHF && !string.IsNullOrEmpty(lastGeneratedCode))
        {
            DrawRLHFFeedback();
        }
    }

    private void DrawRLHFFeedback()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Feedback (RLHF):", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        Color bgColor = GUI.backgroundColor;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("✓ Correct", GUILayout.Height(25)))
        {
            RecordFeedback(true);
        }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("✗ Incorrect", GUILayout.Height(25)))
        {
            RecordFeedback(false);
        }

        GUI.backgroundColor = bgColor;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawConversationHistory()
    {
        var conversation = aiGen.GetCurrentConversation();
        if (conversation?.messages == null || conversation.messages.Length == 0)
        {
            EditorGUILayout.LabelField("No conversation history yet.", EditorStyles.miniLabel);
            return;
        }

        foreach (var message in conversation.messages)
        {
            string roleLabel = message.role == "user" ? "[You]" : "[AI]";
            string preview = message.content.Length > 100
                ? message.content.Substring(0, 100) + "..."
                : message.content;

            EditorGUILayout.LabelField($"{roleLabel}: {preview}", EditorStyles.wordWrappedMiniLabel);
        }
    }

    private async void GenerateCode()
    {
        if (string.IsNullOrWhiteSpace(aiPrompt)) return;

        isGeneratingCode = true;
        lastPrompt = aiPrompt;
        Repaint();

        try
        {
            // Get current script from SatieRuntime if it exists
            string currentScript = "";
            var runtime = FindObjectOfType<SatieRuntime>();
            if (runtime != null && runtime.ScriptFile != null)
            {
                currentScript = runtime.ScriptFile.text;
            }

            string result = await aiGen.GenerateWithFollowUp(aiPrompt, currentScript);

            if (!string.IsNullOrEmpty(result))
            {
                lastGeneratedCode = result;

                if (!result.StartsWith("# Error"))
                {
                    Debug.Log($"[AI] Successfully generated {result.Length} characters of Satie code");
                }
                else
                {
                    Debug.LogError($"[AI] Generation failed: {result}");
                    EditorUtility.DisplayDialog("Generation Failed",
                        result.Replace("# Error: ", ""), "OK");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Code generation error: {e.Message}");
            EditorUtility.DisplayDialog("Generation Error",
                $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            isGeneratingCode = false;
            Repaint();
        }
    }

    private void ApplyGeneratedCode()
    {
        if (string.IsNullOrEmpty(lastGeneratedCode))
        {
            Debug.LogError("No generated code to apply");
            return;
        }

        Debug.Log("Starting to apply generated code...");

        // Find SatieRuntime in the scene
        var runtime = FindObjectOfType<SatieRuntime>();
        if (runtime == null)
        {
            Debug.LogError("No SatieRuntime found in scene");
            EditorUtility.DisplayDialog("No Runtime Found",
                "Please add a SatieRuntime component to a GameObject in the scene.", "OK");
            return;
        }

        Debug.Log($"Found SatieRuntime on GameObject: {runtime.gameObject.name}");

        try
        {
            // Create or update the script file
            string path = $"Assets/Generated/generated_{DateTime.Now:yyyyMMdd_HHmmss}.sp";
            string directory = System.IO.Path.GetDirectoryName(path);

            Debug.Log($"Creating file at: {path}");

            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                Debug.Log($"Created directory: {directory}");
            }

            System.IO.File.WriteAllText(path, lastGeneratedCode);
            Debug.Log($"Wrote {lastGeneratedCode.Length} characters to file");

            AssetDatabase.Refresh();
            Debug.Log("AssetDatabase refreshed");

            // Wait a frame for asset database to process
            EditorApplication.delayCall += () => {
                // Assign to runtime
                TextAsset scriptAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (scriptAsset != null)
                {
                    Debug.Log($"Successfully loaded TextAsset: {scriptAsset.name}");

                    SerializedObject runtimeSO = new SerializedObject(runtime);
                    SerializedProperty scriptProp = runtimeSO.FindProperty("scriptFile");

                    if (scriptProp != null)
                    {
                        scriptProp.objectReferenceValue = scriptAsset;
                        runtimeSO.ApplyModifiedProperties();

                        Debug.Log($"Applied generated code to SatieRuntime: {path}");

                        // Trigger sync if in play mode
                        if (Application.isPlaying)
                        {
                            Debug.Log("Triggering runtime sync...");
                            runtime.Sync(fullReset: true);
                        }

                        EditorUtility.DisplayDialog("Code Applied",
                            $"Generated code has been applied to {runtime.gameObject.name}\n\nFile: {path}", "OK");
                    }
                    else
                    {
                        Debug.LogError("Could not find 'scriptFile' property on SatieRuntime");
                    }
                }
                else
                {
                    Debug.LogError($"Failed to load TextAsset at path: {path}");
                    EditorUtility.DisplayDialog("Apply Failed",
                        $"Could not load the generated script file.\n\nExpected path: {path}", "OK");
                }
            };
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error applying generated code: {e.Message}");
            EditorUtility.DisplayDialog("Apply Error",
                $"Error applying code: {e.Message}", "OK");
        }
    }

    private async void TestAPIConnection()
    {
        try
        {
            string result = await aiGen.TestAPIConnection();
            EditorUtility.DisplayDialog("API Test Result", result, "OK");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("API Test Failed",
                $"Error: {e.Message}", "OK");
        }
    }

    private void RecordFeedback(bool wasCorrect)
    {
        if (string.IsNullOrEmpty(lastPrompt) || string.IsNullOrEmpty(lastGeneratedCode))
        {
            Debug.LogWarning("No generation to provide feedback for");
            return;
        }

        aiGen.RecordRLHFFeedback(lastPrompt, lastGeneratedCode, wasCorrect);

        string feedbackText = wasCorrect ? "positive" : "negative";
        Debug.Log($"[RLHF] Recorded {feedbackText} feedback");

        EditorUtility.DisplayDialog("Feedback Recorded",
            $"Thank you! Your {feedbackText} feedback has been recorded.", "OK");
    }

    private void SetEditMode(bool enabled)
    {
        editMode = enabled;

        string currentScript = "";
        var runtime = FindObjectOfType<SatieRuntime>();
        if (runtime != null && runtime.ScriptFile != null)
        {
            currentScript = runtime.ScriptFile.text;
        }

        aiGen.SetEditMode(enabled, currentScript);

        if (enabled)
        {
            Debug.Log("[AI] Edit mode enabled - AI will maintain context for follow-up edits");
        }
        else
        {
            Debug.Log("[AI] Edit mode disabled - Starting fresh conversation");
            aiGen.StartNewConversation();
        }
    }
}