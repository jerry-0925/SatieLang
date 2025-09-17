using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Satie;

[CustomEditor(typeof(SatieAudioGen))]
public class SatieAudioGenEditor : Editor
{
    private SatieAudioGen audioGen;

    // UI State
    private string audioPrompt = "";
    private bool isGeneratingAudio = false;
    private AudioGenerationResult currentAudioResult;
    private int selectedAudioIndex = -1;
    private AudioSource previewAudioSource;

    // Foldouts
    private bool showServerSettings = false;
    private bool showProviderSettings = true;
    private bool showGeneratedFiles = false;

    void OnEnable()
    {
        audioGen = (SatieAudioGen)target;
    }

    void OnDisable()
    {
        // Clean up preview audio source
        if (previewAudioSource != null)
        {
            DestroyImmediate(previewAudioSource.gameObject);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Audio Generation", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Server configuration
        DrawServerConfiguration();

        EditorGUILayout.Space(5);

        // Provider settings
        DrawProviderSettings();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space(5);

        // Generation interface
        DrawGenerationInterface();

        // Show current generation results
        if (currentAudioResult != null)
        {
            DrawGenerationResults();
        }

        EditorGUILayout.Space(10);

        // Show generated files
        DrawGeneratedFiles();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawServerConfiguration()
    {
        showServerSettings = EditorGUILayout.Foldout(showServerSettings, "Server Configuration", true);

        if (showServerSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("apiUrl"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleRate"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Server Health", GUILayout.Height(20)))
            {
                CheckServerHealth();
            }

            if (GUILayout.Button("Clear Cache", GUILayout.Height(20)))
            {
                audioGen.ClearCache();
                Debug.Log("Audio generation cache cleared");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }
    }

    private void DrawProviderSettings()
    {
        showProviderSettings = EditorGUILayout.Foldout(showProviderSettings, "Provider Settings", true);

        if (showProviderSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultProvider"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("numOptions"));

            EditorGUILayout.Space(5);

            AudioProvider currentProvider = (AudioProvider)serializedObject.FindProperty("defaultProvider").enumValueIndex;

            if (currentProvider == AudioProvider.ElevenLabs)
            {
                EditorGUILayout.LabelField("Eleven Labs Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elevenLabsDuration"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elevenLabsPromptInfluence"));
            }
            else if (currentProvider == AudioProvider.AudioLDM2)
            {
                EditorGUILayout.LabelField("AudioLDM2 Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("audioldm2InferenceSteps"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("audioldm2Duration"));
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawGenerationInterface()
    {
        EditorGUILayout.LabelField("Generate Audio", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Prompt:");
        audioPrompt = EditorGUILayout.TextArea(audioPrompt, GUILayout.Height(40));

        EditorGUI.BeginDisabledGroup(isGeneratingAudio || string.IsNullOrWhiteSpace(audioPrompt));

        Color bgColor = GUI.backgroundColor;
        GUI.backgroundColor = isGeneratingAudio ? Color.yellow : new Color(0.5f, 0.8f, 1f);

        string buttonText = isGeneratingAudio ? "Generating Audio..." : "Generate Audio Options";
        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            GenerateAudioOptions();
        }

        GUI.backgroundColor = bgColor;
        EditorGUI.EndDisabledGroup();
    }

    private void DrawGenerationResults()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField($"Generated Audio Options ({currentAudioResult.provider})", EditorStyles.boldLabel);

        if (currentAudioResult.audioData != null)
        {
            for (int i = 0; i < currentAudioResult.audioData.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();

                bool hasData = currentAudioResult.audioData[i] != null && currentAudioResult.audioData[i].Length > 0;
                EditorGUI.BeginDisabledGroup(!hasData);

                string optionLabel = $"Option {i + 1}";
                if (hasData)
                {
                    optionLabel += $" ({currentAudioResult.audioData[i].Length / 1024} KB)";
                }
                else
                {
                    optionLabel += " (Generating...)";
                }

                EditorGUILayout.LabelField(optionLabel, GUILayout.Width(150));

                if (GUILayout.Button("▶ Preview", GUILayout.Width(80)))
                {
                    PlayAudioPreview(i);
                }

                bool isSelected = selectedAudioIndex == i;
                GUI.backgroundColor = isSelected ? Color.green : GUI.backgroundColor;

                if (GUILayout.Button(isSelected ? "✓ Selected" : "Select", GUILayout.Width(80)))
                {
                    selectedAudioIndex = i;
                }

                GUI.backgroundColor = GUI.backgroundColor;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            if (previewAudioSource != null && previewAudioSource.isPlaying)
            {
                if (GUILayout.Button("■ Stop Preview", GUILayout.Height(25)))
                {
                    StopAudioPreview();
                }
            }

            if (selectedAudioIndex >= 0)
            {
                EditorGUILayout.Space(5);

                Color bgColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.cyan;

                if (GUILayout.Button("Save Selected Audio", GUILayout.Height(30)))
                {
                    SaveSelectedAudio();
                }

                GUI.backgroundColor = bgColor;
            }
        }
    }

    private void DrawGeneratedFiles()
    {
        showGeneratedFiles = EditorGUILayout.Foldout(showGeneratedFiles, "Previously Generated Audio", true);

        if (showGeneratedFiles)
        {
            var files = audioGen.GetGeneratedAudioFiles();

            if (files.Count > 0)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField($"Found {files.Count} generated audio files:", EditorStyles.miniLabel);

                int maxDisplay = Mathf.Min(10, files.Count);
                for (int i = 0; i < maxDisplay; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(files[i], EditorStyles.miniLabel);

                    if (GUILayout.Button("Load", GUILayout.Width(50), GUILayout.Height(16)))
                    {
                        string fullPath = Path.Combine("Audio", "generation", files[i]);
                        AudioClip clip = Resources.Load<AudioClip>(fullPath);
                        if (clip != null)
                        {
                            Debug.Log($"Loaded audio clip: {files[i]}");
                            PlayLoadedClip(clip);
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (files.Count > maxDisplay)
                {
                    EditorGUILayout.LabelField($"... and {files.Count - maxDisplay} more", EditorStyles.miniLabel);
                }

                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("No generated audio files found.", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }
    }

    private async void GenerateAudioOptions()
    {
        isGeneratingAudio = true;
        currentAudioResult = null;
        selectedAudioIndex = -1;
        Repaint();

        try
        {
            int numOptions = serializedObject.FindProperty("numOptions").intValue;
            currentAudioResult = await audioGen.GenerateAudioOptions(
                audioPrompt,
                numOptions,
                null,
                OnAudioOptionGenerated
            );

            if (currentAudioResult != null)
            {
                Debug.Log($"Successfully generated {currentAudioResult.audioData.Length} audio options");
            }
            else
            {
                EditorUtility.DisplayDialog("Generation Failed",
                    "Failed to generate audio. Make sure the audio server is running.", "OK");
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
        currentAudioResult = result;
        EditorApplication.delayCall += () => Repaint();
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
            if (previewAudioSource == null)
            {
                GameObject tempGO = new GameObject("AudioPreview");
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                previewAudioSource = tempGO.AddComponent<AudioSource>();
            }

            var audioClip = audioGen.ConvertBytesToAudioClip(
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

    private void PlayLoadedClip(AudioClip clip)
    {
        if (clip == null) return;

        try
        {
            if (previewAudioSource == null)
            {
                GameObject tempGO = new GameObject("AudioPreview");
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                previewAudioSource = tempGO.AddComponent<AudioSource>();
            }

            previewAudioSource.clip = clip;
            previewAudioSource.Play();
            Debug.Log($"Playing loaded audio clip");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error playing loaded clip: {e.Message}");
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
        if (currentAudioResult == null || selectedAudioIndex < 0) return;

        try
        {
            string savedPath = await audioGen.SaveSelectedAudio(
                currentAudioResult,
                selectedAudioIndex
            );

            if (!string.IsNullOrEmpty(savedPath))
            {
                EditorUtility.DisplayDialog("Audio Saved",
                    $"Audio saved to: {savedPath}", "OK");

                // Apply to SatieRuntime if exists
                var runtime = FindObjectOfType<SatieRuntime>();
                if (runtime != null)
                {
                    Debug.Log($"Audio saved and can be used in Satie scripts: {savedPath}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving audio: {e.Message}");
            EditorUtility.DisplayDialog("Save Error", $"Error: {e.Message}", "OK");
        }
    }

    private async void CheckServerHealth()
    {
        try
        {
            bool isHealthy = await audioGen.CheckServerHealth();

            if (isHealthy)
            {
                EditorUtility.DisplayDialog("Server Health",
                    "Audio generation server is running and healthy!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Server Health",
                    "Could not connect to audio generation server.\n" +
                    "Please ensure the server is running:\n" +
                    "python audio_generation_server.py", "OK");
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Health Check Error",
                $"Error: {e.Message}", "OK");
        }
    }
}