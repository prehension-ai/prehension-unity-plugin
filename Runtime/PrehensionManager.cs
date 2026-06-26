using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Hands;

public class PrehensionManager : MonoBehaviour
{
    [Header("Prehension Config")]
    [Tooltip("Central Prehension config containing gesture and sample information")]
    [SerializeField] internal PrehensionConfig config;

    [Header("Body transforms")]
    [Tooltip("The camera transform, e.g. Main Camera")]
    public Transform headTransform;

    [Header("Gesture recognition algorithm")]
    [Tooltip("Successive frames required to detect a gesture")]
    public int buildFrames = 8;
    [Tooltip("Space between successive gestures")]
    public int cooldownFrames = 10;
    
    [Header("Debug logging")]
    [Tooltip("Log to the console whenever a gesture is fully recognized")]
    public bool logRecognizedGestures = false;
    [Tooltip("Log to the console whenever a gesture becomes a candidate (build frames started but not yet confirmed)")]
    public bool logCandidateGestures = false;

    [Header("Model output logging over UDP")]
    public bool logSoftmaxOverUDP = false;
    public string udpIP = "127.0.0.1";
    public int udpPort = 5005;


    // TODO neither of these are up for changing right now but maybe in the future?
    private const int numJointPositions = 66;
    private const int windowFrameLength = 15;
    private const int udpNamesIntervalFrames = 300;
    private static int numGestureClasses;
    
    private static PrehensionHandDataTransformer handDataTransformer;
    private static PrehensionHandDataAggregator rightHandDataAggregator;
    private static PrehensionHandDataAggregator leftHandDataAggregator;
    private static IModelLoader libraryLoader;
    private static PrehensionGestureRecognitionAlgorithm rightGestureRecognitionAlgo;
    private static PrehensionGestureRecognitionAlgorithm leftGestureRecognitionAlgo;
    private bool _analyzeRightHand = true;
    private float[] _rightSoftmaxCache;
    private float[] _leftSoftmaxCache;
    private bool[] _rightHandGestureMask;
    private List<PrehensionConfig.Gesture> activeGestures;

    private XRHandSubsystem handSubsystem;

    private UdpClient udpClient;
    private IPEndPoint udpEndPoint;
    private uint udpFrameCount = 0;

    public delegate void OnGestureRecognized(string gestureName);
    private static event OnGestureRecognized gestureRecognized;

    public delegate void OnGestureCandidate(string gestureName);
    private static event OnGestureCandidate gestureCandidate;

    public delegate void OnGestureReleased(string gestureName);
    private static event OnGestureReleased gestureReleased;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        activeGestures = config.gestures.FindAll(g => g.active);
        numGestureClasses = activeGestures.Count;

        _rightHandGestureMask = new bool[numGestureClasses];
        for (int i = 0; i < numGestureClasses; i++)
            _rightHandGestureMask[i] = activeGestures[i].handedness == PrehensionConfig.Handedness.Right;

        _rightSoftmaxCache = new float[numGestureClasses];
        _leftSoftmaxCache  = new float[numGestureClasses];

        handDataTransformer = new PrehensionHandDataTransformer(numJointPositions, headTransform);

        rightHandDataAggregator = new PrehensionHandDataAggregator(windowFrameLength);
        leftHandDataAggregator = new PrehensionHandDataAggregator(windowFrameLength);

        List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(handSubsystems);
        if (handSubsystems.Count > 0)
            handSubsystem = handSubsystems[0];
        else
            Debug.LogWarning("[Prehension] No XRHandSubsystem found. Is XR Hands configured in your project?");

        libraryLoader = PrehensionModelLoaderFactory.Create(gameObject);
        libraryLoader.InitializeModelParams(numJointPositions, windowFrameLength, numGestureClasses);
        libraryLoader.LoadModel();

        int nullRightGestureIndex = activeGestures.FindIndex(g => g.uuid == config.nullRightGestureUuid);
        if (nullRightGestureIndex < 0)
        {
            Debug.LogWarning("[Prehension] null right gesture UUID not found in config — defaulting to 0");
            nullRightGestureIndex = 0;
        }
        int nullLeftGestureIndex = activeGestures.FindIndex(g => g.uuid == config.nullLeftGestureUuid);
        if (nullLeftGestureIndex < 0)
        {
            Debug.LogWarning("[Prehension] null left gesture UUID not found in config — defaulting to 0");
            nullLeftGestureIndex = 0;
        }
        int[] oppositeOf = new int[numGestureClasses];
        for (int i = 0; i < numGestureClasses; i++) oppositeOf[i] = -1;
        for (int i = 0; i < activeGestures.Count; i++)
        {
            string oppUuid = activeGestures[i].oppositeGestureUuid;
            if (string.IsNullOrEmpty(oppUuid)) continue;
            int j = activeGestures.FindIndex(g => g.uuid == oppUuid);
            if (j >= 0) { oppositeOf[i] = j; oppositeOf[j] = i; }
        }
        bool[] rapidReFire = new bool[numGestureClasses];
        bool[] isHoldGesture = new bool[numGestureClasses];
        int[] buildFramesPerGesture = new int[numGestureClasses];
        int globalBuildFrames = Mathf.Max(1, buildFrames / 2);
        for (int i = 0; i < activeGestures.Count; i++)
        {
            rapidReFire[i] = activeGestures[i].rapidReFire;
            isHoldGesture[i] = activeGestures[i].isHoldGesture;
            buildFramesPerGesture[i] = activeGestures[i].buildFramesOverride > 0
                ? Mathf.Max(1, activeGestures[i].buildFramesOverride / 2)
                : globalBuildFrames;
        }
        rightGestureRecognitionAlgo = new PrehensionGestureRecognitionAlgorithm(nullRightGestureIndex, buildFramesPerGesture, Mathf.Max(1, cooldownFrames / 2), oppositeOf, rapidReFire, isHoldGesture);
        leftGestureRecognitionAlgo  = new PrehensionGestureRecognitionAlgorithm(nullLeftGestureIndex,  buildFramesPerGesture, Mathf.Max(1, cooldownFrames / 2), oppositeOf, rapidReFire, isHoldGesture);

        System.Action<int> onCandidate = (index) =>
        {
            if (index >= 0 && index < activeGestures.Count)
            {
                string name = activeGestures[index].name;
                if (logCandidateGestures) Debug.Log($"[Prehension] Candidate: {name}");
                gestureCandidate?.Invoke(name);
            }
        };
        rightGestureRecognitionAlgo.CandidateStarted += onCandidate;
        leftGestureRecognitionAlgo.CandidateStarted += onCandidate;

        System.Action<int> onReleased = (index) =>
        {
            if (index >= 0 && index < activeGestures.Count)
            {
                string name = activeGestures[index].name;
                if (logRecognizedGestures) Debug.Log($"[Prehension] Released: {name}");
                gestureReleased?.Invoke(name);
            }
        };
        rightGestureRecognitionAlgo.GestureReleased += onReleased;
        leftGestureRecognitionAlgo.GestureReleased += onReleased;

        udpClient = new UdpClient();
        udpEndPoint = new IPEndPoint(IPAddress.Parse(udpIP), udpPort);

        if (logSoftmaxOverUDP)
        {
            SendGestureNamesPacket();
        }
    }

    // Update is called once per frame
    void Update()
    {
        bool rightTracked = handSubsystem != null && handSubsystem.running && handSubsystem.rightHand.isTracked;
        bool leftTracked  = handSubsystem != null && handSubsystem.running && handSubsystem.leftHand.isTracked;

        rightHandDataAggregator.AddDataToDataWindow(rightTracked
            ? handDataTransformer.TransformXRHandData(handSubsystem.rightHand)
            : new float[numJointPositions]);

        leftHandDataAggregator.AddDataToDataWindow(leftTracked
            ? handDataTransformer.TransformXRHandData(handSubsystem.leftHand)
            : new float[numJointPositions]);

        bool analyzedRight = _analyzeRightHand;
        PrehensionHandDataAggregator activeAggregator = analyzedRight ? rightHandDataAggregator : leftHandDataAggregator;
        PrehensionGestureRecognitionAlgorithm activeAlgo = analyzedRight ? rightGestureRecognitionAlgo : leftGestureRecognitionAlgo;
        _analyzeRightHand = !_analyzeRightHand;

        if (activeAggregator.data.Count < windowFrameLength)
            return;

        float[] modelSoftmaxOutput;
        libraryLoader.RunModelInference(activeAggregator.data, out modelSoftmaxOutput);

        if (analyzedRight)
            Buffer.BlockCopy(modelSoftmaxOutput, 0, _rightSoftmaxCache, 0, numGestureClasses * sizeof(float));
        else
            Buffer.BlockCopy(modelSoftmaxOutput, 0, _leftSoftmaxCache,  0, numGestureClasses * sizeof(float));

        // zero out gesture classes that belong to the other hand before recognition
        float[] maskedOutput = new float[numGestureClasses];
        for (int i = 0; i < numGestureClasses; i++)
            maskedOutput[i] = _rightHandGestureMask[i] == analyzedRight ? modelSoftmaxOutput[i] : 0f;

        if (logSoftmaxOverUDP && _rightSoftmaxCache != null && _leftSoftmaxCache != null)
        {
            // merge: right-hand class values from right cache, left-hand class values from left cache
            float[] mergedOutput = new float[numGestureClasses];
            for (int i = 0; i < numGestureClasses; i++)
                mergedOutput[i] = _rightHandGestureMask[i] ? _rightSoftmaxCache[i] : _leftSoftmaxCache[i];

            byte[] byteArray = new byte[4 + mergedOutput.Length * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(udpFrameCount), 0, byteArray, 0, 4);
            Buffer.BlockCopy(mergedOutput, 0, byteArray, 4, mergedOutput.Length * 4);
            try { udpClient.Send(byteArray, byteArray.Length, udpEndPoint); }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Prehension] UDP send failed: {e.Message}");
#endif
            }
            udpFrameCount++;

            if (udpFrameCount % udpNamesIntervalFrames == 0)
                SendGestureNamesPacket();
        }

        int finalRecognizedGesture = activeAlgo.Update(maskedOutput);

        if (finalRecognizedGesture >= 0)
        {
            string name = activeGestures[finalRecognizedGesture].name;
            if (logRecognizedGestures) Debug.Log($"[Prehension] Recognized: {name}");
            gestureRecognized?.Invoke(name);
        }
    }
    [System.Serializable]
    private class GestureNamesPacket
    {
        public string[] names;
        public string[] hands;
    }

    private void SendGestureNamesPacket()
    {
        string[] names = new string[activeGestures.Count];
        string[] hands = new string[activeGestures.Count];
        for (int i = 0; i < activeGestures.Count; i++)
        {
            names[i] = activeGestures[i].name;
            hands[i] = activeGestures[i].handedness == PrehensionConfig.Handedness.Left ? "left" : "right";
        }
        string json = JsonUtility.ToJson(new GestureNamesPacket { names = names, hands = hands });
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] packet = new byte[4 + jsonBytes.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(uint.MaxValue), 0, packet, 0, 4);
        Buffer.BlockCopy(jsonBytes, 0, packet, 4, jsonBytes.Length);
        try { udpClient.Send(packet, packet.Length, udpEndPoint); }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Prehension] UDP send failed: {e.Message}");
#endif
        }
    }

    /// <summary>
    /// Subscribes a callback to be invoked whenever a gesture is recognized.
    /// The callback receives the name of the recognized gesture as defined in your <see cref="PrehensionConfig"/>.
    /// Multiple callbacks can be subscribed and will all fire on each recognition event.
    /// </summary>
    /// <param name="func">A method matching the signature <c>void MyHandler(string gestureName)</c>.</param>
    public void SubscribeToGestureRecognized(OnGestureRecognized func)
    {
        gestureRecognized += func;
    }

    /// <summary>
    /// Subscribes a callback to be invoked whenever a new candidate gesture is registered for recognition.
    /// The callback receives the name of the candidate gesture as defined in your <see cref="PrehensionConfig"/>.
    /// Multiple callbacks can be subscribed and will all fire on each recognition event.
    /// </summary>
    /// <param name="func">A method matching the signature <c>void MyHandler(string gestureName)</c>.</param>
    public void SubscribeToGestureCandidate(OnGestureCandidate func)
    {
        gestureCandidate += func;
    }

    public void SubscribeToGestureReleased(OnGestureReleased func)
    {
        gestureReleased += func;
    }
}
