using System.IO;
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
        EditorGUILayout.PropertyField(useHRTFProp);
        
        EditorGUILayout.Space(20);
        
        DrawAISection();
        
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
        
        EditorGUILayout.LabelField("Describe the audio experience you want:", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        aiPrompt = EditorGUILayout.TextArea(aiPrompt, promptStyle, GUILayout.Height(60));
        if (EditorGUI.EndChangeCheck())
        {
            showGeneratedCode = false;
        }
        
        EditorGUILayout.Space(5);
        
        EditorGUI.BeginDisabledGroup(isGenerating || string.IsNullOrWhiteSpace(aiPrompt));
        
        GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
        if (GUILayout.Button(isGenerating ? "Generating..." : "Generate Satie Code", GUILayout.Height(30)))
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
        }
        
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
            generatedCode = await generator.GenerateSatieCode(aiPrompt);
            showGeneratedCode = true;
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
}