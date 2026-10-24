using System.Numerics;

namespace JadeView;

/// <summary>
/// Pure OBJMove interpretation shared by game-script consumers.
/// Source references: Jaded AIfunctions_obj.c (AI_EvalFunc_OBJMove_C), EVEplay.h
/// (EVE_f_GetCurrentRatio), and Iris ai_gameobjectfunctions.c (same evaluator).
/// </summary>
internal static class GameScriptMoveSemantics
{
    internal static GameScriptMoveResolution EvaluateEveProgress(
        GameScriptMoveCommand command, float elapsedEventSeconds)
    {
        GameScriptMoveResolution? validationFailure = Validate(command);
        if (validationFailure is { } failure) return failure;
        if (command.Context != GameScriptExecutionContext.EveEvent)
            return Unresolved(GameScriptMoveResolutionStatus.UnknownExecutionContext,
                "Only a command known to run from an EVE event can be sampled over event time.");
        if (!float.IsFinite(elapsedEventSeconds))
            return Unresolved(GameScriptMoveResolutionStatus.InvalidElapsedTime,
                "EVE elapsed time must be finite.");
        if (command.EventDurationSeconds is not { } duration || !float.IsFinite(duration) || duration <= 0)
            return Unresolved(GameScriptMoveResolutionStatus.MissingOrInvalidEventDuration,
                "The source event duration is required for EVE ratio scaling.");

        float ratio = Math.Clamp(elapsedEventSeconds / duration, 0, 1);
        return Resolved(command.WorldDisplacement * ratio,
            "EVE scales OBJMove by elapsed event time divided by event duration.");
    }

    /// <summary>
    /// Resolves the effect of one AI callback dispatch. The caller must invoke this once
    /// per observed dispatch; the result is not a per-frame velocity.
    /// </summary>
    internal static GameScriptMoveResolution ResolveAiCallbackDispatch(GameScriptMoveCommand command)
    {
        GameScriptMoveResolution? validationFailure = Validate(command);
        if (validationFailure is { } failure) return failure;
        if (command.Context != GameScriptExecutionContext.AiCallback)
            return Unresolved(GameScriptMoveResolutionStatus.UnknownExecutionContext,
                "A complete displacement is only returned for an explicit AI callback context.");

        return Resolved(command.WorldDisplacement,
            "The non-EVE OBJMove path adds the full vector in one callback dispatch.");
    }

    private static GameScriptMoveResolution? Validate(GameScriptMoveCommand command)
    {
        if (command.TargetKey is 0 or uint.MaxValue)
            return Unresolved(GameScriptMoveResolutionStatus.InvalidTarget,
                "OBJMove target key is missing or reserved.");
        if (!IsFinite(command.WorldDisplacement))
            return Unresolved(GameScriptMoveResolutionStatus.InvalidDisplacement,
                "OBJMove displacement must contain finite components.");
        return null;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static GameScriptMoveResolution Resolved(Vector3 displacement, string explanation) =>
        new(GameScriptMoveResolutionStatus.Resolved, displacement, explanation);

    private static GameScriptMoveResolution Unresolved(
        GameScriptMoveResolutionStatus status, string explanation) =>
        new(status, Vector3.Zero, explanation);
}
