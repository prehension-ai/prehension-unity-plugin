using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

internal class PrehensionHandDataTransformer
{
    private int numJointPositions;
    private Transform headTransform;

    public PrehensionHandDataTransformer(int numJointPositions, Transform headTransform)
    {
        this.numJointPositions = numJointPositions;
        this.headTransform = headTransform;
    }

    // Maps XRHandJointID to the model's expected input index.
    // Metacarpal joints for index/middle/ring/little fingers are excluded —
    // the model was trained without them.
    private static readonly Dictionary<XRHandJointID, int> JointIDMapping = new Dictionary<XRHandJointID, int>()
    {
        { XRHandJointID.Wrist,               0 },
        { XRHandJointID.Palm,                1 },
        { XRHandJointID.ThumbMetacarpal,     2 },
        { XRHandJointID.ThumbProximal,       3 },
        { XRHandJointID.ThumbDistal,         4 },
        { XRHandJointID.ThumbTip,            5 },
        { XRHandJointID.IndexProximal,       6 },
        { XRHandJointID.IndexIntermediate,   7 },
        { XRHandJointID.IndexDistal,         8 },
        { XRHandJointID.IndexTip,            9 },
        { XRHandJointID.MiddleProximal,     10 },
        { XRHandJointID.MiddleIntermediate, 11 },
        { XRHandJointID.MiddleDistal,       12 },
        { XRHandJointID.MiddleTip,          13 },
        { XRHandJointID.RingProximal,       14 },
        { XRHandJointID.RingIntermediate,   15 },
        { XRHandJointID.RingDistal,         16 },
        { XRHandJointID.RingTip,            17 },
        { XRHandJointID.LittleProximal,     18 },
        { XRHandJointID.LittleIntermediate, 19 },
        { XRHandJointID.LittleDistal,       20 },
        { XRHandJointID.LittleTip,          21 },
    };

    public float[] TransformXRHandData(XRHand hand)
    {
        float[] frameJointData = new float[numJointPositions];

        foreach (var kvp in JointIDMapping)
        {
            XRHandJoint joint = hand.GetJoint(kvp.Key);
            if (!joint.TryGetPose(out Pose pose)) continue;

            Vector3 offset = pose.position - headTransform.position;
            Quaternion inverseYaw = Quaternion.Inverse(Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f));
            Vector3 pos = inverseYaw * offset;

            int idx = kvp.Value;
            frameJointData[idx * 3]     = pos.x;
            frameJointData[idx * 3 + 1] = pos.y;
            frameJointData[idx * 3 + 2] = pos.z;
        }

        return frameJointData;
    }
}
