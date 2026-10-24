namespace JadeView;

internal sealed record CinematicLiteralCall(uint Target, int Selector, uint[] Arguments)
{
    private const int VectorConstructorSelector = 0x1770;

    // The first serialized word is retained for audits, but an EVE AI-function
    // receiver is bound from the executing track (or its owner), not this word.
    internal uint SerializedContextKey { get; init; }

    internal static (float Time, CinematicLiteralCall Call)[] ReadTimeline(AnimationClip clip,
        uint fallbackOwnerKey = 0)
    {
        return ReadTimelineWithDuration(clip, fallbackOwnerKey)
            .Select(item => (item.Time, item.Call))
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();
    }

    internal static (float Time, float Duration, CinematicLiteralCall Call)[] ReadTimelineWithDuration(
        AnimationClip clip, uint fallbackOwnerKey = 0) => clip.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.SelectMany(payload =>
                CinematicOwnerContext.ForTrack(clip, track, fallbackOwnerKey)
                    .Select(context => (key.Time, key.Duration,
                        Call: Decode(payload, track.Target, context,
                            clip.UsesEventTrackExecutionContext))))))
            .Where(item => item.Call != null)
            .Select(item => (item.Time, item.Duration, Call: item.Call!))
            .DistinctBy(item => (item.Time, item.Duration, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();

    internal static CinematicLiteralCall? Decode(AnimationOpaquePayload payload, uint trackTarget, uint ownerKey,
        bool useTrackExecutionObject = false)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length < 4) return null;
        bool hasContext = CinematicWordPairList.IsLiteral(rows[0], 0x28)
            && CinematicWordPairList.HasSelector(rows[1], 2, 2054);
        bool hasEnd = CinematicWordPairList.IsEnd(rows[^1]);
        var function = rows[^2];
        if (!hasContext || !hasEnd || function.Value != 0 || function.Tag >> 24 != 3) return null;
        if (!TryReadArguments(rows[2..^2], out uint[] arguments)) return null;
        uint inheritedTarget = trackTarget is > 2 and < uint.MaxValue ? trackTarget : ownerKey;
        uint serializedContextKey = rows[0].Value;
        uint target = useTrackExecutionObject && inheritedTarget is > 2 and < uint.MaxValue
            ? inheritedTarget
            : serializedContextKey != 0 ? serializedContextKey : inheritedTarget;
        if (target is 0 or uint.MaxValue) return null;
        return new(target, (int)(function.Tag & 0xffff), arguments)
        {
            SerializedContextKey = serializedContextKey
        };
    }

    private static bool TryReadArguments((uint Value, uint Tag)[] rows, out uint[] arguments)
    {
        var values = new List<uint>(rows.Length);
        for (int index = 0; index < rows.Length;)
        {
            var row = rows[index];
            bool isVectorConstructor = index + 3 < rows.Length
                && rows[index..(index + 3)].All(component => CinematicWordPairList.IsLiteral(component, 0x22))
                && CinematicWordPairList.HasSelector(rows[index + 3], 3, VectorConstructorSelector);
            if (isVectorConstructor)
            {
                values.AddRange(rows[index..(index + 3)].Select(component => component.Value));
                index += 4;
                continue;
            }

            if (CinematicWordPairList.IsLiteral(row, 0x21)
                || CinematicWordPairList.IsLiteral(row, 0x22)
                || CinematicWordPairList.IsLiteral(row, 0x28))
            {
                values.Add(row.Value);
                index++;
                continue;
            }

            arguments = [];
            return false;
        }

        arguments = values.ToArray();
        return true;
    }
}
