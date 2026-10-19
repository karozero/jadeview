namespace JadeView;

// Verified source bindings, not inferred facial semantics. The generic preview
// consumes capabilities without depending on character geometry keys.
internal sealed record PcMorphProfile
{
    internal required string Id { get; init; }
    internal required uint[] GeometryKeys { get; init; }
    internal required int GroupCount { get; init; }
    internal required int ChannelCount { get; init; }
    internal required Dictionary<int, uint[]> ReferenceRows { get; init; }
    internal required IReadOnlyList<int> BlinkGroups { get; init; }
    internal string BlinkTitle { get; init; } = "PC head blink";
    internal bool CanBakeBlink { get; init; }
    internal int[] EmptyGroups { get; init; } = [];
    internal bool RequireNonzeroBlink { get; init; }
    internal MorphExpressionPresetSet? ExpressionPresets { get; init; }

    internal bool Matches(uint geometry, FaceMorphData data)
    {
        bool matchingLayout = GeometryKeys.Contains(geometry) &&
            data.Groups.Length == GroupCount && data.Channels.Length == ChannelCount;
        if (!matchingLayout) return false;
        bool matchingReferences = ReferenceRows.All(row =>
            data.Channels[row.Key].GroupReferences.SequenceEqual(row.Value));
        bool matchingEmptyGroups = EmptyGroups.All(group => data.Groups[group].VertexIndices.Length == 0);
        if (!matchingReferences || !matchingEmptyGroups) return false;
        return !RequireNonzeroBlink || BlinkGroups.All(group =>
            data.Groups[group].AccumulatedDeltas().Values.Any(delta => delta.LengthSquared() > 0));
    }
}
