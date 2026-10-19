using System.Security.Cryptography;

namespace JadeView;

internal static class MorphProjectSource
{
    internal static string ModifierHash(FaceMorphPreview preview) => Convert.ToHexString(SHA256.HashData(preview.Modifier.Data));
    internal static string GeometryHash(Geometry geometry)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(geometry.Positions.Length);
            foreach (var point in geometry.Positions) { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
            writer.Write(geometry.Parts.Count);
            foreach (var part in geometry.Parts)
            {
                writer.Write(part.VertexIndices.Length);
                foreach (int vertex in part.VertexIndices) writer.Write(vertex);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
