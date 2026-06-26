using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
using UnityEditor;
#endif

using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System;


[ExecuteInEditMode]
internal class PrehensionAPIClient : MonoBehaviour
{
    // TODO rename this to something more befitting of a general api interface class 

    public PrehensionConfig config;

    [System.Serializable]
    internal class JobIDResponse
    {
        public string job_id;
    }

    [System.Serializable]
    internal class JobProgressResponse
    {
        public float progress;
        public string download_url;
        public string error;
    }

    [System.Serializable]
    internal class GestureSyncResponse
    {
        public List<GestureAndSampleEntry> gestures;
    }

    [System.Serializable]
    internal class GestureAndSampleEntry
    {
        public string id;
        public string handedness;
        public string mirror_from_gesture_id;
        public List<string> samples;
    }

    [System.Serializable]
    internal class GestureSampleSerializable
    {
        public string gesture_id;
        public string gesture_name;
        public string sample_id;
        public List<float[]> frames;
    }

    [System.Serializable]
    internal class ProjectDetails
    {
        public string api_key;
        public string project_name;
        public string project_id;
    }

    [System.Serializable]
    internal class CredentialRequest
    {
        public string project_id;
        public string bundle_id;
        public string device_id;
        public string model_version;
    }


    // this is for uploading config
    [System.Serializable]
    internal class PrehensionConfigAPIFormat
    {
        public string project_id;
        public string null_right_gesture_id;
        public string null_left_gesture_id;
        public GestureAndSampleEntry[] gestures;
    }

    // this is for uploading gestures themselves
    [System.Serializable]
    internal class PrehensionAPIGestureUpload
    {
        public string project_id;
        public GestureSampleSerializable[] gestures;
    }

    internal const string BaseUrl = "https://prehensionai.com";
    private const string baseUrl = BaseUrl;
    internal const string PendingJobSessionKey = "Prehension.PendingJobId";
    internal const string PendingDownloadUrlSessionKey = "Prehension.PendingDownloadUrl";

    private static ProjectDetails _projectDetails;
    internal static ProjectDetails LoadedProjectDetails => _projectDetails;

    void Awake()
    {
        _projectDetails = JsonUtility.FromJson<ProjectDetails>(File.ReadAllText(PrehensionPaths.ProjectDetailsPath));
    }


// Yes - this entire class is intended to be used in editor, and nowhere else
#if UNITY_EDITOR
    public void PrehensionAPI_SyncGesturesToDatabase()
    {
        List<string> gesturesMissingData = new List<string>();
        foreach (PrehensionConfig.Gesture gesture in config.gestures)
        {
            if (!gesture.active) continue;
            foreach (PrehensionConfig.Sample sample in gesture.samples)
            {
                if (sample.active && !sample.hasData)
                {
                    gesturesMissingData.Add(gesture.name);
                    break;
                }
            }
        }

        if (gesturesMissingData.Count > 0)
        {
            Debug.LogWarning(
                "[Prehension] The following active gestures have samples with no recorded data: " +
                string.Join(", ", gesturesMissingData) +
                ". Model generation will fail until all active samples have data."
            );
        }

        string configJSON = SerializeConfigToJson();
        EditorCoroutineUtility.StartCoroutine(GetGesturesMissingFromDatabase(configJSON), this);
    }

    public void PrehensionAPI_GenerateModelFromDatabase()
    {
        string configJSON = SerializeConfigToJson();
        EditorCoroutineUtility.StartCoroutine(GenerateModel(configJSON), this);
    }

    public string SerializeConfigToJson()
    {
        PrehensionConfigAPIFormat requestAPIConfig = new PrehensionConfigAPIFormat();
        requestAPIConfig.project_id = _projectDetails.project_id;
        requestAPIConfig.null_right_gesture_id = config.nullRightGestureUuid;
        requestAPIConfig.null_left_gesture_id  = config.nullLeftGestureUuid;
        List<GestureAndSampleEntry> gesturesList = new List<GestureAndSampleEntry>();
        foreach (PrehensionConfig.Gesture gesture in config.gestures)
        {
            if (gesture.active)
            {
                GestureAndSampleEntry entry = new GestureAndSampleEntry();
                entry.id = gesture.uuid;
                entry.handedness = gesture.handedness == PrehensionConfig.Handedness.Left ? "left" : "right";
                entry.mirror_from_gesture_id = gesture.mirrorFromGestureUuid;
                List<string> samplesList = new List<string>();
                foreach(PrehensionConfig.Sample sample in gesture.samples)
                {
                    if(sample.active)
                    {
                        samplesList.Add(sample.uuid);
                    }
                }
                entry.samples = samplesList;
                gesturesList.Add(entry);
            }
        }
        requestAPIConfig.gestures = gesturesList.ToArray();
        
        return JsonUtility.ToJson(requestAPIConfig);
    }

    private IEnumerator GetGesturesMissingFromDatabase(string jsonRequestString)
    {
        string gestureSyncUri = $"{baseUrl}/api/v1/compare-gestures/";
        using(UnityWebRequest request = new UnityWebRequest(gestureSyncUri, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonRequestString));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Prehension] Gesture sync failed: {request.error}");
                yield break;
            }

            GestureSyncResponse response = JsonUtility.FromJson<GestureSyncResponse>(request.downloadHandler.text);
            yield return UploadGestureSamples(response);
        }
    }

    private const int uploadBatchSize = 10;

    private IEnumerator UploadGestureSamples(GestureSyncResponse missingSamples)
    {
        GestureSyncResponse gesturesToUpload = missingSamples;

        // add in samples marked as dirty
        foreach(PrehensionConfig.Gesture configGesture in config.gestures)
        {
            foreach(PrehensionConfig.Sample configSample in configGesture.samples)
            {
                if(configSample.dirtyToDB)
                {
                    GestureAndSampleEntry entry = gesturesToUpload.gestures.Find(gesture => gesture.id == configGesture.uuid);
                    if(entry == null)
                    {
                        GestureAndSampleEntry newEntry = new GestureAndSampleEntry();
                        newEntry.id = configGesture.uuid;
                        newEntry.samples = new List<string> { configSample.uuid };
                        gesturesToUpload.gestures.Add(newEntry);
                    }
                    else
                    {
                        if(!entry.samples.Contains(configSample.uuid))
                            entry.samples.Add(configSample.uuid);
                    }
                }
            }
        }

        // build the full list of samples to upload
        List<GestureSampleSerializable> gestureSamplesList = new List<GestureSampleSerializable>();
        foreach(GestureAndSampleEntry entry in gesturesToUpload.gestures)
        {
            foreach(string sampleId in entry.samples)
            {
                string assetPath = $"{PrehensionPaths.DataRoot}/{entry.id}/{sampleId}.asset";
                PrehensionGestureSample sampleSO = AssetDatabase.LoadAssetAtPath<PrehensionGestureSample>(assetPath);
                if(sampleSO != null)
                {
                    GestureSampleSerializable sample = new GestureSampleSerializable();
                    sample.sample_id = sampleSO.sample_id;
                    sample.gesture_id = sampleSO.gesture_id;
                    sample.gesture_name = config.gestures.Find(g => g.uuid == sampleSO.gesture_id).name;
                    sample.frames = sampleSO.GetRawFrames();
                    gestureSamplesList.Add(sample);
                }
                else
                {
                    Debug.LogWarning($"[Prehension] Could not find sample asset at {assetPath}, skipping.");
                }
            }
        }

        if(gestureSamplesList.Count == 0)
        {
            Debug.Log("[Prehension] No samples to upload.");
            yield break;
        }

        // upload in batches to avoid hitting payload size limits
        string gestureUploadUri = $"{baseUrl}/api/v1/upload-gestures/";
        int totalBatches = (int)Math.Ceiling((double)gestureSamplesList.Count / uploadBatchSize);
        Debug.Log($"[Prehension] Uploading {gestureSamplesList.Count} samples in {totalBatches} batch(es).");

        for(int batchStart = 0; batchStart < gestureSamplesList.Count; batchStart += uploadBatchSize)
        {
            int batchIndex = batchStart / uploadBatchSize + 1;
            List<GestureSampleSerializable> batch = gestureSamplesList.GetRange(
                batchStart, Math.Min(uploadBatchSize, gestureSamplesList.Count - batchStart));

            PrehensionAPIGestureUpload batchUploadData = new PrehensionAPIGestureUpload();
            batchUploadData.project_id = _projectDetails.project_id;
            batchUploadData.gestures = batch.ToArray();

            string jsonRequestString = Newtonsoft.Json.JsonConvert.SerializeObject(batchUploadData);

            bool batchSucceeded = false;
            using(UnityWebRequest request = new UnityWebRequest(gestureUploadUri, "PUT"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonRequestString));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");

                yield return request.SendWebRequest();

                if(request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Prehension] Gesture upload failed on batch {batchIndex}/{totalBatches}: {request.error}");
                }
                else
                {
                    Debug.Log($"[Prehension] Gesture upload batch {batchIndex}/{totalBatches} succeeded ({batch.Count} samples).");
                    batchSucceeded = true;
                }
            }

            if(!batchSucceeded) yield break;

            // mark this batch's samples clean immediately so a partial failure doesn't re-upload them
            HashSet<string> batchSampleIds = new HashSet<string>(batch.Select(s => s.sample_id));
            foreach(PrehensionConfig.Gesture gesture in config.gestures)
                foreach(PrehensionConfig.Sample sample in gesture.samples)
                    if(batchSampleIds.Contains(sample.uuid))
                        sample.dirtyToDB = false;
            EditorUtility.SetDirty(config);
        }

        Debug.Log("[Prehension] All gesture uploads complete.");
    }

    private IEnumerator GenerateModel(string jsonRequestString)
    {
        SessionState.SetString(PendingJobSessionKey, "pending");
        int progressID = Progress.Start("Generating model");

        Debug.Log("[Prehension] Kicking off model generation on server");
        string genModelUri = $"{baseUrl}/api/v1/generate-model/";
        string jobID = "";

        using(UnityWebRequest request = new UnityWebRequest(genModelUri, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonRequestString));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Prehension] Config upload failed: {request.error}");
                Progress.Finish(progressID, Progress.Status.Failed);
                SessionState.EraseString(PendingJobSessionKey);
                yield break;
            }

            Debug.Log("[Prehension] Config uploaded successfully");
            JobIDResponse response = JsonUtility.FromJson<JobIDResponse>(request.downloadHandler.text);
            jobID = response.job_id;
        }

        SessionState.SetString(PendingJobSessionKey, jobID);
        yield return EditorCoroutineUtility.StartCoroutine(PollForCompletionAndDownload(jobID, progressID), this);
    }

    public static IEnumerator PollForCompletionAndDownload(string jobID, int progressID)
    {
        if (_projectDetails == null)
            _projectDetails = JsonUtility.FromJson<ProjectDetails>(File.ReadAllText(PrehensionPaths.ProjectDetailsPath));

        string pollProgressUri = $"{baseUrl}/api/v1/get-download-url/{jobID}/";
        string downloadUrl = "";
        bool isComplete = false;

        while(!isComplete)
        {
            using(UnityWebRequest request = new UnityWebRequest(pollProgressUri, "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Prehension] Request for model generation progress failed: {request.error}");
                    Progress.Finish(progressID, Progress.Status.Failed);
                    SessionState.EraseString(PendingJobSessionKey);
                    yield break;
                }

                JobProgressResponse response = JsonUtility.FromJson<JobProgressResponse>(request.downloadHandler.text);
                if(response.download_url != null)
                {
                    downloadUrl = response.download_url;
                    isComplete = true;
                }
                else if(response.error != null)
                {
                    Debug.Log($"[Prehension] Error while generating model: {response.error}");
                    Progress.Finish(progressID, Progress.Status.Failed);
                    SessionState.EraseString(PendingJobSessionKey);
                    yield break;
                }
                else
                {
                    Progress.Report(progressID, response.progress, $"Job completion: {response.progress * 100}%");
                }
            }
            if(!isComplete)
                yield return new EditorWaitForSeconds(5f);
        }

        SessionState.EraseString(PendingJobSessionKey);
        SessionState.SetString(PendingDownloadUrlSessionKey, downloadUrl);
        Debug.Log("[Prehension] Downloading generated models from server");
        yield return DownloadModel(downloadUrl, progressID);
    }

    public static IEnumerator DownloadModel(string url, int progressID)
    {
        if (_projectDetails == null)
            _projectDetails = JsonUtility.FromJson<ProjectDetails>(File.ReadAllText(PrehensionPaths.ProjectDetailsPath));

        using (UnityWebRequest request = new UnityWebRequest(url, "GET"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Prehension] Error downloading model from server: {request.error}");
                Progress.Finish(progressID, Progress.Status.Failed);
                SessionState.EraseString(PendingDownloadUrlSessionKey);
                yield break;
            }

            if (!ExtractModelZip(request.downloadHandler.data, progressID))
                yield break;

            Debug.Log("[Prehension] Models downloaded successfully!");
            Progress.Finish(progressID);
            SessionState.EraseString(PendingDownloadUrlSessionKey);
        }
    }

    private static bool ExtractModelZip(byte[] zipData, int progressID)
    {
        try
        {
            using (var zipStream = new MemoryStream(zipData))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (string filename in new[] { "model_dev.enc", "model_release.enc" })
                {
                    var entry = archive.GetEntry(filename);
                    if (entry == null)
                    {
                        Debug.LogError($"[Prehension] {filename} not found in downloaded zip.");
                        Progress.Finish(progressID, Progress.Status.Failed);
                        SessionState.EraseString(PendingDownloadUrlSessionKey);
                        return false;
                    }
                    using (var entryStream = entry.Open())
                    using (var fileStream = File.Create(Path.Combine("Assets/StreamingAssets", filename)))
                        entryStream.CopyTo(fileStream);
                }
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Prehension] Failed to extract model zip: {e.Message}");
            Progress.Finish(progressID, Progress.Status.Failed);
            SessionState.EraseString(PendingDownloadUrlSessionKey);
            return false;
        }
    }

    public static IEnumerator RenewCredential(Action<string> onSuccess, Action<string> onError = null)
    {
        if (_projectDetails == null)
            _projectDetails = JsonUtility.FromJson<ProjectDetails>(File.ReadAllText(PrehensionPaths.ProjectDetailsPath));

        CredentialRequest credentialRequest = new CredentialRequest
        {
            project_id = _projectDetails.project_id,
            bundle_id = Application.identifier,
            device_id = SystemInfo.deviceUniqueIdentifier,
            model_version = Application.version
        };

        string credUri = $"{baseUrl}/api/v1/credentials/dev/renew/";
        using (UnityWebRequest request = new UnityWebRequest(credUri, "POST"))
        {
            string json = JsonUtility.ToJson(credentialRequest);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_projectDetails.api_key}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Prehension] Credential renewal failed: {request.error}");
                onError?.Invoke(request.error);
                yield break;
            }

            var response = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(
                request.downloadHandler.text,
                new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
            string credentialJson = response["credential"].ToString(Newtonsoft.Json.Formatting.None);
            onSuccess(credentialJson);
        }
    }
#endif // UNITY_EDITOR
}
