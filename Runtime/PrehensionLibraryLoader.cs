using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

// this needs to be a monobehavior because it needs to inherit StartCoroutine for loading the model
internal class PrehensionLibraryLoader : MonoBehaviour, IModelLoader
{
#if UNITY_EDITOR
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32")]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool InitModel_Delegate(byte[] blob, IntPtr blob_size, string credential_json, string api_key);
    private InitModel_Delegate InitModel;

    // note that IntPtr here is basically size_t
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RunInference_Delegate(float[] input_data, IntPtr num_dims, IntPtr[] input_size, float[] output_data, IntPtr output_size);
    private RunInference_Delegate RunInference;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeModel_Delegate();
    private FreeModel_Delegate FreeModel;

    private IntPtr dll = IntPtr.Zero;

#else
    [DllImport("PrehensionLib", EntryPoint = "init_model")]
    private static extern bool InitModel(byte[] blob, IntPtr blob_size, string credential_json, string api_key);

    // note that IntPtr here is basically size_t
    [DllImport("PrehensionLib", EntryPoint = "run_inference")]
    private static extern int RunInference(float[] input_data, IntPtr num_dims, IntPtr[] input_size, float[] output_data, IntPtr output_size);

    [DllImport("PrehensionLib", EntryPoint = "free_model")]
    private static extern void FreeModel();
#endif

    private bool modelLoaded = false;

    private int numJointPositions;
    private int windowFrameLength;
    private int numGestureClasses;
    private float[] input_data;
    private float[] output_data;

    // Start is called before the first frame update
    void Start()
    {
#if UNITY_EDITOR  // Load dll functions in special load library things
        dll = LoadLibrary(System.IO.Path.GetFullPath(PrehensionPaths.LibPath)); // TODO not sure exactly why relative pathing works this way, but just make sure its sorted eventually

        if (dll == IntPtr.Zero)
        {
            Debug.LogError($"[Prehension] Failed to load DLL at {PrehensionPaths.LibPath}");
            return;
        }

        IntPtr InitModelPtr = GetProcAddress(dll, "init_model");
        InitModel = Marshal.GetDelegateForFunctionPointer<InitModel_Delegate>(InitModelPtr);

        IntPtr RunInferencePtr = GetProcAddress(dll, "run_inference");
        RunInference = Marshal.GetDelegateForFunctionPointer<RunInference_Delegate>(RunInferencePtr);

        IntPtr FreeModelPtr = GetProcAddress(dll, "free_model");
        FreeModel = Marshal.GetDelegateForFunctionPointer<FreeModel_Delegate>(FreeModelPtr);
#endif

    }
    
    public void LoadModel()
    {
        StartCoroutine(LoadModelCoroutine());
    }

    private const string credentialFilename = "credential_dev.json";
    private const string credentialSavePath = "Assets/StreamingAssets/credential_dev.json";

    private IEnumerator LoadModelCoroutine()
    {
        byte[] modelBytes = null;
#if UNITY_EDITOR
        yield return ReadStreamingAssetsFile("model_dev.enc", data => modelBytes = data);
#else
        yield return ReadStreamingAssetsFile("model_release.enc", data => modelBytes = data);
#endif

        if (modelBytes == null)
        {
            Debug.LogError("[Prehension] Failed to read model file from StreamingAssets.");
            yield break;
        }
        string credentialJson = null;

#if UNITY_EDITOR
        yield return PrehensionAPIClient.RenewCredential(
            cred => credentialJson = cred,
            err => Debug.LogWarning($"[Prehension] Credential renewal failed, falling back to saved credential: {err}")
        );

        if (credentialJson != null)
        {
            File.WriteAllText(credentialSavePath, credentialJson);
        }
        else
        {
            byte[] credBytes = null;
            yield return ReadStreamingAssetsFile(credentialFilename, data => credBytes = data);
            if (credBytes != null)
                credentialJson = System.Text.Encoding.UTF8.GetString(credBytes);
        }
#else
        byte[] credBytes = null;
        yield return ReadStreamingAssetsFile("credential_release.json", data => credBytes = data);
        if (credBytes != null)
            credentialJson = System.Text.Encoding.UTF8.GetString(credBytes);
#endif

        if (credentialJson == null)
        {
            Debug.LogError("[Prehension] No credential available — cannot load model.");
            yield break;
        }
#if UNITY_EDITOR
        string modelKey = PrehensionAPIClient.LoadedProjectDetails.api_key;
#else
        var credentialObj = Newtonsoft.Json.Linq.JObject.Parse(credentialJson);
        var projectIdToken = credentialObj["payload"]?["project_id"];
        if (projectIdToken == null)
        {
            Debug.LogError("[Prehension] Could not find payload.project_id in credential_release.json.");
            yield break;
        }
        string modelKey = projectIdToken.ToString();
#endif
        try
        {
            bool success = InitModel(modelBytes, (IntPtr)modelBytes.Length, credentialJson, modelKey);
            if (success)
                modelLoaded = true;
            else
                Debug.LogError("[Prehension] init_model returned false.");
        }
        catch (DllNotFoundException e)
        {
            Debug.LogError($"[Prehension] Native library not found: {e.Message}");
        }
        catch (EntryPointNotFoundException e)
        {
            Debug.LogError($"[Prehension] Native function 'init_model' not found: {e.Message}");
        }
    }

    private IEnumerator ReadStreamingAssetsFile(string filename, Action<byte[]> onComplete)
    {
        string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, filename);

        if (filePath.StartsWith("jar") || filePath.StartsWith("http") || filePath.StartsWith("file:") || filePath.Contains("/file:/"))
        {
            UnityWebRequest request = UnityWebRequest.Get(filePath);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                onComplete(request.downloadHandler.data);
        }
        else
        {
            onComplete(System.IO.File.ReadAllBytes(filePath));
        }
    }

    
    public void InitializeModelParams(int numJointPositions, int windowFrameLength, int numGestureClasses)
    {
        this.numJointPositions = numJointPositions;
        this.windowFrameLength = windowFrameLength;
        this.numGestureClasses = numGestureClasses;
        input_data = new float[numJointPositions * windowFrameLength];
        output_data = new float[numGestureClasses];
    }

    void Update()
    {
    }

    public int RunModelInference(List<float[]> jointData, out float[] outputData)
    {
        unsafe
        {
            if (modelLoaded)
            {
                IntPtr[] inputDims = { (IntPtr)1, (IntPtr)numJointPositions, (IntPtr)windowFrameLength };
                for(int i = 0; i < numJointPositions; i++)
                {
                    for(int j = 0; j < windowFrameLength; j++)
                    {
                        input_data[i * windowFrameLength + j] = jointData[j][i];
                    }
                }

                int gesture = RunInference(input_data, (IntPtr)inputDims.Length, inputDims, output_data, (IntPtr)numGestureClasses);

                if (gesture == -2)
                {
                    Debug.LogError("[Prehension] Model output size mismatch — the loaded model is incompatible with the current config. Regenerate the model to fix this.");
                    outputData = output_data;
                    return gesture;
                }

                outputData = output_data;

                return gesture;
            }
        }

        outputData = new float[numGestureClasses]; 
        return -1;
    }

    void OnApplicationQuit()
    {
        // go = 0;
        FreeModel();
        Debug.Log("[Prehension] successfully freed model");

#if UNITY_EDITOR
        FreeLibrary(dll);
        Debug.Log("[Prehension] done freeing library");
#endif
    }
}
