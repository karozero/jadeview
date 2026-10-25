namespace JadeView;

/// <summary>
/// Applies statically resolvable per-object RLI display callbacks as a draw-scoped
/// override. Each frame is rebuilt from the source scene value to avoid leaking a
/// callback's temporary lighting state to other objects.
/// </summary>
internal sealed class CinematicRliDisplayCallbackPlayback
{
    private sealed record Runtime(CinematicScriptOwner Owner, SceneObject OwnerObject,
        SceneObject Target, CinematicScriptRliDisplayCallbackCandidate[] Callbacks,
        CinematicScriptReachableEngineCall[] EngineCalls,
        CinematicScriptRliValueFlowAssignment[] ValueAssignments,
        CinematicScriptReachableOvaFieldAccess[] OvaFieldAccesses,
        IReadOnlyDictionary<string, CinematicScriptFunctionPath> CallbackPaths,
        (float Time, CinematicLiteralCall Call)[] TimelineCalls);

    private readonly Runtime[] runtimes;
    private readonly SceneObject[] targets;

    private CinematicRliDisplayCallbackPlayback(Runtime[] runtimes)
    {
        this.runtimes = runtimes;
        targets = runtimes.Select(runtime => runtime.Target).Distinct().ToArray();
    }

    internal string[] Diagnostics => runtimes.Select(runtime =>
            $"{runtime.Owner.Name} → {runtime.Target.Name}: paired RLI draw hooks are resolved from source callbacks; unresolved registration or scalar state stays unapplied")
        .ToArray();

    internal static CinematicRliDisplayCallbackPlayback? Create(SceneDocument document,
        WorldData world, IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);

        var objectGroups = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct().GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var runtimes = new List<Runtime>();

        foreach (CinematicScriptOwner owner in document.CinematicScriptOwners.Values
                     .Where(owner => owner.WorldKey == world.Key))
        {
            if (!objectGroups.TryGetValue(owner.OwnerKey, out SceneObject[]? ownerMatches)
                || ownerMatches.Length != 1 || owner.ModelFunctionNames.Length == 0) continue;

            CinematicScriptReachableEngineCall[] engineCalls =
                CinematicScriptReader.ReachableEngineCalls(owner);
            CinematicScriptRliDisplayCallbackCandidate[] candidates =
                CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner, engineCalls);
            CinematicScriptRliValueFlowAssignment[] valueAssignments =
                CinematicScriptReader.ReachableRliValueFlowAssignments(owner);
            CinematicScriptReachableOvaFieldAccess[] ovaFieldAccesses =
                CinematicScriptReader.ReachableOvaFieldAccesses(owner);
            IReadOnlyDictionary<string, CinematicScriptFunctionPath> callbackPaths =
                CinematicScriptReader.NestedCallbackPaths(owner)
                    .ToDictionary(path => path.FunctionName, StringComparer.Ordinal);
            foreach (IGrouping<(uint? ContextKey, uint? TargetKey),
                         CinematicScriptRliDisplayCallbackCandidate> targetCallbacks in
                     candidates.GroupBy(callback => (callback.ContextKey, callback.TargetKey)))
            {
                if (targetCallbacks.Key.ContextKey != owner.OwnerKey
                    || targetCallbacks.Key.TargetKey is not { } targetKey
                    || !objectGroups.TryGetValue(targetKey, out SceneObject[]? targets)
                    || targets.Length != 1) continue;

                runtimes.Add(new(owner, ownerMatches[0], targets[0], targetCallbacks.ToArray(),
                    engineCalls, valueAssignments, ovaFieldAccesses, callbackPaths,
                    timelineCalls.Where(item => item.Call.Target == owner.OwnerKey).ToArray()));
            }
        }

        Runtime[] uniqueTargetRuntimes = runtimes.GroupBy(runtime => runtime.Target.GaoKey)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        return uniqueTargetRuntimes.Length == 0 ? null
            : new(uniqueTargetRuntimes.OrderBy(runtime => runtime.Target.GaoKey)
                .ThenBy(runtime => runtime.Owner.Name, StringComparer.Ordinal).ToArray());
    }

    internal void Evaluate(float playhead, IReadOnlyDictionary<int, bool?>? savedFlags,
        CinematicObjectMorphPlayback? objectMorphPlayback)
    {
        Clear();
        foreach (Runtime runtime in runtimes)
        {
            if (!CinematicRliDisplayCallbackResolver.TryResolvePairedOverride(runtime.Owner,
                    runtime.OwnerObject, runtime.Callbacks, runtime.EngineCalls,
                    runtime.ValueAssignments, runtime.CallbackPaths, runtime.TimelineCalls,
                    savedFlags, playhead,
                    (channel, time) => ResolveChannelProgression(runtime.OwnerObject,
                        channel, time, objectMorphPlayback), out CinematicRliDrawState state,
                    runtime.OvaFieldAccesses))
                continue;
            runtime.Target.PreviewRliDrawState = state;
        }
    }

    private static float? ResolveChannelProgression(SceneObject ownerObject, int channel,
        float time, CinematicObjectMorphPlayback? objectMorphPlayback)
    {
        FaceMorphChannelState? sampled = objectMorphPlayback?.SampleChannelState(
            ownerObject, channel, time);
        if (sampled is { IsValid: true } sourceState) return sourceState.Progression;
        FaceMorphPreview? preview = ownerObject.MorphPreview;
        if (preview == null || (uint)channel >= (uint)preview.ChannelCount) return null;
        FaceMorphChannelState current = preview.GetChannel(channel);
        return current.IsValid ? current.Progression : null;
    }

    internal void Clear()
    {
        foreach (SceneObject target in targets)
            target.PreviewRliDrawState = null;
    }
}
