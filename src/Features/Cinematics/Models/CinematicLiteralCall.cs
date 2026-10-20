namespace JadeView;

internal sealed record CinematicLiteralCall(uint Target, int Selector, uint[] Arguments)
{
    internal static (float Time, CinematicLiteralCall Call)[] ReadTimeline(AnimationClip clip) => clip.Tracks
        .Where(track => track.Bone < 0)
        .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.Select(payload =>
            (key.Time, Call: Decode(payload, track.Target, clip.CinematicOwnerKey)))))
        .Where(item => item.Call != null)
        .Select(item => (item.Time, item.Call!))
        .OrderBy(item => item.Time)
        .ToArray();

    internal static CinematicLiteralCall? Decode(AnimationOpaquePayload payload, uint trackTarget, uint ownerKey)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length < 4) return null;
        bool hasContext = CinematicWordPairList.IsLiteral(rows[0], 0x28)
            && CinematicWordPairList.HasSelector(rows[1], 2, 2054);
        bool hasEnd = CinematicWordPairList.IsEnd(rows[^1]);
        var function = rows[^2];
        if (!hasContext || !hasEnd || function.Value != 0 || function.Tag >> 24 != 3) return null;
        if (rows[2..^2].Any(row => !CinematicWordPairList.IsLiteral(row, 0x21))) return null;
        uint inheritedTarget = trackTarget == 0 ? ownerKey : trackTarget;
        uint target = rows[0].Value == 0 ? inheritedTarget : rows[0].Value;
        if (target is 0 or uint.MaxValue) return null;
        return new(target, (int)(function.Tag & 0xffff), rows[2..^2].Select(row => row.Value).ToArray());
    }
}
