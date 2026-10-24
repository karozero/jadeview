namespace JadeView;

internal enum CinematicScriptHierarchyBindingStatus
{
    Detached,
    Bound,
    ChildMissing,
    ChildAmbiguous,
    ParentMissing,
    ParentAmbiguous,
    Cycle
}

internal enum CinematicScriptHierarchyPoseStatus
{
    NotEvaluated,
    Applied,
    WorldPose,
    MissingEventPose,
    UnresolvedEndpoint,
    InvalidParentMatrix,
    InvalidBaseMatrix,
    Cycle
}

internal sealed record CinematicScriptHierarchyBinding(
    uint ChildKey,
    uint? ParentKey,
    int ChildEndpointCount,
    int ParentEndpointCount,
    CinematicScriptHierarchyBindingStatus Status,
    CinematicScriptHierarchyPoseStatus PoseStatus,
    float? ChangedAt);

/// <summary>
/// Replays explicit literal parent bindings only. It does not calculate matrices;
/// callers must keep pose evaluation separate until the event-time world poses
/// are available.
/// </summary>
internal static class CinematicScriptHierarchyBindingReducer
{
    internal static IReadOnlyDictionary<uint, CinematicScriptHierarchyBinding> Evaluate(
        IEnumerable<CinematicScriptHierarchyCommand> commands,
        IReadOnlyDictionary<uint, uint?> initialParents,
        IReadOnlyDictionary<uint, int> endpointCounts,
        float time)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(initialParents);
        ArgumentNullException.ThrowIfNull(endpointCounts);
        if (!float.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));

        var bindings = initialParents.ToDictionary(pair => pair.Key, pair =>
            CreateBinding(pair.Key, pair.Value, endpointCounts, null));

        foreach (CinematicScriptHierarchyCommand command in commands
                     .Where(item => float.IsFinite(item.Time) && item.Time <= time)
                     .OrderBy(item => item.Time))
        {
            CinematicScriptHierarchyBinding binding = command.Kind switch
            {
                CinematicScriptHierarchyCommandKind.Reset => CreateBinding(
                    command.ChildKey, null, endpointCounts, command.Time),
                CinematicScriptHierarchyCommandKind.SetParent => CreateBinding(
                    command.ChildKey, command.ParentKey, endpointCounts, command.Time),
                _ => CreateBinding(command.ChildKey, null, endpointCounts, command.Time)
            };
            bindings[command.ChildKey] = binding;
        }

        MarkCycles(bindings);
        return bindings;
    }

    private static CinematicScriptHierarchyBinding CreateBinding(uint childKey, uint? parentKey,
        IReadOnlyDictionary<uint, int> endpointCounts, float? changedAt)
    {
        int childCount = endpointCounts.GetValueOrDefault(childKey);
        if (childCount != 1)
        {
            CinematicScriptHierarchyBindingStatus childStatus = childCount > 1
                ? CinematicScriptHierarchyBindingStatus.ChildAmbiguous
                : CinematicScriptHierarchyBindingStatus.ChildMissing;
            int unresolvedParentCount = parentKey is uint unresolvedKey
                ? endpointCounts.GetValueOrDefault(unresolvedKey) : 0;
            return new(childKey, parentKey, childCount, unresolvedParentCount,
                childStatus, CinematicScriptHierarchyPoseStatus.NotEvaluated, changedAt);
        }

        if (parentKey is null)
            return new(childKey, null, childCount, 0, CinematicScriptHierarchyBindingStatus.Detached,
                CinematicScriptHierarchyPoseStatus.NotEvaluated, changedAt);

        int parentCount = endpointCounts.GetValueOrDefault(parentKey.Value);
        CinematicScriptHierarchyBindingStatus status = parentCount switch
        {
            1 => CinematicScriptHierarchyBindingStatus.Bound,
            > 1 => CinematicScriptHierarchyBindingStatus.ParentAmbiguous,
            _ => CinematicScriptHierarchyBindingStatus.ParentMissing
        };
        return new(childKey, parentKey, childCount, parentCount, status,
            CinematicScriptHierarchyPoseStatus.NotEvaluated, changedAt);
    }

    private static void MarkCycles(Dictionary<uint, CinematicScriptHierarchyBinding> bindings)
    {
        var cycleKeys = new HashSet<uint>();
        foreach (uint startKey in bindings.Keys)
        {
            var visitedIndex = new Dictionary<uint, int>();
            var path = new List<uint>();
            uint currentKey = startKey;
            while (bindings.TryGetValue(currentKey, out CinematicScriptHierarchyBinding? current)
                   && current.Status == CinematicScriptHierarchyBindingStatus.Bound
                   && current.ParentKey is uint parentKey)
            {
                if (visitedIndex.TryGetValue(currentKey, out int cycleStart))
                {
                    cycleKeys.UnionWith(path.Skip(cycleStart));
                    break;
                }

                visitedIndex[currentKey] = path.Count;
                path.Add(currentKey);
                currentKey = parentKey;
            }
        }

        foreach (uint childKey in cycleKeys)
        {
            CinematicScriptHierarchyBinding binding = bindings[childKey];
            bindings[childKey] = binding with { Status = CinematicScriptHierarchyBindingStatus.Cycle };
        }
    }
}
