namespace JadeView;

internal static class CinematicTimelineFlagAnalyzer
{
    internal static CinematicTimelineFlagGate[] Analyze(CinematicScriptOwner owner,
        IEnumerable<(float Time, CinematicLiteralCall Call)> timelineCalls, float playhead)
    {
        var writes = timelineCalls
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
