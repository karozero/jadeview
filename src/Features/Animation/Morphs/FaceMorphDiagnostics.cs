using System.Security.Cryptography;

namespace JadeView;

public static class FaceMorphDiagnostics
{
    public static FaceMorphSourceIssue[] SourceIssues(Geometry geometry, FaceMorphData data)
    {
        return PcMorphSourceIssues.Entries
            .Where(issue => issue.Geometry == geometry.Key && issue.Group < data.Groups.Length)
            .Where(issue => Signature(data.Groups[issue.Group]) == issue.Signature)
            .Select(issue => new FaceMorphSourceIssue(issue.Group, issue.Message)).ToArray();
    }

    private static string Signature(FaceMorphGroup group)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (int vertex in group.VertexIndices) writer.Write(vertex);
            foreach (var delta in group.Deltas)
            {
                writer.Write(delta.X); writer.Write(delta.Y); writer.Write(delta.Z);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
