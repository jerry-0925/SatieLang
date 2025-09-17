using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Satie;

[CustomEditor(typeof(SatieRuntime))]
public class SatieRuntimeEditor : Editor
{
    private SerializedProperty scriptFileProp;
    private SerializedProperty useHRTFProp;
    
    private string aiPrompt = "";
    private bool isGenerating = false;
    private string generatedCode = "";
    private bool showGeneratedCode = false;
    private bool showRLHFFeedback = false;
    private string lastPrompt = "";
    private string lastGeneratedCode = "";
    
    // Follow-up editing
    private bool editMode = false;
    private bool showConversationHistory = false;
    private Vector2 conversationScrollPos;
    
    // Advanced settings
    private bool showAdvancedSettings = false;

    // Audio generation
    private bool showAudioGeneration = false;
    private string audioPrompt = "";
    private bool isGeneratingAudio = false;
    private AudioGenerationResult currentAudioResult;
    private AudioSource previewAudioSource;
    private int selectedAudioIndex = -1;

    private GUIStyle promptStyle;
    private GUIStyle generatedCodeStyle;
    private GUIStyle headerStyle;

    void OnEnable()
    {
        scriptFileProp = serializedObject.FindProperty("scriptFile");
        useHRTFProp = serializedObject.FindProperty("useHRTF");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        InitStyles();
        
        EditorGUILayout.PropertyField(scriptFileProp);
        
        EditorGUILayout.Space(20);
        
        DrawAISection();

        EditorGUILayout.Space(10);

        DrawAudioGenerationSection();

        EditorGUILayout.Space(10);

        DrawAdvancedSettings();
        
        EditorGUILayout.Space(10);
        
        DrawRuntimeControls();
        
        serializedObject.ApplyModifiedProperties();
    }

    private void InitStyles()
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(EditorStyles.textArea);
            promptStyle.wordWrap = true;
            promptStyle.fontSize = 12;
        }
        
        if (generatedCodeStyle == null)
        {
            generatedCodeStyle = new GUIStyle(EditorStyles.textArea);
            generatedCodeStyle.wordWrap = false;
            generatedCodeStyle.fontSize = 11;
            generatedCodeStyle.fontStyle = FontStyle.Normal;
        }
        
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
        }
    }

    private void DrawAISection()
    {
        var bgColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.2f, 0.3f, 0.5f, 0.2f);
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = bgColor;
        
        EditorGUILayout.LabelField("AI Code Generation", headerStyle);
        EditorGUILayout.Space(5);
        
        // Mode selector
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Mode:", GUILayout.Width(40));
        
        bool newEditMode = GUILayout.Toggle(!editMode, "New Script", EditorStyles.radioButton);
        bool newContinueMode = GUILayout.Toggle(editMode, "Continue Editing", EditorStyles.radioButton);
        
        if (newEditMode && editMode)
        {
            SetEditMode(false);
        }
        else if (newContinueMode && !editMode)
        {
            SetEditMode(true);
        }
        
        EditorGUILayout.EndHorizontal();
        
        if (editMode)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Follow-up prompt (what would you like to change?):", EditorStyles.boldLabel);
        }
        else
        {
            EditorGUILayout.LabelField("Describe the audio experience you want:", EditorStyles.boldLabel);
        }
        
        EditorGUI.BeginChangeCheck();
        aiPrompt = EditorGUILayout.TextArea(aiPrompt, promptStyle, GUILayout.Height(60));
        if (EditorGUI.EndChangeCheck())
        {
            showGeneratedCode = false;
        }
        
        // Show conversation history in edit mode
        if (editMode && SatieAICodeGen.Instance.IsInEditMode())
        {
            EditorGUILayout.Space(5);
            showConversationHistory = EditorGUILayout.Foldout(showConversationHistory, "Conversation History");
            
            if (showConversationHistory)
            {
                DrawConversationHistory();
            }
        }
        
        EditorGUILayout.Space(5);
        
        EditorGUI.BeginDisabledGroup(isGenerating || string.IsNullOrWhiteSpace(aiPrompt));
        
        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
        string buttonText = isGenerating ? "Generating..." : 
                           editMode ? "Apply Changes" : "Generate Satie Code";
        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            GenerateCode();
        }
        GUI.backgroundColor = bgColor;
        
        EditorGUI.EndDisabledGroup();
        
        if (showGeneratedCode && !string.IsNullOrEmpty(generatedCode))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Generated Code:", EditorStyles.boldLabel);
            
            float codeHeight = Mathf.Min(300, generatedCode.Split('\n').Length * 15 + 20);
            var scrollPos = EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.Height(codeHeight));
            generatedCode = EditorGUILayout.TextArea(generatedCode, generatedCodeStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("Apply to Current Script", GUILayout.Height(25)))
            {
                ApplyGeneratedCode();
            }
            
            GUI.backgroundColor = new Color(0.7f, 0.7f, 0.3f);
            if (GUILayout.Button("Save as New Script", GUILayout.Height(25)))
            {
                SaveAsNewScript();
            }
            
            GUI.backgroundColor = bgColor;
            
            EditorGUILayout.EndHorizontal();
            
            // RLHF Feedback Section
            if (SatieAICodeGen.Instance.config.enableRLHF)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Code Quality Feedback (RLHF):", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Was this generated code helpful and correct?", EditorStyles.miniLabel);
                
                EditorGUILayout.BeginHorizontal();
                
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("✓ Correct", GUILayout.Height(25)))
                {
                    RecordRLHFFeedback(true);
                }
                
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("✗ Incorrect", GUILayout.Height(25)))
                {
                    RecordRLHFFeedback(false);
                }
                
                GUI.backgroundColor = bgColor;
                
                EditorGUILayout.EndHorizontal();
            }
        }
        
        
        EditorGUILayout.EndVertical();
    }

    private void DrawAudioGenerationSection()
    {
        var bgColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.3f, 0.2f, 0.5f, 0.2f);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = bgColor;

        showAudioGeneration = EditorGUILayout.Foldout(showAudioGeneration, "AI Audio Generation (AudioLDM2)", true);

        if (showAudioGeneration)
        {
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Generate audio from text description:", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            audioPrompt = EditorGUILayout.TextArea(audioPrompt, promptStyle, GUILayout.Height(40));
            if (EditorGUI.EndChangeCheck())
            {
                // Clear previous results when prompt changes
                currentAudioResult = null;
                selectedAudioIndex = -1;
            }

            EditorGUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(isGeneratingAudio || string.IsNullOrWhiteSpace(audioPrompt));

            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.7f);
            string audioButtonText = isGeneratingAudio ? "Generating Audio..." : "Generate 3 Audio Options";
            if (GUILayout.Button(audioButtonText, GUILayout.Height(30)))
            {
                GenerateAudioOptions();
            }
            GUI.backgroundColor = bgColor;

            EditorGUI.EndDisabledGroup();

            // Display generated audio options
            if (currentAudioResult != null && currentAudioResult.audioData != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Generated Audio Options:", EditorStyles.boldLabel);

                for (int i = 0; i < currentAudioResult.audioData.Length; i++)
                {
                    if (currentAudioResult.audioData[i] != null && currentAudioResult.audioData[i].Length > 0)
                    {
                        EditorGUILayout.BeginHorizontal();

                        // Radio button for selection
                        bool isSelected = EditorGUILayout.Toggle(selectedAudioIndex == i, GUILayout.Width(20));
                        if (isSelected && selectedAudioIndex != i)
                        {
                            selectedAudioIndex = i;
                        }

                        EditorGUILayout.LabelField($"Option {i + 1}", GUILayout.Width(60));

                        // Play button
                        if (GUILayout.Button("▶ Play", GUILayout.Width(60)))
                        {
                            PlayAudioPreview(i);
                        }

                        // Stop button
                        if (GUILayout.Button("■ Stop", GUILayout.Width(60)))
                        {
                            StopAudioPreview();
                        }

                        // File size info
                        float sizeKB = currentAudioResult.audioData[i].Length / 1024f;
                        EditorGUILayout.LabelField($"{sizeKB:F1} KB", GUILayout.Width(60));

                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space(5);

                EditorGUI.BeginDisabledGroup(selectedAudioIndex < 0);

                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                if (GUILayout.Button($"Save Selected Audio (Option {selectedAudioIndex + 1}) to Resources", GUILayout.Height(25)))
                {
                    SaveSelectedAudio();
                }
                GUI.backgroundColor = bgColor;

                EditorGUI.EndDisabledGroup();

                // Show saved files
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Show Generated Audio Files", GUILayout.Height(20)))
                {
                    ShowGeneratedAudioFiles();
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    private async void GenerateAudioOptions()
    {
        isGeneratingAudio = true;
        currentAudioResult = null;
        selectedAudioIndex = -1;
        Repaint();

        try
        {
            var generator = SatieAudioGen.Instance;
            currentAudioResult = await generator.GenerateAudioOptions(audioPrompt, 2, OnAudioOptionGenerated);

            if (currentAudioResult != null)
            {
                Debug.Log($"Successfully generated {currentAudioResult.audioData.Length} audio options");
            }
            else
            {
                EditorUtility.DisplayDialog("Generation Failed",
                    "Failed to generate audio. Make sure the AudioLDM2 server is running.", "OK");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Audio generation error: {e.Message}");
            EditorUtility.DisplayDialog("Generation Error", $"Error: {e.Message}", "OK");
        }
        finally
        {
            isGeneratingAudio = false;
            Repaint();
        }
    }

    private void OnAudioOptionGenerated(AudioGenerationResult result, int optionIndex)
    {
        // Update the current result reference
        currentAudioResult = result;

        // Force UI repaint to show the newly available option
        if (EditorApplication.isPlaying || !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += () => Repaint();
        }

        Debug.Log($"Audio option {optionIndex + 1} is now available for preview");
    }

    private void PlayAudioPreview(int index)
    {
        if (currentAudioResult == null || index < 0 || index >= currentAudioResult.audioData.Length)
            return;

        if (currentAudioResult.audioData[index] == null || currentAudioResult.audioData[index].Length == 0)
        {
            Debug.LogError($"No audio data for option {index + 1}");
            return;
        }

        try
        {
            // Create audio source if needed
            if (previewAudioSource == null)
            {
                GameObject tempGO = new GameObject("AudioPreview");
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                previewAudioSource = tempGO.AddComponent<AudioSource>();
            }

            // Convert bytes to AudioClip
            var audioClip = SatieAudioGen.Instance.ConvertBytesToAudioClip(
                currentAudioResult.audioData[index],
                $"Preview_{index}"
            );

            if (audioClip != null)
            {
                previewAudioSource.clip = audioClip;
                previewAudioSource.Play();
                Debug.Log($"Playing audio option {index + 1}");
            }
            else
            {
                Debug.LogError("Failed to convert audio data to AudioClip");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error playing audio preview: {e.Message}");
        }
    }

    private void StopAudioPreview()
    {
        if (previewAudioSource != null && previewAudioSource.isPlaying)
        {
            previewAudioSource.Stop();
            Debug.Log("Stopped audio preview");
        }
    }

    private async void SaveSelectedAudio()
    {
        if (currentAudioResult == null || selectedAudioIndex < 0)
            return;

        try
        {
            string savedPath = await SatieAudioGen.Instance.SaveSelectedAudio(
                currentAudioResult,
                selectedAudioIndex
            );

            if (!string.IsNullOrEmpty(savedPath))
            {
                EditorUtility.DisplayDialog("Audio Saved",
                    $"Audio saved to:\n{savedPath}\n\nYou can now reference it in Satie code as:\n\"generation/{Path.GetFileName(savedPath)}\"",
                    "OK");

                Debug.Log($"Audio saved to: {savedPath}");
            }
            else
            {
                EditorUtility.DisplayDialog("Save Failed", "Failed to save audio file.", "OK");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving audio: {e.Message}");
            EditorUtility.DisplayDialog("Save Error", $"Error: {e.Message}", "OK");
        }
    }

    private void ShowGeneratedAudioFiles()
    {
        var files = SatieAudioGen.Instance.GetGeneratedAudioFiles();

        if (files.Count > 0)
        {
            string fileList = string.Join("\n", files.Take(10));
            if (files.Count > 10)
            {
                fileList += $"\n... and {files.Count - 10} more";
            }

            EditorUtility.DisplayDialog("Generated Audio Files",
                $"Found {files.Count} generated audio files:\n\n{fileList}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("No Files Found",
                "No generated audio files found in Assets/Resources/Audio/generation/", "OK");
        }
    }

    private void DrawAdvancedSettings()
    {
        var bgColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.5f, 0.3f, 0.2f, 0.2f);
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = bgColor;
        
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
        
        if (showAdvancedSettings)
        {
            EditorGUILayout.Space(5);
            
            // HRTF Setting
            EditorGUILayout.PropertyField(useHRTFProp);
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Audio & API Controls", EditorStyles.boldLabel);
            
            // Resource cache controls
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Audio Cache", GUILayout.Height(20)))
            {
                SatieAICodeGen.Instance.InvalidateResourceCache();
            }
            EditorGUILayout.EndHorizontal();
            
            // API controls
            if (!File.Exists(Path.Combine(Application.dataPath, "api_key.txt")))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("API key not found! Create Assets/api_key.txt with your OpenAI API key.", MessageType.Warning);
                
                if (GUILayout.Button("Create API Key File"))
                {
                    CreateApiKeyFile();
                }
            }
            else
            {
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Test API Connection", GUILayout.Height(20)))
                {
                    TestAPIConnection();
                }
            }
        }
        
        EditorGUILayout.EndVertical();
    }

    private void DrawRuntimeControls()
    {
        if (!Application.isPlaying)
            return;
            
        EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Soft Reset (R)"))
        {
            var runtime = target as SatieRuntime;
            runtime.SendMessage("Sync", false);
        }
        
        if (GUILayout.Button("Hard Reset (Shift+R)"))
        {
            var runtime = target as SatieRuntime;
            runtime.SendMessage("Sync", true);
        }
        
        EditorGUILayout.EndHorizontal();
    }

    private async void GenerateCode()
    {
        isGenerating = true;
        showGeneratedCode = false;
        Repaint();
        
        try
        {
            var generator = SatieAICodeGen.Instance;
            
            // Get current script content if in edit mode
            string currentScript = "";
            if (editMode)
            {
                var scriptFile = scriptFileProp.objectReferenceValue as TextAsset;
                if (scriptFile != null)
                {
                    currentScript = scriptFile.text;
                }
            }
            
            // Use follow-up generation or regular generation
            generatedCode = await generator.GenerateWithFollowUp(aiPrompt, currentScript);
            
            lastPrompt = aiPrompt;
            lastGeneratedCode = generatedCode;
            showGeneratedCode = true;
            showRLHFFeedback = true;
            
            // Clear prompt after successful generation in edit mode
            if (editMode)
            {
                aiPrompt = "";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to generate code: {e.Message}");
            generatedCode = $"# Error generating code: {e.Message}";
            showGeneratedCode = true;
        }
        finally
        {
            isGenerating = false;
            Repaint();
        }
    }

    private void ApplyGeneratedCode()
    {
        var scriptFile = scriptFileProp.objectReferenceValue as TextAsset;
        
        if (scriptFile == null)
        {
            EditorUtility.DisplayDialog("No Script", "Please assign a script file first.", "OK");
            return;
        }
        
        string path = AssetDatabase.GetAssetPath(scriptFile);
        
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Invalid Script", "Could not find script file path.", "OK");
            return;
        }
        
        string fullPath = Path.Combine(Application.dataPath, path.Replace("Assets/", ""));
        
        File.WriteAllText(fullPath, generatedCode);
        AssetDatabase.Refresh();
        
        Debug.Log($"Updated script: {path}");
        
        if (Application.isPlaying)
        {
            var runtime = target as SatieRuntime;
            runtime.SendMessage("Sync", true);
        }
    }

    private void SaveAsNewScript()
    {
        string path = EditorUtility.SaveFilePanel(
            "Save Satie Script",
            "Assets/Examples",
            "generated.sat",
            "sat"
        );
        
        if (string.IsNullOrEmpty(path))
            return;
            
        File.WriteAllText(path, generatedCode);
        
        if (path.StartsWith(Application.dataPath))
        {
            AssetDatabase.Refresh();
            
            string assetPath = "Assets" + path.Substring(Application.dataPath.Length);
            var newScript = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            
            if (newScript != null)
            {
                scriptFileProp.objectReferenceValue = newScript;
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"Created and assigned new script: {assetPath}");
            }
        }
    }

    private void CreateApiKeyFile()
    {
        string path = Path.Combine(Application.dataPath, "api_key.txt");
        File.WriteAllText(path, "sk-YOUR-API-KEY-HERE");
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog(
            "API Key File Created",
            "Created Assets/api_key.txt\n\nPlease edit this file and replace 'sk-YOUR-API-KEY-HERE' with your actual OpenAI API key.",
            "OK"
        );
        
        EditorUtility.RevealInFinder(path);
    }
    
    private async void TestAPIConnection()
    {
        Debug.Log("[AI Test] Starting API connection test...");
        
        try
        {
            var generator = SatieAICodeGen.Instance;
            string result = await generator.TestAPIConnection();
            Debug.Log($"[AI Test] Test result: {result}");
            
            EditorUtility.DisplayDialog("API Test Result", result, "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AI Test] Test failed with exception: {e}");
            EditorUtility.DisplayDialog("API Test Failed", $"Exception: {e.Message}", "OK");
        }
    }
    
    private void RecordRLHFFeedback(bool wasCorrect)
    {
        if (string.IsNullOrEmpty(lastPrompt) || string.IsNullOrEmpty(lastGeneratedCode))
        {
            Debug.LogWarning("No recent generation to provide feedback on.");
            return;
        }
        
        try
        {
            var generator = SatieAICodeGen.Instance;
            generator.RecordRLHFFeedback(lastPrompt, lastGeneratedCode, wasCorrect);
            
            string feedbackMessage = wasCorrect ? 
                "Thank you! This feedback helps improve future code generation." : 
                "Thank you for the feedback. This will help improve future generations.";
                
            EditorUtility.DisplayDialog("Feedback Recorded", feedbackMessage, "OK");
            showRLHFFeedback = false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to record RLHF feedback: {e.Message}");
            EditorUtility.DisplayDialog("Feedback Error", $"Failed to record feedback: {e.Message}", "OK");
        }
    }
    
    private void SetEditMode(bool enabled)
    {
        editMode = enabled;
        var generator = SatieAICodeGen.Instance;
        
        if (enabled)
        {
            // Get current script content for edit mode
            string currentScript = "";
            var scriptFile = scriptFileProp.objectReferenceValue as TextAsset;
            if (scriptFile != null)
            {
                currentScript = scriptFile.text;
            }
            generator.SetEditMode(true, currentScript);
        }
        else
        {
            generator.SetEditMode(false);
            showConversationHistory = false;
        }
        
        // Clear prompt when switching modes
        aiPrompt = "";
        showGeneratedCode = false;
    }
    
    private void DrawConversationHistory()
    {
        var conversation = SatieAICodeGen.Instance.GetCurrentConversation();
        if (conversation?.messages == null || conversation.messages.Length == 0)
        {
            EditorGUILayout.LabelField("No conversation history yet.", EditorStyles.miniLabel);
            return;
        }
        
        var bgColor = GUI.backgroundColor;
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        conversationScrollPos = EditorGUILayout.BeginScrollView(conversationScrollPos, GUILayout.Height(150));
        
        for (int i = 0; i < conversation.messages.Length; i++)
        {
            var msg = conversation.messages[i];
            
            // Style based on role
            if (msg.role == "user")
            {
                GUI.backgroundColor = new Color(0.7f, 0.8f, 1f, 0.3f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"You ({msg.timestamp}):", EditorStyles.boldLabel);
            }
            else
            {
                GUI.backgroundColor = new Color(0.8f, 1f, 0.7f, 0.3f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"AI ({msg.timestamp}):", EditorStyles.boldLabel);
            }
            
            GUI.backgroundColor = bgColor;
            
            // Show abbreviated content
            string content = msg.content;
            if (content.Length > 200)
            {
                content = content.Substring(0, 200) + "...";
            }
            
            EditorGUILayout.LabelField(content, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }
        
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        
        GUI.backgroundColor = bgColor;
    }
}