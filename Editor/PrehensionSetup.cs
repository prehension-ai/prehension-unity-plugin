using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

internal static class PrehensionSetup
{
    private static string ConfigPath => PrehensionPaths.ProjectDataRoot + "/PrehensionConfig.asset";
    private static string ScenePath => PrehensionPaths.ProjectDataRoot + "/DataRecording.unity";

    [MenuItem("Prehension/Setup Project", false, 1)]
    public static void SetupProject()
    {
        bool madeChanges = false;

        // Create folder structure
        if (!AssetDatabase.IsValidFolder(PrehensionPaths.ProjectDataRoot))
        {
            AssetDatabase.CreateFolder("Assets", "PrehensionData");
            madeChanges = true;
        }

        if (!AssetDatabase.IsValidFolder(PrehensionPaths.DataRoot))
        {
            AssetDatabase.CreateFolder(PrehensionPaths.ProjectDataRoot, "Data");
            madeChanges = true;
        }

        // Create PrehensionConfig asset
        PrehensionConfig config = AssetDatabase.LoadAssetAtPath<PrehensionConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<PrehensionConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            madeChanges = true;
        }

        // Create template project details JSON
        if (!File.Exists(PrehensionPaths.ProjectDetailsPath))
        {
            PrehensionAPIClient.ProjectDetails details = new PrehensionAPIClient.ProjectDetails
            {
                api_key = "",
                project_name = "",
                project_id = ""
            };
            File.WriteAllText(PrehensionPaths.ProjectDetailsPath, JsonUtility.ToJson(details, true));
            AssetDatabase.ImportAsset(PrehensionPaths.ProjectDetailsPath);
            madeChanges = true;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Ping the config in the Project window
        EditorGUIUtility.PingObject(config);
        Selection.activeObject = config;

        // Warn if TMP Essential Resources haven't been imported
        if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            Debug.LogWarning("[Prehension] TextMesh Pro Essential Resources not found. Please import them via Window > TextMeshPro > Import TMP Essential Resources, otherwise text in the DataRecording scene will not render correctly.");


        if (madeChanges)
            Debug.Log("[Prehension] Project setup complete. Fill in your project details at " + PrehensionPaths.ProjectDetailsPath);
        else
            Debug.Log("[Prehension] Project already set up.");
    }

    [MenuItem("Prehension/Highlight Config", false, 3)]
    public static void HighlightConfig()
    {
        PrehensionConfig config = AssetDatabase.LoadAssetAtPath<PrehensionConfig>(ConfigPath);
        if (config != null)
        {
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }
        else
        {
            Debug.LogWarning("[Prehension] Could not find PrehensionConfig — run Prehension > Setup Project first.");
        }
    }

    [MenuItem("Prehension/Record Data", false, 2)]
    public static void OpenDataRecordingScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // Scenes in read-only packages can't be opened directly — copy to Assets/ first
        if (!File.Exists(ScenePath))
        {
            if (!AssetDatabase.IsValidFolder(PrehensionPaths.ProjectDataRoot))
            {
                Debug.LogWarning("[Prehension] Could not find PrehensionData folder — run Prehension > Setup Project first.");
                return;
            }
            AssetDatabase.CopyAsset(PrehensionPaths.PluginRoot + "/Scenes/DataRecording.unity", ScenePath);
            AssetDatabase.Refresh();
        }

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath);

        PrehensionConfig config = AssetDatabase.LoadAssetAtPath<PrehensionConfig>(ConfigPath);
        if (config != null)
        {
            Selection.activeObject = config;

            PrehensionDataRecorder recorder = UnityEngine.Object.FindFirstObjectByType<PrehensionDataRecorder>(FindObjectsInactive.Exclude);
            if (recorder != null && recorder.config == null)
            {
                recorder.config = config;
                EditorSceneManager.SaveScene(scene);
            }
        }
        else
        {
            Debug.LogWarning("[Prehension] Could not find PrehensionConfig — run Prehension > Setup Project first.");
        }
    }
}
