using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

public sealed class ResolvedObjectInfo
{
    public string Name { get; init; } = "";
    public uint GaoKey { get; init; }
    public uint GeometryKey { get; init; }
    public uint MaterialKey { get; init; }
    public int Vertices { get; init; }
    public int Triangles { get; init; }
    public int PartsCount { get; init; }
    public int ResolvedTexturesCount { get; init; }
    public List<string> TextureNames { get; } = [];
    public bool IsFullyResolved => PartsCount > 0 && ResolvedTexturesCount == PartsCount;
}

