using System.IO;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal class PrehensionBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        var projectDetails = JsonUtility.FromJson<PrehensionAPIClient.ProjectDetails>(
            File.ReadAllText(PrehensionPaths.ProjectDetailsPath));

        var credRequest = new PrehensionAPIClient.CredentialRequest
        {
            project_id = projectDetails.project_id,
            bundle_id = PlayerSettings.applicationIdentifier,
            device_id = UnityEngine.SystemInfo.deviceUniqueIdentifier,
            model_version = PlayerSettings.bundleVersion
        };

        string requestJson = JsonUtility.ToJson(credRequest);
        string url = $"{PrehensionAPIClient.BaseUrl}/api/v1/credentials/release/renew/";

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {projectDetails.api_key}");
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = client.PostAsync(url, content).Result;
            }
            catch (System.Exception e)
            {
                throw new BuildFailedException($"Prehension: Release credential request failed: {e.Message}");
            }

            if (!response.IsSuccessStatusCode)
                throw new BuildFailedException($"Prehension: Release credential request returned {(int)response.StatusCode} {response.ReasonPhrase}");

            string responseBody = response.Content.ReadAsStringAsync().Result;
            var responseObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(
                responseBody,
                new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });

            string credentialJson = responseObj["credential"].ToString(Newtonsoft.Json.Formatting.None);

            const string savePath = "Assets/StreamingAssets/credential_release.json";
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            File.WriteAllText(savePath, credentialJson);
            Debug.Log("[Prehension] Release credential written to StreamingAssets.");
        }
    }
}
