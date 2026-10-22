namespace JadeView;

internal sealed record CinematicLiteralCall(uint Target, int Selector, uint[] Arguments)
{
    internal static (float Time, CinematicLiteralCall Call)[] ReadTimeline(AnimationClip clip,
        uint fallbackOwnerKey = 0)
    {
        return clip.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.SelectMany(payload =>
                CinematicOwnerContext.ForTrack(clip, track, fallbackOwnerKey)
                    .Select(context => (key.Time, Call: Decode(payload, track.Target, context))))))
            .Where(item => item.Call != null)
            .Select(item => (item.Time, Call: item.Call!))
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();
    }

    internal static CinematicLiteralCall? Decode(AnimationOpaquePayload payload, uint trackTarget, uint ownerKey)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length < 4) return null;
        bool hasContext = CinematicWordPairList.IsLiteral(rows[0], 0x28)
            && CinematicWordPairList.HasSelector(rows[1], 2, 2054);
        bool hasEnd = CinematicWordPairList.IsEnd(rows[^1]);
        var function = rows[^2];
        if (!hasContext || !hasEnd || function.Value != 0 || function.Tag >> 24 != 3) return null;
        if (rows[2..^2].Any(row => !CinematicWordPairList.IsLiteral(row, 0x21)
                && !CinematicWordPairList.IsLiteral(row, 0x22)
                && !CinematicWordPairList.IsLiteral(row, 0x28))) return null;
        uint inheritedTarget = trackTarget == 0 ? ownerKey : trackTarget;
        uint target = rows[0].Value == 0 ? inheritedTarget : rows[0].Value;
        if (target is 0 or uint.MaxValue) return null;
        return new(target, (int)(function.Tag & 0xffff), rows[2..^2].Select(row => row.Value).ToArray());
    }
}
