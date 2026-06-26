using UnityEngine;
using System.Collections.Generic;
using System.Linq;

internal class PrehensionGestureSampleBones : ScriptableObject
{
    [System.Serializable]
    public struct Bone
    {
        public int id;
        public Vector3 position;
        public Quaternion rotation;
    }
    
    [System.Serializable]
    public class BoneArrayWrapper
    {
        public Bone[] bones;
    }

    public string gesture_id;
    public string sample_id;
    public List<BoneArrayWrapper> frames = new List<BoneArrayWrapper>();

    public bool WriteBoneFrames(List<List<Bone>> recordedFrames)
    {
        // foreach(IList<OVRBone> recordedFrame in recordedFrames)
        // {
        //     BoneArrayWrapper frameToAdd = new BoneArrayWrapper();
        //     frameToAdd.bones = new Bone[recordedFrame.Count];
        //     for(int i = 0;i < recordedFrame.Count;i++)
        //     {
        //         OVRBone ovrBone = recordedFrame[i]; 
        //         Bone boneToAdd = new Bone();
        //         boneToAdd.id = (int)ovrBone.Id;
        //         // boneToAdd.transform = ovrBone.Transform;
        //         boneToAdd.position = ovrBone.Transform.position;
        //         boneToAdd.rotation = ovrBone.Transform.localRotation;
        //         frameToAdd.bones[i] = boneToAdd;
        //     }
        //     frames.Add(frameToAdd);
        // }

        foreach(List<Bone> frame in recordedFrames)
        {
            BoneArrayWrapper frameToSave = new BoneArrayWrapper();
            frameToSave.bones = frame.ToArray();
            frames.Add(frameToSave);
        }

        return true;
    }
    
    // public List<List<Bone>> GetFrames()
    // {
    //     List<List<Bone>> frames = new List<List<Bone>>();
    //     foreach(var frame in frames)
    //     {
    //         rawFrames.Add(frame.values);
    //     }
    //     return rawFrames;
    // }
}
