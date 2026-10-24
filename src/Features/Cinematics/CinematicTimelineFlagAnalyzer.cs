namespace JadeView;

internal static class CinematicTimelineFlagAnalyzer
{
    internal static IEnumerable<(float Time, CinematicLiteralCall Call)>? ExternalTimelineCalls(
        CinematicScriptOwner owner, uint? sourceClipKey,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? timelineCalls)
    {
        // The selected cinematic clip is decoded from the SceneEvents resource
        // it previews. Its state writes cannot prove that the same owner passed
        // the gates that would start that resource.
        return sourceClipKey is { } clipKey && owner.SceneEventsKey == clipKey
            ? null
            : timelineCalls;
    }

    internal static float? FindFirstExternalStartFlagTime(CinematicScriptOwner owner,
        uint sourceClipKey, IEnumerable<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        var externalCalls = ExternalTimelineCalls(owner, sourceClipKey, timelineCalls);
        return externalCalls == null ? null : FindFirstSatisfiedStartFlagTime(owner, externalCalls);
    }

    internal static float? FindFirstSatisfiedStartFlagTime(CinematicScriptOwner owner,
        IEnumerable<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        var startFlagGates = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner)
            .Where(access => access.Access == "reads"
                && access.RequiredSet != null
                && access.VariableName is "des_i_specialFlag_test1" or "des_i_specialFlag_test2")
            .DistinctBy(access => (access.Flag, access.RequiredSet))
            .ToArray();
        if (startFlagGates.Length == 0) return null;

        var writes = timelineCalls
            .Select(item => DecodeWrite(item.Time, item.Call))
            .OfType<FlagWrite>()
            .Where(write => write.Flag > 0 && float.IsFinite(write.Time))
            .OrderBy(write => write.Time)
            .ToArray();

        var flagStates = new Dictionary<int, bool?>();
        foreach (var simultaneousWrites in writes.GroupBy(write => write.Time).OrderBy(group => group.Key))
        {
            foreach (var writesForFlag in simultaneousWrites.GroupBy(write => write.Flag))
            {
                bool[] states = writesForFlag.Select(write => write.Set).Distinct().ToArray();
                flagStates[writesForFlag.Key] = states.Length == 1 ? states[0] : null;
            }

            bool allStartFlagsSatisfied = startFlagGates.All(gate =>
                flagStates.TryGetValue(gate.Flag, out bool? currentState)
                && currentState is { } state && state == gate.RequiredSet);
            if (allStartFlagsSatisfied) return simultaneousWrites.Key;
        }

        return null;
    }

    internal static CinematicTimelineFlagGate[] Analyze(CinematicScriptOwner owner,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? timelineCalls, float playhead)
    {
        var writes = (timelineCalls ?? [])
            .Select(item => DecodeWrite(item.Time, item.Call))
            .OfType<FlagWrite>()
            .Where(write => write.Flag > 0)
            .OrderBy(write => write.Time)
            .ToArray();

        return CinematicScriptFlagFlowAnalyzer.GetAccesses(owner)
            .Where(access => access.Access == "reads")
            .Select(access => CreateGate(access, writes, playhead))
            .Where(gate => gate != null)
            .Cast<CinematicTimelineFlagGate>()
            .DistinctBy(gate => (gate.Flag, gate.RequiredSet, gate.Predicate))
            .ToArray();
    }

    private static CinematicTimelineFlagGate? CreateGate(CinematicScriptFlagAccess access,
        FlagWrite[] writes, float playhead)
    {
        bool? requiredSet = RequiredState(access.Description);
        if (requiredSet == null) return null;

        var matchingWrites = writes.Where(write => write.Flag == access.Flag).ToArray();
        var priorWrites = matchingWrites.Where(write => write.Time <= playhead).ToArray();
        var futureWrites = matchingWrites.Where(write => write.Time > playhead).ToArray();
        var lastWrite = priorWrites.LastOrDefault();
        bool? currentState = lastWrite?.Set;
        return new(access.Flag, requiredSet.Value, access.Description, currentState,
            lastWrite?.Time,
            futureWrites.FirstOrDefault()?.Time,
            currentState is { } state ? state == requiredSet.Value : null);
    }

    private static bool? RequiredState(string description)
    {
        if (description.Contains("requires set", StringComparison.OrdinalIgnoreCase)) return true;
        if (description.Contains("requires clear", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static FlagWrite? DecodeWrite(float time, CinematicLiteralCall call)
    {
        if (call.Arguments.Length == 0 || call.Arguments[0] is 0 or > int.MaxValue) return null;
        return CinematicEngineFunctionCatalog.NameFor(call.Selector) switch
        {
            "ARRGlobalIntFlagSet" => new(time, (int)call.Arguments[0], true),
            "ARRGlobalIntFlagClear" => new(time, (int)call.Arguments[0], false),
            _ => null
        };
    }

    private sealed record FlagWrite(float Time, int Flag, bool Set);
}

internal sealed record CinematicTimelineFlagGate(int Flag, bool RequiredSet, string Predicate,
    bool? CurrentSet, float? LastWriteTime, float? NextWriteTime, bool? Satisfied);
