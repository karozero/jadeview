using System.Numerics;

namespace JadeView;

internal sealed class GpuDiffuseLightingCache
{
    internal int Revision;
    internal ulong Signature;
    internal int PoseRevision;
    internal int AppearanceRevision;
    internal Vector4 RliDestination;
    internal float RliFactor;
    internal bool HasSample;
    internal Vector3[] Positions = [];
    internal Vector3[] Normals = [];
    internal uint[] RawPaint = [];
    internal uint[] ComputedColors = [];

    internal void EnsureCapacity(int count)
    {
        if (Positions.Length == count) return;
        Positions = new Vector3[count];
        Normals = new Vector3[count];
        RawPaint = new uint[count];
        ComputedColors = new uint[count];
        HasSample = false;
    }
}
