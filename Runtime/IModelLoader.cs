using System.Collections.Generic;

internal interface IModelLoader
{
    void InitializeModelParams(int numJointPositions, int windowFrameLength, int numGestureClasses);
    void LoadModel();
    int RunModelInference(List<float[]> jointData, out float[] outputData);
}
