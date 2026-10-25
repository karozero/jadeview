namespace JadeView;

internal sealed record CinematicScriptCallbackWindow(float Start, float? End);

/// <summary>
/// Reconstructs only callback intervals whose entry and exit gates can be sampled
/// from serialized EVE state. Unknown timers and unsupported gates stay unresolved.
/// </summary>
internal static class CinematicScriptCallbackWindowResolver
{
    internal static bool IsPathProvenUnreachable(CinematicScriptOwner owner,
        SceneObject ownerObject, CinematicScriptFunctionPath entryPath,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        float[] candidateTimes = CandidateTimes(timelineCalls);
        uint? originalCapabilities = ownerObject.PreviewCapabilities;
        try
        {
            foreach (float candidateTime in candidateTimes)
            {
                ownerObject.PreviewCapabilities = CapabilitiesAt(ownerObject, candidateTime,
                    timelineCalls);
                CinematicScriptBranchValue route =
                    CinematicScriptRuntimeBranchEvaluator.EvaluateCallbackPath(owner,
                        ownerObject, entryPath.Steps, candidateTime, timelineCalls, savedFlags).Value;
                if (route != CinematicScriptBranchValue.False) return false;
            }
            return true;
        }
        finally
        {
            ownerObject.PreviewCapabilities = originalCapabilities;
        }
    }

    internal static CinematicScriptCallbackWindow? Resolve(CinematicScriptOwner owner,
        SceneObject ownerObject, string functionName,
        CinematicScriptCallbackPathStep[] entryPath,
        CinematicScriptBranchConditionSource[] entryConditions,
        IReadOnlyDictionary<string, long?> entryAliases,
        CinematicScriptFunctionPath[] outgoingPaths,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags,
        bool allowUnboundedStateMutation = false)
    {
        float[] candidateTimes = CandidateTimes(timelineCalls);
        CinematicScriptCallbackPathStep[] exitSteps = outgoingPaths
            .Select(path => path.Steps.LastOrDefault())
            .OfType<CinematicScriptCallbackPathStep>()
            .Where(step => step.FromFunction == functionName
                && step.Dispatch == "replace-current-track"
                && step.ToFunction != functionName
                && step.DispatchConditionsResolved)
            .ToArray();

        float? start = null;
        float? end = null;
        uint? originalCapabilities = ownerObject.PreviewCapabilities;
        try
        {
            foreach (float candidateTime in candidateTimes)
            {
                ownerObject.PreviewCapabilities = CapabilitiesAt(ownerObject, candidateTime,
                    timelineCalls);
                if (start == null)
                {
                    var route = CinematicScriptRuntimeBranchEvaluator.EvaluateCallbackPath(owner,
                        ownerObject, entryPath, candidateTime, timelineCalls, savedFlags);
                    if (route.Value != CinematicScriptBranchValue.True) continue;
                    var entry = CinematicScriptRuntimeBranchEvaluator.Evaluate(owner, ownerObject,
                        entryConditions, candidateTime, timelineCalls, savedFlags, entryAliases);
                    if (entry.Value != CinematicScriptBranchValue.True) continue;
                    start = candidateTime;
                    // A callback without a resolved replacement cannot acquire a
                    // later exit. Its route also does not need resampling after entry.
                    if (exitSteps.Length == 0)
                        return allowUnboundedStateMutation ? new(candidateTime, null) : null;
                }

                if (start is not { } startTime || candidateTime < startTime) continue;
                bool exitReached = exitSteps.Any(step =>
                    CinematicScriptRuntimeBranchEvaluator.Evaluate(owner, ownerObject,
                        step.BranchConditions, candidateTime, timelineCalls, savedFlags).Value
                        == CinematicScriptBranchValue.True);
                if (exitReached)
                {
                    end = candidateTime;
                    break;
                }
            }
        }
        finally
        {
            ownerObject.PreviewCapabilities = originalCapabilities;
        }

        if (start is not { } resolvedStart) return null;
        if (end is { } resolvedEnd) return new(resolvedStart, resolvedEnd);
        return allowUnboundedStateMutation ? new(resolvedStart, null) : null;
    }

    private static float[] CandidateTimes(
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        float[] candidateTimes = timelineCalls.Select(item => item.Time)
            .Where(float.IsFinite).Append(0).Distinct().Order().ToArray();
        return candidateTimes.Length == 0 ? [0] : candidateTimes;
    }

    internal static uint? CapabilitiesAt(SceneObject ownerObject, float time,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        uint capabilities = ownerObject.InitialCapabilities & ushort.MaxValue;
        foreach (var (eventTime, call) in timelineCalls.OrderBy(item => item.Time))
        {
            if (eventTime > time || call.Target != ownerObject.GaoKey) continue;
            string api = CinematicEngineFunctionCatalog.NameFor(call.Selector);
            if (api == "OBJ_CapaSet" && call.Arguments.Length == 2)
                capabilities = (capabilities & ~(call.Arguments[1] & ushort.MaxValue))
                    | (call.Arguments[0] & ushort.MaxValue);
            else if (api == "OBJ_CapaToggle" && call.Arguments.Length == 1)
                capabilities ^= call.Arguments[0] & ushort.MaxValue;
        }
        return capabilities & ushort.MaxValue;
    }
}
