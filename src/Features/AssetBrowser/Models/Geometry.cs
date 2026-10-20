using System.Numerics;
using System.Globalization;
using System.Text;

namespace JadeView;

public sealed class Geometry
{
    public int Offset, Size;
    public uint Key, MaterialKey;
    public List<string> Objects = [];
    public string Name => Objects.Count > 0 ? Objects[0] : $"Unnamed · {(Key != 0 ? Key.ToString("X8") : $"@{Offset:X8}")}";
    public string ExportName => string.Concat(Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    public Vector3[] Positions = [];
    public Vector2[] Uvs = [];
    public uint[] VertexColors = [];
    public SkinBinding[] Skin = [];
    public List<Part> Parts = [];
    public List<GeometryBillboard> Billboards = [];
    // SPG appends an ordered, contiguous range of four-vertex sprites.
    internal bool IsBillboardVertex(int vertex) => Billboards.Count > 0
        && vertex >= Billboards[0].Vertex && vertex < Billboards[^1].Vertex + 4;
    public ParticleDefinition? Particles;
    public int ParticleStart, ParticlePart;
    public int Triangles => Parts.Sum(p => p.VertexIndices.Length / 3);
    public sealed record Part(int MaterialId, int[] VertexIndices, int[] UvIndices);
    public Vector3 Min => Positions.Aggregate(Vector3.Min);
    public Vector3 Max => Positions.Aggregate(Vector3.Max);
    public static Vector3 ToRender(Vector3 v) => new(v.X, v.Z, -v.Y);
    public string Obj()
    {
        var s = new StringBuilder("# JadeView: original Jade vertices and per-corner UVs\n");
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        foreach (var v in Positions) s.AppendLine($"v {F(v.X)} {F(v.Y)} {F(v.Z)}");
        foreach (var uv in Uvs) s.AppendLine($"vt {F(uv.X)} {F(1 - uv.Y)}");
        foreach (var part in Parts)
        {
            s.AppendLine($"g material_{part.MaterialId}");
            for (int i = 0; i < part.VertexIndices.Length; i += 3)
                s.AppendLine("f " + string.Join(' ', Enumerable.Range(i, 3).Select(j => Uvs.Length == 0 ? $"{part.VertexIndices[j] + 1}" : $"{part.VertexIndices[j] + 1}/{part.UvIndices[j] + 1}")));
        }
        return s.ToString();
    }
}
