using System.Numerics;

namespace JadeView;

/// <summary>
/// Source-backed OBJMove semantics: a world-space displacement applied to a target.
/// For EVE, duration is the serialized event duration in seconds (frames / 60).
/// </summary>
internal sealed record GameScriptMoveCommand(
    uint TargetKey,
    Vector3 WorldDisplacement,
    GameScriptExecutionContext Context,
    float? EventDurationSeconds,
    GameScriptCommandOrigin Origin);

internal sealed record GameScriptCommandOrigin(
    uint WorldKey,
    uint OwnerKey,
    uint EventKey,
    int ChannelIndex,
    float SourceTimeSeconds);

/// <summary>
/// One active slice of an EVE move event. `EventElapsedSeconds` is the source-event
/// time consumed by this timeline interval, which lets loops and pauses remain explicit.
/// </summary>
internal sealed record GameScriptMoveSegment(
    GameScriptMoveCommand Command,
    float TimelineStartSeconds,
    float TimelineEndSeconds,
    float EventElapsedSeconds)
{
    internal GameScriptMoveResolution EvaluateAt(float timelineTimeSeconds)
    {
        if (!float.IsFinite(timelineTimeSeconds) || !float.IsFinite(TimelineStartSeconds)
            || !float.IsFinite(TimelineEndSeconds) || TimelineEndSeconds <= TimelineStartSeconds
            || !float.IsFinite(EventElapsedSeconds) || EventElapsedSeconds <= 0)
            return new(GameScriptMoveResolutionStatus.InvalidElapsedTime, Vector3.Zero,
                "Timeline move interval or sample is invalid.");
        if (timelineTimeSeconds <= TimelineStartSeconds)
            return new(GameScriptMoveResolutionStatus.Resolved, Vector3.Zero,
                "The EVE move interval has not started.");

        float progress = Math.Clamp((timelineTimeSeconds - TimelineStartSeconds)
            / (TimelineEndSeconds - TimelineStartSeconds), 0, 1);
        return GameScriptMoveSemantics.EvaluateEveProgress(Command, EventElapsedSeconds * progress);
    }
}

internal enum GameScriptMoveResolutionStatus
{
    Resolved,
    UnknownExecutionContext,
    InvalidTarget,
    InvalidDisplacement,
    InvalidElapsedTime,
    MissingOrInvalidEventDuration
}

internal readonly record struct GameScriptMoveResolution(
    GameScriptMoveResolutionStatus Status,
    Vector3 Displacement,
    string Explanation)
{
    internal bool IsResolved => Status == GameScriptMoveResolutionStatus.Resolved;
}
