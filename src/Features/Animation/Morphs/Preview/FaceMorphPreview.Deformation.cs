using System.Numerics;

namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private Vector3[]? deformedPositionBuffer;

    public Vector3[] Positions()
    {
        if (Data == null || !weights.Any(weight => weight != 0)) return Geometry.Positions;
        deformedPositionBuffer ??= new Vector3[Geometry.Positions.Length];
        var positions = deformedPositionBuffer!;
        Geometry.Positions.CopyTo(positions, 0);
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
