using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Satie;

[CustomEditor(typeof(SatieRuntime))]
public class SatieRuntimeEditor : Editor
{
    private SatieRuntime runtime;
    private SerializedProperty scriptFileProp;

    // Component management
    private bool showComponentSetup = false;
    private bool hasAICodeGen = false;
    private bool hasAudioGen = false;
    private bool hasSpatialAudio = false;

    // Runtime controls
    private bool showRuntimeControls = true;
    private bool showScriptPreview = false;

    // UI styles
    private GUIStyle headerStyle;
    private GUIStyle previewStyle;

    void OnEnable()
    {
        runtime = (SatieRuntime)target;
        scriptFileProp = serializedObject.FindProperty("scriptFile");

        CheckComponents();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        InitStyles();

        DrawHeader();
        DrawComponentSetup();
        DrawScriptConfiguration();
        DrawRuntimeControls();

        if (runtime.ScriptFile != null)
        {
            DrawScriptPreview();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void InitStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
            };
        }

        if (previewStyle == null)
        {
            previewStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                fontSize = 11,
                fontStyle = FontStyle.Normal
            };
        }
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Satie Runtime", headerStyle);
        EditorGUILayout.LabelField("Executes .sp scripts with spatial audio support", EditorStyles.miniLabel);
        EditorGUILayout.Space(10);
    }

    private void DrawComponentSetup()
    {
        Color bgColor = GUI.backgroundColor;

        // Check if any components are missing
        bool anyMissing = !hasAICodeGen || !hasAudioGen || !hasSpatialAudio;

        if (anyMissing)
        {
            GUI.backgroundColor = new Color(1f, 0.8f, 0.2f, 0.3f); // Warning color
        }
        else
        {
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f, 0.3f); // Success color
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = bgColor;

        showComponentSetup = EditorGUILayout.Foldout(showComponentSetup,
            anyMissing ? "⚠ Component Setup (Missing Components)" : "✓ Component Setup (Complete)", true);

        if (showComponentSetup)
        {
            EditorGUI.indentLevel++;

            DrawComponentStatus("AI Code Generation", hasAICodeGen, typeof(SatieAICodeGen));
            DrawComponentStatus("Audio Generation", hasAudioGen, typeof(SatieAudioGen));
            DrawComponentStatus("Spatial Audio", hasSpatialAudio, typeof(SatieSpatialAudio));

            if (anyMissing)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "For full functionality, add the missing components above. " +
                    "Each component has its own editor interface for configuration.",
                    MessageType.Info);

                if (GUILayout.Button("Add All Missing Components", GUILayout.Height(25)))
                {
                    AddMissingComponents();
                }
            }
            else
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "All components are present! Use their respective inspector sections to configure AI, Audio generation, and Spatial Audio.",
                    MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
    }

    private void DrawComponentStatus(string componentName, bool hasComponent, System.Type componentType)
    {
        EditorGUILayout.BeginHorizontal();

        string statusIcon = hasComponent ? "✓" : "✗";
        Color statusColor = hasComponent ? Color.green : Color.red;

        Color originalColor = GUI.color;
        GUI.color = statusColor;
        EditorGUILayout.LabelField(statusIcon, GUILayout.Width(20));
        GUI.color = originalColor;

        EditorGUILayout.LabelField(componentName, GUILayout.ExpandWidth(true));

        if (!hasComponent)
        {
            if (GUILayout.Button("Add", GUILayout.Width(50)))
            {
                runtime.gameObject.AddComponent(componentType);
                CheckComponents();
            }
        }
        else
        {
            EditorGUILayout.LabelField("Present", EditorStyles.miniLabel, GUILayout.Width(50));
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawScriptConfiguration()
    {
        EditorGUILayout.LabelField("Script Configuration", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(scriptFileProp, new GUIContent("Script File (.sp)"));

        if (runtime.ScriptFile == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a .sp script file to execute. You can generate scripts using the AI Code Generation component.",
                MessageType.Info);
        }

        EditorGUILayout.Space(5);
    }


    private void DrawRuntimeControls()
    {
        showRuntimeControls = EditorGUILayout.Foldout(showRuntimeControls, "Runtime Controls", true);

        if (showRuntimeControls)
        {
            EditorGUI.indentLevel++;

            if (Application.isPlaying)
            {
                DrawPlayModeControls();
            }
            else
            {
                DrawEditModeControls();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(5);
    }

    private void DrawPlayModeControls()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("🔄 Soft Reload (R)", GUILayout.Height(25)))
        {
            runtime.Sync(fullReset: false);
        }

        if (GUILayout.Button("🔄 Hard Reset (Shift+R)", GUILayout.Height(25)))
        {
            runtime.Sync(fullReset: true);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Runtime Shortcuts:\n" +
            "• R - Soft reload (keeps existing audio)\n" +
            "• Shift+R - Hard reset (stops all audio)",
            MessageType.Info);
    }

    private void DrawEditModeControls()
    {
        EditorGUI.BeginDisabledGroup(runtime.ScriptFile == null);

        if (GUILayout.Button("▶ Preview in Play Mode", GUILayout.Height(25)))
        {
            EditorApplication.isPlaying = true;
        }

        EditorGUI.EndDisabledGroup();

        if (runtime.ScriptFile == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a script file to preview the audio experience.",
                MessageType.Info);
        }
    }

    private void DrawScriptPreview()
    {
        showScriptPreview = EditorGUILayout.Foldout(showScriptPreview, "Script Preview", true);

        if (showScriptPreview)
        {
            EditorGUI.indentLevel++;

            string scriptContent = runtime.ScriptFile.text;
            string[] lines = scriptContent.Split('\n');

            EditorGUILayout.LabelField($"Lines: {lines.Length}", EditorStyles.miniLabel);

            EditorGUILayout.BeginVertical("box");
            Vector2 scrollPos = Vector2.zero;
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(150));

            EditorGUILayout.TextArea(scriptContent, previewStyle);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUI.indentLevel--;
        }
    }

    private void CheckComponents()
    {
        if (runtime == null) return;

        hasAICodeGen = runtime.GetComponent<SatieAICodeGen>() != null;
        hasAudioGen = runtime.GetComponent<SatieAudioGen>() != null;
        hasSpatialAudio = runtime.GetComponent<SatieSpatialAudio>() != null;
    }

    private void AddMissingComponents()
    {
        if (!hasAICodeGen)
        {
            runtime.gameObject.AddComponent<SatieAICodeGen>();
            Debug.Log("[Satie] Added SatieAICodeGen component");
        }

        if (!hasAudioGen)
        {
            runtime.gameObject.AddComponent<SatieAudioGen>();
            Debug.Log("[Satie] Added SatieAudioGen component");
        }

        if (!hasSpatialAudio)
        {
            runtime.gameObject.AddComponent<SatieSpatialAudio>();
            Debug.Log("[Satie] Added SatieSpatialAudio component");
        }

        CheckComponents();
        EditorUtility.SetDirty(runtime);
    }

    void OnDisable()
    {
        // Clean up any preview resources
    }
}