using System.Numerics;

namespace JadeView;

public sealed partial class FaceMorphPreview
{
    public Vector3[] Positions()
    {
        if (Data == null || !weights.Any(weight => weight != 0)) return Geometry.Positions;
        var positions = (Vector3[])Geometry.Positions.Clone();
        foreach (var group in Data.Groups)
        {
            float weight = weights[group.Index];
            if (weight == 0) continue;
            for (int entry = 0; entry < group.VertexIndices.Length; entry++)
                positions[group.VertexIndices[entry]] += group.Deltas[entry] * weight;
        }
        return positions;
    }
}
