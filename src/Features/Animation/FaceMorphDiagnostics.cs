using System.Security.Cryptography;

namespace JadeView;

public static class FaceMorphDiagnostics
{
    public static FaceMorphSourceIssue[] SourceIssues(Geometry geometry, FaceMorphData data)
    {
        if (geometry.Key is not (0x620030FB or 0x6200318D) || data.Groups.Length <= 37) return [];
        var group = data.Groups[37];
        string signature = Signature(group);
        bool ordinary = geometry.Key == 0x620030FB && signature == "548EE3B453CF9A36C41FDD587C2F8A0A600DB551501FD44B0BE9522FBBCED6B1";
        bool highDetail = geometry.Key == 0x6200318D && signature == "BFAEC59F0F5DC7630DE2D120A8D95D5FC495A8B9C0077770144BA2717CEA3EAC";
        if (!ordinary && !highDetail) return [];
        // Revision required: qualify PC controller/baseline semantics before attempting a correction.
        // Exact source signatures keep this observation from flagging corrected or unrelated assets.
        return [new(37, "Known eyelid artifacts when this raw target is applied alone. PC controller/baseline behavior is unresolved; source deltas are preserved.")];
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
