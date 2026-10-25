namespace JadeView;

internal enum CinematicEventFunctionWaitMode
{
    None,
    WaitWhileTrue,
    WaitWhileFalse
}

internal sealed record CinematicEventFunctionControlFlow(
    int KeyIndex,
    float Time,
    int KeyFlags,
    CinematicEventFunctionWaitMode WaitMode,
    uint? TestedGlobalFlag,
    int DownstreamKeyCount,
    float? FirstDownstreamKeyTime,
    float? LastDownstreamKeyTime);

/// <summary>
/// Reads the control flags used by Iris when an EVE key invokes an AI-function node.
/// It describes the serialized wait behavior without evaluating the node or its result.
/// </summary>
internal static class CinematicEventFunctionControlFlowReader
{
    private const int EventKindMask = 0x07C0;
    private const int AiFunctionEventKind = 0x0040;
    private const int WaitWhenResultIsTrue = 0x0008;
    private const int WaitWhenResultIsFalse = 0x0010;

    internal static CinematicEventFunctionControlFlow[] Read(
        CinematicEmbeddedEventTrackList eventList, CinematicEmbeddedEventTrack track)
    {
        ArgumentNullException.ThrowIfNull(eventList);
        ArgumentNullException.ThrowIfNull(track);
        if (track.SourceTrack is not { } sourceTrack) return [];

        return sourceTrack.Keys
            .Select((key, index) => (Key: key, Index: index))
            .Where(item => (item.Key.Flags & EventKindMask) == AiFunctionEventKind)
            .Select(item =>
            {
                AnimationKey[] downstreamKeys = sourceTrack.Keys.Skip(item.Index + 1).ToArray();
                return new CinematicEventFunctionControlFlow(item.Index, item.Key.Time,
                    item.Key.Flags, WaitModeFor(item.Key.Flags),
                    ResolveTestedGlobalFlag(eventList, sourceTrack, item.Key),
                    downstreamKeys.Length,
                    downstreamKeys.FirstOrDefault()?.Time,
                    downstreamKeys.LastOrDefault()?.Time);
            })
            .ToArray();
    }

    private static uint? ResolveTestedGlobalFlag(CinematicEmbeddedEventTrackList eventList,
        AnimationTrack track, AnimationKey key)
    {
        if (key.OpaquePayloads.Count != 1) return null;

        CinematicLiteralCall? call = CinematicLiteralCall.Decode(key.OpaquePayloads[0], track.Target,
            eventList.OwnerKey);
        if (call is not { Arguments.Length: 1 }
            || CinematicEngineFunctionCatalog.NameFor(call.Selector) != "ARRGlobalIntFlagTest")
            return null;

        return call.Arguments[0];
    }

    internal static CinematicEventFunctionWaitMode WaitModeFor(int flags)
    {
        // Iris uses `if (wait-true) ... else if (wait-false) ...`; true takes precedence
        // if unusual data sets both control bits.
        if ((flags & WaitWhenResultIsTrue) != 0)
            return CinematicEventFunctionWaitMode.WaitWhileTrue;
        if ((flags & WaitWhenResultIsFalse) != 0)
            return CinematicEventFunctionWaitMode.WaitWhileFalse;
        return CinematicEventFunctionWaitMode.None;
    }
}
