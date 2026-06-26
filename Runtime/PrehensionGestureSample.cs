using UnityEngine;
using System.Collections.Generic;

[PreferBinarySerialization]
internal class PrehensionGestureSample : ScriptableObject
{
    [System.Serializable]
    public class FloatArrayWrapper
    {
        public float[] values;
    }

    public string gesture_id;
    public string sample_id;
    public List<FloatArrayWrapper> frames = new List<FloatArrayWrapper>();

    public List<float[]> GetRawFrames()
    {
        List<float[]> rawFrames = new List<float[]>();
        foreach(var frame in frames)
        {
            rawFrames.Add(frame.values);
        }
        return rawFrames;
    }
}
