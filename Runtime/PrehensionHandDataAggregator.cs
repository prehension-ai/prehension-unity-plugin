using System.Collections.Generic;
using UnityEngine;

internal class PrehensionHandDataAggregator
{
    public List<float[]> data { get; private set; }
    private int windowFrameLength;
    
    public PrehensionHandDataAggregator(int windowFrameLength)
    {
        this.windowFrameLength = windowFrameLength;
        
        data = new List<float[]>();
    }

    public void AddDataToDataWindow(float[] jointData)
    {
        data.Add(jointData);
        while(data.Count > windowFrameLength)
        {
            data.RemoveAt(0);
        }
    }
}
