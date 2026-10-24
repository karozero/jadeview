using System.Numerics;

namespace JadeView;

internal sealed record CinematicScriptHierarchyPoseFrame(
    float Time,
    IReadOnlyDictionary<uint, Matrix4x4> BaseWorldMatrices);

internal sealed record CinematicScriptHierarchyParent(uint ParentKey, Matrix4x4 LocalMatrix);

internal sealed record CinematicScriptHierarchyPoseResult(
    IReadOnlyDictionary<uint, Matrix4x4> WorldMatrices,
    IReadOnlyDictionary<uint, CinematicScriptHierarchyPoseStatus> Statuses);

/// <summary>
/// Evaluates literal script reparent operations against sampled, unmodified world poses.
/// Set stores a parent-relative matrix while preserving world pose; Reset preserves the
/// current pose and then follows the object's own base animation, if any.
/// </summary>
internal static class CinematicScriptHierarchyPoseTimeline
{
    internal static CinematicScriptHierarchyPoseResult Evaluate(
        IReadOnlyList<CinematicScriptHierarchyCommand> commands,
        IReadOnlyDictionary<uint, CinematicScriptHierarchyParent> serializedParents,
        IReadOnlyDictionary<uint, int> endpointCounts,
        IReadOnlyList<CinematicScriptHierarchyPoseFrame> eventFrames,
        IReadOnlyDictionary<uint, Matrix4x4> currentBaseWorldMatrices,
        float time,
        IReadOnlyCollection<uint>? precomputedAffectedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(serializedParents);
        ArgumentNullException.ThrowIfNull(endpointCounts);
        ArgumentNullException.ThrowIfNull(eventFrames);
        ArgumentNullException.ThrowIfNull(currentBaseWorldMatrices);
        if (!float.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));

        var states = new Dictionary<uint, ScriptParentState>();
        var statuses = new Dictionary<uint, CinematicScriptHierarchyPoseStatus>();
        var framesByTime = eventFrames.ToDictionary(frame => frame.Time);

        foreach (CinematicScriptHierarchyCommand command in commands
                     .Select((item, index) => (item, index))
                     .Where(entry => float.IsFinite(entry.item.Time) && entry.item.Time <= time)
                     .OrderBy(entry => entry.item.Time)
                     .ThenBy(entry => entry.index)
                     .Select(entry => entry.item))
        {
            if (!framesByTime.TryGetValue(command.Time, out CinematicScriptHierarchyPoseFrame? frame))
            {
                statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.MissingEventPose;
                continue;
            }

            if (!HasUniqueEndpoint(command.ChildKey, endpointCounts))
            {
                statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.UnresolvedEndpoint;
                continue;
            }

            if (!TryResolveWorld(command.ChildKey, frame.BaseWorldMatrices, states,
                    serializedParents, endpointCounts, out Matrix4x4 childWorld,
                    out CinematicScriptHierarchyPoseStatus childStatus))
            {
                statuses[command.ChildKey] = childStatus;
                continue;
            }

            uint? requestedParentKey = command.Kind == CinematicScriptHierarchyCommandKind.SetParent
                ? command.ParentKey : null;
            if (requestedParentKey is not uint parentKey)
            {
                states[command.ChildKey] = CreateDetachedState(
                    command.ChildKey, childWorld, frame.BaseWorldMatrices, statuses);
                if (!statuses.ContainsKey(command.ChildKey))
                    statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.Applied;
                continue;
            }

            CinematicScriptHierarchyPoseStatus parentStatus = CinematicScriptHierarchyPoseStatus.UnresolvedEndpoint;
            if (!HasUniqueEndpoint(parentKey, endpointCounts)
                || !TryResolveWorld(parentKey, frame.BaseWorldMatrices, states,
                    serializedParents, endpointCounts, out Matrix4x4 parentWorld, out parentStatus))
            {
                states[command.ChildKey] = CreateDetachedState(
                    command.ChildKey, childWorld, frame.BaseWorldMatrices, statuses);
                statuses[command.ChildKey] = HasUniqueEndpoint(parentKey, endpointCounts)
                    ? parentStatus : CinematicScriptHierarchyPoseStatus.UnresolvedEndpoint;
                continue;
            }

            if (!Matrix4x4.Invert(parentWorld, out Matrix4x4 inverseParent)
                || !IsFinite(inverseParent))
            {
                states[command.ChildKey] = CreateDetachedState(
                    command.ChildKey, childWorld, frame.BaseWorldMatrices, statuses);
                statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.InvalidParentMatrix;
                continue;
            }

            Matrix4x4 localMatrix = childWorld * inverseParent;
            if (!IsFinite(localMatrix))
            {
                states[command.ChildKey] = CreateDetachedState(
                    command.ChildKey, childWorld, frame.BaseWorldMatrices, statuses);
                statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.InvalidParentMatrix;
                continue;
            }

            states[command.ChildKey] = ScriptParentState.Bound(parentKey, localMatrix);
            statuses[command.ChildKey] = CinematicScriptHierarchyPoseStatus.Applied;
        }

        IReadOnlyCollection<uint> affectedKeys = precomputedAffectedKeys
            ?? FindAffectedKeys(commands, serializedParents).Order().ToArray();
        var worldMatrices = new Dictionary<uint, Matrix4x4>();
        foreach (uint key in affectedKeys)
        {
            if (!HasUniqueEndpoint(key, endpointCounts)
                || !DependsOnScriptState(key, states, serializedParents, endpointCounts))
                continue;

            if (TryResolveWorld(key, currentBaseWorldMatrices, states, serializedParents,
                    endpointCounts, out Matrix4x4 worldMatrix,
                    out CinematicScriptHierarchyPoseStatus poseStatus))
            {
                worldMatrices[key] = worldMatrix;
                if (!statuses.ContainsKey(key)) statuses[key] = poseStatus;
            }
            else if (!statuses.ContainsKey(key))
            {
                statuses[key] = poseStatus;
            }
        }

        return new(worldMatrices, statuses);
    }

    private static ScriptParentState CreateDetachedState(uint childKey, Matrix4x4 childWorld,
        IReadOnlyDictionary<uint, Matrix4x4> baseWorldMatrices,
        Dictionary<uint, CinematicScriptHierarchyPoseStatus> statuses)
    {
        if (!baseWorldMatrices.TryGetValue(childKey, out Matrix4x4 baseWorld)
            || !Matrix4x4.Invert(baseWorld, out Matrix4x4 inverseBase)
            || !IsFinite(inverseBase))
        {
            statuses[childKey] = CinematicScriptHierarchyPoseStatus.InvalidBaseMatrix;
            return ScriptParentState.Frozen(childWorld);
        }

        Matrix4x4 detachedOffset = childWorld * inverseBase;
        if (!IsFinite(detachedOffset))
        {
            statuses[childKey] = CinematicScriptHierarchyPoseStatus.InvalidBaseMatrix;
            return ScriptParentState.Frozen(childWorld);
        }

        statuses[childKey] = CinematicScriptHierarchyPoseStatus.Applied;
        return ScriptParentState.Detached(detachedOffset);
    }

    private static bool TryResolveWorld(uint key,
        IReadOnlyDictionary<uint, Matrix4x4> baseWorldMatrices,
        IReadOnlyDictionary<uint, ScriptParentState> states,
        IReadOnlyDictionary<uint, CinematicScriptHierarchyParent> serializedParents,
        IReadOnlyDictionary<uint, int> endpointCounts,
        out Matrix4x4 worldMatrix,
        out CinematicScriptHierarchyPoseStatus status)
    {
        return TryResolveWorld(key, baseWorldMatrices, states, serializedParents,
            endpointCounts, new HashSet<uint>(), out worldMatrix, out status);
    }

    private static bool TryResolveWorld(uint key,
        IReadOnlyDictionary<uint, Matrix4x4> baseWorldMatrices,
        IReadOnlyDictionary<uint, ScriptParentState> states,
        IReadOnlyDictionary<uint, CinematicScriptHierarchyParent> serializedParents,
        IReadOnlyDictionary<uint, int> endpointCounts,
        HashSet<uint> resolving,
        out Matrix4x4 worldMatrix,
        out CinematicScriptHierarchyPoseStatus status)
    {
        worldMatrix = Matrix4x4.Identity;
        if (!HasUniqueEndpoint(key, endpointCounts))
        {
            status = CinematicScriptHierarchyPoseStatus.UnresolvedEndpoint;
            return false;
        }
        if (!resolving.Add(key))
        {
            status = CinematicScriptHierarchyPoseStatus.Cycle;
            return false;
        }

        try
        {
            if (states.TryGetValue(key, out ScriptParentState? state))
            {
                if (state.ParentKey is uint parentKey)
                {
                    if (!TryResolveWorld(parentKey, baseWorldMatrices, states, serializedParents,
                            endpointCounts, resolving, out Matrix4x4 parentWorld, out status))
                        return false;
                    worldMatrix = state.LocalMatrix * parentWorld;
                }
                else if (state.DetachedOffset is Matrix4x4 offset)
                {
                    if (!baseWorldMatrices.TryGetValue(key, out Matrix4x4 baseWorld))
                    {
                        status = CinematicScriptHierarchyPoseStatus.MissingEventPose;
                        return false;
                    }
                    worldMatrix = offset * baseWorld;
                }
                else if (state.FrozenWorld is Matrix4x4 frozenWorld)
                {
                    worldMatrix = frozenWorld;
                }
                else
                {
                    status = CinematicScriptHierarchyPoseStatus.MissingEventPose;
                    return false;
                }
            }
            else if (serializedParents.TryGetValue(key, out CinematicScriptHierarchyParent? serializedParent))
            {
                if (!TryResolveWorld(serializedParent.ParentKey, baseWorldMatrices, states,
                        serializedParents, endpointCounts, resolving, out Matrix4x4 parentWorld,
                        out status))
                    return false;
                worldMatrix = serializedParent.LocalMatrix * parentWorld;
            }
            else if (!baseWorldMatrices.TryGetValue(key, out worldMatrix))
            {
                status = CinematicScriptHierarchyPoseStatus.MissingEventPose;
                return false;
            }

            if (!IsFinite(worldMatrix))
            {
                status = CinematicScriptHierarchyPoseStatus.InvalidBaseMatrix;
                return false;
            }

            status = CinematicScriptHierarchyPoseStatus.WorldPose;
            return true;
        }
        finally
        {
            resolving.Remove(key);
        }
    }

    private static bool DependsOnScriptState(uint key,
        IReadOnlyDictionary<uint, ScriptParentState> states,
        IReadOnlyDictionary<uint, CinematicScriptHierarchyParent> serializedParents,
        IReadOnlyDictionary<uint, int> endpointCounts)
    {
        var visited = new HashSet<uint>();
        while (visited.Add(key))
        {
            if (states.ContainsKey(key)) return true;
            if (!serializedParents.TryGetValue(key, out CinematicScriptHierarchyParent? parent)) return false;
            if (!HasUniqueEndpoint(parent.ParentKey, endpointCounts)) return false;
            key = parent.ParentKey;
        }
        return false;
    }

    internal static HashSet<uint> FindAffectedKeys(IEnumerable<CinematicScriptHierarchyCommand> commands,
        IReadOnlyDictionary<uint, CinematicScriptHierarchyParent> serializedParents)
    {
        var affected = commands.SelectMany(command => command.ParentKey is uint parentKey
                ? new[] { command.ChildKey, parentKey }
                : new[] { command.ChildKey })
            .ToHashSet();

        var childrenByParent = serializedParents
            .GroupBy(pair => pair.Value.ParentKey, pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pending = new Queue<uint>(affected);
        while (pending.TryDequeue(out uint parentKey))
        {
            if (!childrenByParent.TryGetValue(parentKey, out uint[]? children)) continue;
            foreach (uint childKey in children)
            {
                if (affected.Add(childKey)) pending.Enqueue(childKey);
            }
        }

        return affected;
    }

    private static bool HasUniqueEndpoint(uint key, IReadOnlyDictionary<uint, int> endpointCounts) =>
        endpointCounts.GetValueOrDefault(key) == 1;

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private sealed record ScriptParentState(
        uint? ParentKey,
        Matrix4x4 LocalMatrix,
        Matrix4x4? DetachedOffset,
        Matrix4x4? FrozenWorld)
    {
        internal static ScriptParentState Bound(uint parentKey, Matrix4x4 localMatrix) =>
            new(parentKey, localMatrix, null, null);

        internal static ScriptParentState Detached(Matrix4x4 detachedOffset) =>
            new(null, Matrix4x4.Identity, detachedOffset, null);

        internal static ScriptParentState Frozen(Matrix4x4 frozenWorld) =>
            new(null, Matrix4x4.Identity, null, frozenWorld);
    }
}
