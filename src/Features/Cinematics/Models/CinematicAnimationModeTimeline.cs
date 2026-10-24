namespace JadeView;

internal enum CinematicAnimationModeEventKind
{
    LiteralModeSetRequest,
    MalformedModeSetCall,
    BodyActionStart,
    GameplayHandoff
}

internal enum CinematicAnimationModeMeaning
{
    SpeedFromAnimation,
    UpdateFrequency,
    EncodedButUnnamed,
    SpeedFromDynamics,
    OutsideKnownEncoding
}

internal sealed record CinematicAnimationModeEvent(
    float Time,
    CinematicAnimationModeEventKind Kind,
    uint TargetKey,
    uint? TrackIndex,
    uint? ModeValue,
    CinematicAnimationModeMeaning? Meaning,
    bool TrackWithinEngineCapacity,
    bool AfterGameplayHandoff,
    bool SharesTimestamp);

/// <summary>
/// A source-event ledger for ANI_ModeSet, body-action starts, and gameplay
/// handoffs. It deliberately does not claim an effective engine mode.
/// </summary>
internal static class CinematicAnimationModeTimeline
{
    private const int AnimationModeSetSelector = 0x32D3;
    private const uint MaximumSimultaneousAnimationTracks = 4;

    internal static CinematicAnimationModeEvent[] Read(
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls,
        uint targetKey,
        IEnumerable<float> bodyActionStarts,
        IEnumerable<float> gameplayHandoffs,
        float playhead)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(bodyActionStarts);
        ArgumentNullException.ThrowIfNull(gameplayHandoffs);
        if (!float.IsFinite(playhead) || playhead < 0)
            throw new ArgumentOutOfRangeException(nameof(playhead));

        var events = new List<CinematicAnimationModeEvent>();
        foreach (var (time, call) in calls)
        {
            if (call.Selector != AnimationModeSetSelector || call.Target != targetKey
                || !IsVisibleAtPlayhead(time, playhead))
                continue;

            events.Add(DecodeModeSet(time, call));
        }

        events.AddRange(ReadBoundaries(bodyActionStarts, playhead,
            CinematicAnimationModeEventKind.BodyActionStart, targetKey));
        events.AddRange(ReadBoundaries(gameplayHandoffs, playhead,
            CinematicAnimationModeEventKind.GameplayHandoff, targetKey));

        return AddOrderingAndHandoffMetadata(events);
    }

    private static CinematicAnimationModeEvent DecodeModeSet(float time, CinematicLiteralCall call)
    {
        if (call.Arguments.Length != 2)
            return new(time, CinematicAnimationModeEventKind.MalformedModeSetCall,
                call.Target, null, null, null, false, false, false);

        uint trackIndex = call.Arguments[0];
        uint modeValue = call.Arguments[1];
        var meaning = MeaningFor(modeValue);
        return new(time, CinematicAnimationModeEventKind.LiteralModeSetRequest,
            call.Target, trackIndex, modeValue, meaning,
            trackIndex < MaximumSimultaneousAnimationTracks, false, false);
    }

    private static CinematicAnimationModeMeaning MeaningFor(uint modeValue) => modeValue switch
    {
        0 => CinematicAnimationModeMeaning.SpeedFromAnimation,
        1 => CinematicAnimationModeMeaning.UpdateFrequency,
        2 => CinematicAnimationModeMeaning.EncodedButUnnamed,
        3 => CinematicAnimationModeMeaning.SpeedFromDynamics,
        _ => CinematicAnimationModeMeaning.OutsideKnownEncoding
    };

    private static IEnumerable<CinematicAnimationModeEvent> ReadBoundaries(
        IEnumerable<float> times, float playhead,
        CinematicAnimationModeEventKind kind, uint targetKey)
    {
        foreach (float time in times.Where(time => IsVisibleAtPlayhead(time, playhead)))
            yield return new(time, kind, targetKey, null, null, null,
                false, false, false);
    }

    private static CinematicAnimationModeEvent[] AddOrderingAndHandoffMetadata(
        List<CinematicAnimationModeEvent> events)
    {
        var sameTimeGroups = events.GroupBy(item => item.Time)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        float[] handoffTimes = events
            .Where(item => item.Kind == CinematicAnimationModeEventKind.GameplayHandoff)
            .Select(item => item.Time)
            .ToArray();

        return events.OrderBy(item => item.Time)
            .ThenBy(item => item.Kind)
            .Select(item => item with
            {
                AfterGameplayHandoff = handoffTimes.Any(handoffTime => item.Time > handoffTime)
                    && item.Kind == CinematicAnimationModeEventKind.LiteralModeSetRequest,
                SharesTimestamp = sameTimeGroups.Contains(item.Time)
            })
            .ToArray();
    }

    private static bool IsVisibleAtPlayhead(float time, float playhead) =>
        float.IsFinite(time) && time >= 0 && time <= playhead;
}
