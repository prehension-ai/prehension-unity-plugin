// using Unity.VisualScripting;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.XR.Hands;
using TMPro;
using UnityEngine.UI;
using System.Collections;

internal class PrehensionDataRecorder : MonoBehaviour
{
    [Header("Prehension Config")]
    public PrehensionConfig config;
    public PrehensionHandDataTransformer handDataTransformer;
    public Transform headTransform;
    public PrehensionSampleVisualizer sampleVisualizer;

    public GameObject handVisualization;
    public Image progressWheel;
    public TextMeshProUGUI statusText; // GestureBrowser also has a reference to this and I don't know if I love that but here we are

    public float countdownDuration = 3.0f;
    public float recordingDuration = 1.0f;
    
    private XRHandSubsystem handSubsystem;

    private bool recording = false;
    private PrehensionConfig.Handedness recordingHandedness;
    private XRHand RecordingHand => recordingHandedness == PrehensionConfig.Handedness.Left
        ? handSubsystem.leftHand
        : handSubsystem.rightHand;
    private List<float[]> data = new List<float[]>();
    private List<List<PrehensionGestureSampleBones.Bone>> ovrSkeletonData = new List<List<PrehensionGestureSampleBones.Bone>>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // TODO how to get this 66 in there properly? (and not hardcoded?)
        handDataTransformer = new PrehensionHandDataTransformer(66, headTransform);

        List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(handSubsystems);
        if (handSubsystems.Count > 0)
        {
            handSubsystem = handSubsystems[0];
        }
        else
        {
            Debug.LogWarning("[Prehension] No XRHandSubsystem found. Is XR Hands configured in your project?");
        }

        progressWheel.fillAmount = 0;
        statusText.text = "";
    }

    // Update is called once per frame
    void Update()
    {
        if (recording)
        {
            data.Add(handDataTransformer.TransformXRHandData(RecordingHand));

            // save skeleton data for playback in recording interface
            List<PrehensionGestureSampleBones.Bone> newFrame = new List<PrehensionGestureSampleBones.Bone>();
            for (XRHandJointID jointId = XRHandJointID.BeginMarker; jointId < XRHandJointID.EndMarker; jointId++)
            {
                XRHandJoint joint = RecordingHand.GetJoint(jointId);
                if (!joint.TryGetPose(out Pose pose))
                {
                    Debug.LogWarning("[Prehension] Could not get pose for hand joints during recording");
                    continue;
                }

                PrehensionGestureSampleBones.Bone newBone = new PrehensionGestureSampleBones.Bone();
                newBone.id = (int)jointId;
                newBone.position = pose.position;
                newBone.rotation = pose.rotation;
                newFrame.Add(newBone);
            }
            ovrSkeletonData.Add(newFrame);
        }
    }

    public void StartRecordingFlow()
    {
        StartCoroutine(RecordingRoutine());
    }

    private IEnumerator RecordingRoutine()
    {
        if (handVisualization != null) handVisualization.SetActive(false);

        float timer = countdownDuration;
        while(timer > 0f)
        {
            timer -= Time.deltaTime;
            progressWheel.fillAmount = timer / countdownDuration;
            statusText.text = Mathf.Ceil(timer).ToString();
            yield return null;
        }

        StartRecording();

        statusText.text = "REC";

        timer = 0f;
        while(timer < recordingDuration)
        {
            timer += Time.deltaTime;
            progressWheel.fillAmount = timer / recordingDuration;
            yield return null;
        }

        StopRecording();
        progressWheel.fillAmount = 0;
        statusText.text = "Saved";

        handVisualization.SetActive(true);

        yield return new WaitForSeconds(1f);
        statusText.text = "";

    }

    public void StartRecording()
    {
        recording = true;
        data = new List<float[]>();
        ovrSkeletonData = new List<List<PrehensionGestureSampleBones.Bone>>();

        recordingHandedness = PrehensionConfig.Handedness.Right;
        foreach (PrehensionConfig.Gesture gesture in config.gestures)
        {
            if (gesture.uuid == config.currentRecordingGestureUuid)
            {
                recordingHandedness = gesture.handedness;
                break;
            }
        }

        if (handSubsystem == null || !RecordingHand.isTracked)
        {
            Debug.LogWarning("[Prehension] Started recording while hand isn't tracked!");
        }
    }

    public void StopRecording()
    {
        recording = false;

        // write to file
        SaveDataToAsset();
        sampleVisualizer.SetSampleData(config.currentRecordingSampleUuid);
    }

    private void SaveDataToAsset()
    {
        // create scriptable object and get relevant uuids
        PrehensionGestureSample sampleSO = ScriptableObject.CreateInstance<PrehensionGestureSample>();
        string gestureUuid = config.currentRecordingGestureUuid;
        string sampleUuid = config.currentRecordingSampleUuid;

        // write data to scriptable object 
        sampleSO.gesture_id = gestureUuid;
        sampleSO.sample_id = sampleUuid;
        foreach (float[] array in data)
        {
            sampleSO.frames.Add(new PrehensionGestureSample.FloatArrayWrapper { values = array });
        }

        // create scriptable object of OVR data for playback in recording interface later
        PrehensionGestureSampleBones sampleBonesSO = ScriptableObject.CreateInstance<PrehensionGestureSampleBones>();
        sampleBonesSO.gesture_id = gestureUuid;
        sampleBonesSO.sample_id = sampleUuid;
        sampleBonesSO.WriteBoneFrames(ovrSkeletonData);


        // find sample in config data and mark it as having data
        // TODO feels like there should be a better way of doing this
        foreach(PrehensionConfig.Gesture gesture in config.gestures)
        {
            if(gesture.uuid == gestureUuid)
            {
                foreach(PrehensionConfig.Sample sample in gesture.samples)
                {
                    if(sample.uuid == sampleUuid)
                    {
                        sample.hasData = true;
                        sample.dirtyToDB = true;
#if UNITY_EDITOR
                        EditorUtility.SetDirty(config); // this is needed to make sure the hasData gets saved to disk
#endif
                    }
                }
            }
        }

#if UNITY_EDITOR
        // you can only use AssetDatabase while in editor, which is fine because that's exactly where we're going to be
        // TODO need to handle intention plugin being in a different spot 
        string directoryPath = $"{PrehensionPaths.DataRoot}/{gestureUuid}";
        
        // check that the folder exists
        if(!AssetDatabase.IsValidFolder(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }

        string filePath = $"{directoryPath}/{sampleUuid}.asset";
        AssetDatabase.CreateAsset(sampleSO, filePath);

        string sampleBonesSOPath = $"{directoryPath}/{sampleUuid}-bones.asset";
        AssetDatabase.CreateAsset(sampleBonesSO, sampleBonesSOPath);
        
        
        AssetDatabase.SaveAssets();

        Debug.Log($"[Prehension] Saved data to {filePath}");
#endif

    }
}
