using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;



[CreateAssetMenu(
    fileName = "PrehensionConfig",
    menuName = "Prehension/Create Config")]
internal class PrehensionConfig : ScriptableObject
{
    // TODO most of these should likely be private, but able to be set in editor script (which I think is actually just marking it as private)
    internal enum Handedness { Right, Left }

    [System.Serializable]
    internal class Gesture
    {
        public string name;
        public string uuid;
        public bool active;
        public bool rapidReFire;
        public bool isHoldGesture;
        public int buildFramesOverride; // 0 = use global value
        public Handedness handedness;
        public string mirrorFromGestureUuid;
        public string oppositeGestureUuid; // blocks fast re-trigger after this gesture fires
        public List<Sample> samples;
    }

    [System.Serializable]
    internal class Sample
    {
        public string uuid;
        public bool active;
        public bool hasData;
        public bool dirtyToDB; // whether or not this has been rerecorded since last upload to db
    }

    [SerializeField] internal List<Gesture> gestures = new List<Gesture>();
    [SerializeField] internal string nullRightGestureUuid;
    [SerializeField] internal string nullLeftGestureUuid;

    [SerializeField] internal string currentRecordingGestureName;
    [SerializeField] internal string currentRecordingGestureUuid;
    [SerializeField] internal int currentRecordingSampleIndex;
    [SerializeField] internal string currentRecordingSampleUuid;
}
