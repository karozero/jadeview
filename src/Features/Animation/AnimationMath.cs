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
    {
        var values = new Vector3[positions.Length]; var counts = new int[positions.Length];
        foreach (var part in parts) for (int i = 0; i < part.VertexIndices.Length; i += 3)
        {
            int a=part.VertexIndices[i],b=part.VertexIndices[i+1],c=part.VertexIndices[i+2];
            var n=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]);
            if(n.LengthSquared()>1e-20f) n=Vector3.Normalize(n); else n=Vector3.Zero;
            values[a]+=n; values[b]+=n; values[c]+=n; counts[a]++;counts[b]++;counts[c]++;
        }
        for(int i=0;i<values.Length;i++)if(counts[i]>0)values[i]/=counts[i];
        return values;
    }
}

