namespace JadeView;

internal enum CinematicOwnerExecutionRoute
{
    RunningInit,
    CallbackReachability,
    ManualPreview,
    CinematicEventCall,
    NestedEventCall
}

internal enum CinematicOwnerPreviewStatus
{
    Included,
    Disabled,
    Available
}

internal enum CinematicRuntimeGateStatus
{
    Unknown
}

internal sealed record CinematicOwnerExecutionLedgerEntry(
    CinematicScriptOwnerId OwnerId,
    uint EventKey,
    CinematicOwnerExecutionRoute Route,
    CinematicOwnerPreviewStatus PreviewStatus,
    CinematicRuntimeGateStatus RuntimeGate,
    int[] ChannelIndices,
    float? PreviewStartTime,
    string Provenance,
    string Limit)
{
    internal CinematicCallbackSourceBinding[] CallbackSources { get; init; } = [];
}

internal enum CinematicCallbackSourceBindingStatus
{
    Bound,
    SlotTableUnavailable,
    NoMatchingSlot,
    AmbiguousSlot
}

internal sealed record CinematicCallbackSourceBinding(CinematicCallbackSourceBindingStatus Status,
    int? SlotIndex, string? SlotFunctionName, string CallbackPath);

internal sealed record CinematicOwnerChannelExecution(
    CinematicScriptOwnerId OwnerId,
    uint EventKey,
    int ChannelIndex,
    CinematicOwnerExecutionRoute PreviewRoute,
    CinematicTrackPlaybackClock Clock);
