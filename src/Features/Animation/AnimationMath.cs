using System.Numerics;

namespace JadeView;

public static class AnimationMath
{
    public static Matrix4x4 SkinInverseBind(SkinBinding binding, uint drawFlags)
    {
        var inverse = binding.InverseBind;
        if ((drawFlags & 0x8000) == 0)
        {
            inverse.M11 = -inverse.M11;
            inverse.M21 = -inverse.M21;
            inverse.M31 = -inverse.M31;
            inverse.M41 = -inverse.M41;
        }
        return inverse;
    }
    public static Matrix4x4 Trs(TransformData t) => Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
    public static Matrix4x4 Matrix(byte[] bytes, int p)
    {
        var j = JadeMatrix.Read(bytes.AsSpan(p, 68));
        var m = new Matrix4x4(j.Ix,j.Iy,j.Iz,0,j.Jx,j.Jy,j.Jz,0,j.Kx,j.Ky,j.Kz,0,j.Tx,j.Ty,j.Tz,1);
        return (j.Type & 8) != 0 ? Matrix4x4.CreateScale(j.Sx,j.Sy,j.Sz) * m : m;
    }
    public static Matrix4x4 ToRender(Matrix4x4 jade)
    {
        var b = new Matrix4x4(1,0,0,0,0,0,1,0,0,-1,0,0,0,0,0,1);
        return b * jade * Matrix4x4.Transpose(b);
    }
    public static Vector3[] Normals(Vector3[] positions, List<Geometry.Part> parts)
        => Normals(positions, parts, positions.Length, parts.Count);

    public static Vector3[] Normals(Vector3[] positions, List<Geometry.Part> parts,
        int vertexCount, int partCount)
    {
        vertexCount = Math.Clamp(vertexCount, 0, positions.Length);
        partCount = Math.Clamp(partCount, 0, parts.Count);
        var values = new Vector3[vertexCount]; var counts = new int[vertexCount];
        FillNormals(positions, parts, vertexCount, partCount, values, counts);
        return values;
    }

    internal static void FillNormals(Vector3[] positions, List<Geometry.Part> parts,
        int vertexCount, int partCount, Span<Vector3> values, Span<int> counts)
    {
        vertexCount = Math.Clamp(vertexCount, 0, positions.Length);
        partCount = Math.Clamp(partCount, 0, parts.Count);
        if (values.Length < vertexCount || counts.Length < vertexCount)
            throw new ArgumentException("Normal scratch buffers are smaller than the vertex range.");
        values = values[..vertexCount];
        counts = counts[..vertexCount];
        values.Clear();
        counts.Clear();
        for (int partIndex = 0; partIndex < partCount; partIndex++)
        {
            var part = parts[partIndex];
            for (int index = 0; index < part.VertexIndices.Length; index += 3)
            {
                int first = part.VertexIndices[index];
                int second = part.VertexIndices[index + 1];
                int third = part.VertexIndices[index + 2];
                if (first >= vertexCount || second >= vertexCount || third >= vertexCount) continue;
                var normal = Vector3.Cross(positions[second] - positions[first],
                    positions[third] - positions[first]);
                if (normal.LengthSquared() > 1e-20f) normal = Vector3.Normalize(normal);
                else normal = Vector3.Zero;
                values[first] += normal;
                values[second] += normal;
                values[third] += normal;
                counts[first]++;
                counts[second]++;
                counts[third]++;
            }
        }
        for (int vertexIndex = 0; vertexIndex < values.Length; vertexIndex++)
            if (counts[vertexIndex] > 0) values[vertexIndex] /= counts[vertexIndex];
    }
}

