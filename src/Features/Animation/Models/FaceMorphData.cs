using System.Numerics;

namespace JadeView;

public sealed record FaceMorphGroup(int Index, int[] VertexIndices, Vector3[] Deltas);
public sealed record FaceMorphChannel(uint WeightBits, uint ProgressionBits, uint[] GroupReferences)
{
    public float Weight => BitConverter.UInt32BitsToSingle(WeightBits);
    public float Progression => BitConverter.UInt32BitsToSingle(ProgressionBits);
}

// Channel words are source state, not an authored animation timeline.
public sealed record FaceMorphData(uint[] HeaderWords, FaceMorphGroup[] Groups, FaceMorphChannel[] Channels);
