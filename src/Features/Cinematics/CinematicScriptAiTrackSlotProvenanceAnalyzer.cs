namespace JadeView;

internal sealed record CinematicScriptAiTrackSlotProvenance(int[] CandidateSlots, bool HasUnknownPath);

internal static class CinematicScriptAiTrackSlotProvenanceAnalyzer
{
    private const int TrackCount = 5;

    internal static IReadOnlyDictionary<string, CinematicScriptAiTrackSlotProvenance> Analyze(
        CinematicScriptCallbackSlot[]? callbackSlots,
        IReadOnlyDictionary<string, CinematicScriptFunctionCall[]> functionCalls,
        IReadOnlySet<string> modelFunctions)
    {
        if (callbackSlots == null) return new Dictionary<string, CinematicScriptAiTrackSlotProvenance>();

        var visited = new HashSet<(string FunctionName, int? TrackIndex)>();
        var pending = new Queue<(string FunctionName, int? TrackIndex)>();
        foreach (CinematicScriptCallbackSlot slot in callbackSlots)
        {
            if (slot.Index is < 0 or >= TrackCount
                || slot.FunctionName is not { Length: > 0 } functionName
                || !modelFunctions.Contains(functionName))
                continue;
            Enqueue(functionName, slot.Index);
        }

        while (pending.TryDequeue(out var state))
        {
            foreach (CinematicScriptFunctionCall edge in functionCalls.GetValueOrDefault(state.FunctionName, []))
            {
                if (!modelFunctions.Contains(edge.To) || !TryResolveTargetSlot(edge, state.TrackIndex, out int? targetSlot))
                    continue;
                Enqueue(edge.To, targetSlot);
            }
        }

        return visited.GroupBy(state => state.FunctionName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new CinematicScriptAiTrackSlotProvenance(
                group.Where(state => state.TrackIndex is not null)
                    .Select(state => state.TrackIndex!.Value).Distinct().Order().ToArray(),
                group.Any(state => state.TrackIndex is null)), StringComparer.Ordinal);

        void Enqueue(string functionName, int? trackIndex)
        {
            var state = (functionName, trackIndex);
            if (visited.Add(state)) pending.Enqueue(state);
        }
    }

    private static bool TryResolveTargetSlot(CinematicScriptFunctionCall edge, int? callerSlot,
        out int? targetSlot)
    {
        switch (edge.Dispatch)
        {
            case "execute":
            case "set-current-track":
            case "replace-current-track":
                targetSlot = callerSlot;
                return true;
            case "track-change":
                targetSlot = edge.TrackIndex is >= 0 and < TrackCount ? edge.TrackIndex : null;
                return true;
            default:
                targetSlot = null;
                return edge.Dispatch.StartsWith("register-callback:", StringComparison.Ordinal);
        }
    }
}
