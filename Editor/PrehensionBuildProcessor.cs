using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

class PrehensionBuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && (scene.path.Contains("com.prehension.sdk") || scene.path.Contains("PrehensionData/DataRecording")))
            {
                throw new BuildFailedException(
                    "The Prehension DataRecording scene is included in your build. " +
                    "This scene is for editor use only — remove it from Build Settings before building."
                );
            }
        }
    }
}
