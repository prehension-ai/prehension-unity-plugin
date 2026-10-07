using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
#if UNITY_EDITOR
using UnityEditor;
#endif

internal class PrehensionSampleVisualizer : MonoBehaviour
{
    public GameObject rightHandMeshPrefab;
    public GameObject leftHandMeshPrefab;

    [SerializeField] private Transform screenConstraintPlane;

    private PrehensionConfig config;
    private List<PrehensionGestureSampleBones.BoneArrayWrapper> sampleData;
    private int currentFrame = 0;
    private Vector3 recordingOrigin;
    private Dictionary<int, Transform> jointTransforms;
    private GameObject currentHandInstance;

    void Awake()
    {
        config = GetComponentInParent<PrehensionDataRecorder>().config;
    }

    void Update()
    {
        if (sampleData != null && jointTransforms != null)
        {
            PrehensionGestureSampleBones.BoneArrayWrapper frame = sampleData[currentFrame];
            Vector3 offset = transform.position - recordingOrigin;

            if (screenConstraintPlane != null)
            {
                float maxPenetration = 0f;
                foreach (PrehensionGestureSampleBones.Bone bone in frame.bones)
                {
                    float penetration = Vector3.Dot(
                        bone.position + offset - screenConstraintPlane.position,
                        screenConstraintPlane.forward
                    );
                    if (penetration > maxPenetration)
                        maxPenetration = penetration;
                }
                if (maxPenetration > 0f)
                    offset -= screenConstraintPlane.forward * maxPenetration;
            }

            foreach (PrehensionGestureSampleBones.Bone bone in frame.bones)
            {
                if (jointTransforms.TryGetValue(bone.id, out Transform jointTransform))
                {
                    jointTransform.position = bone.position + offset;
                    jointTransform.rotation = bone.rotation;
                }
            }

            currentFrame = (currentFrame + 1) % sampleData.Count;
        }
        else
        {
            currentFrame = 0;
        }
    }

    public void SetSampleData(string sampleUuid)
    {
        string gestureUuid = null;
        PrehensionConfig.Handedness handedness = PrehensionConfig.Handedness.Right;
        foreach (PrehensionConfig.Gesture gesture in config.gestures)
        {
            if (gesture.samples.Exists(x => x.uuid == sampleUuid))
            {
                gestureUuid = gesture.uuid;
                handedness = gesture.handedness;
            }
        }

        GameObject prefab = handedness == PrehensionConfig.Handedness.Left ? leftHandMeshPrefab : rightHandMeshPrefab;
        SetupHandInstance(prefab);

#if UNITY_EDITOR
        string assetPath = $"{PrehensionPaths.DataRoot}/{gestureUuid}/{sampleUuid}-bones.asset";
        PrehensionGestureSampleBones sampleSO = AssetDatabase.LoadAssetAtPath<PrehensionGestureSampleBones>(assetPath);

        if (sampleSO != null)
        {
            sampleData = sampleSO.frames;
            recordingOrigin = System.Array.Find(
                sampleData[0].bones,
                b => b.id == (int)XRHandJointID.Wrist
            ).position;
            currentFrame = 0;
        }
#endif
    }

    public void ClearSample()
    {
        sampleData = null;
        jointTransforms = null;
        currentFrame = 0;

        if (currentHandInstance != null)
        {
            Destroy(currentHandInstance);
            currentHandInstance = null;
        }
    }

    private void SetupHandInstance(GameObject prefab)
    {
        if (currentHandInstance != null)
            Destroy(currentHandInstance);

        jointTransforms = null;

        if (prefab == null)
        {
            Debug.LogWarning("[Prehension] handMeshPrefab is not assigned.");
            return;
        }

        currentHandInstance = Instantiate(prefab, transform);

        XRHandTrackingEvents trackingEvents = currentHandInstance.GetComponent<XRHandTrackingEvents>();
        if (trackingEvents != null)
            trackingEvents.enabled = false;

        XRHandMeshController meshController = currentHandInstance.GetComponent<XRHandMeshController>();
        if (meshController != null)
        {
            meshController.enabled = false;
            if (meshController.handMeshRenderer != null)
                meshController.handMeshRenderer.enabled = true;
        }

        jointTransforms = new Dictionary<int, Transform>();
        XRHandSkeletonDriver skeletonDriver = currentHandInstance.GetComponent<XRHandSkeletonDriver>();
        if (skeletonDriver != null)
        {
            foreach (JointToTransformReference reference in skeletonDriver.jointTransformReferences)
                jointTransforms[(int)reference.xrHandJointID] = reference.jointTransform;
            skeletonDriver.enabled = false;
        }
    }
}
