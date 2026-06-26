using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;

[InitializeOnLoad]
internal static class PrehensionJobResumeHandler
{
    static PrehensionJobResumeHandler()
    {
        string jobId = SessionState.GetString(PrehensionAPIClient.PendingJobSessionKey, "");
        string downloadUrl = SessionState.GetString(PrehensionAPIClient.PendingDownloadUrlSessionKey, "");

        if (!string.IsNullOrEmpty(jobId))
        {
            Debug.Log($"[Prehension] Resuming model generation polling after domain reload (job: {jobId})");
            int progressID = Progress.Start("Generating model (resumed)");
            EditorCoroutineUtility.StartCoroutineOwnerless(
                PrehensionAPIClient.PollForCompletionAndDownload(jobId, progressID));
        }
        else if (!string.IsNullOrEmpty(downloadUrl))
        {
            Debug.Log($"[Prehension] Resuming model download after domain reload");
            int progressID = Progress.Start("Downloading model (resumed)");
            EditorCoroutineUtility.StartCoroutineOwnerless(
                PrehensionAPIClient.DownloadModel(downloadUrl, progressID));
        }
    }
}
