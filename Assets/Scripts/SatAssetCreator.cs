// Assets/Editor/SatAssetCreator.cs
using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

static class SatAssetCreator
{
    private const string kDefaultContent =
        @"# Satie script
loop ""forest"":
    volume   = 0.8
    fade_in  = 1
";

    [MenuItem("Assets/Create/Satie Script (.sat)", false, 82)]
    private static void CreateSatMenu()
    {
        // Ask the Project window to start a “new asset” rename operation.
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
            0,
            ScriptableObject.CreateInstance<CreateSatAction>(),
            "NewSatieScript.sat",
            EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D,
            null);
    }
    

    private class CreateSatAction : EndNameEditAction
    {
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            // Write the file to disk
            File.WriteAllText(pathName, kDefaultContent);

            // Tell Unity a new file exists right there
            AssetDatabase.ImportAsset(pathName, ImportAssetOptions.ForceUpdate);

            // Load it so ProjectWindowUtil can select / ping it
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(pathName);
            ProjectWindowUtil.ShowCreatedAsset(asset);
        }
    }
}