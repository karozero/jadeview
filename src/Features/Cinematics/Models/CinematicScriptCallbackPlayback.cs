namespace JadeView;

/// <summary>
/// Previews the source-confirmed linked morph-channel handoffs and their paired
/// visibility state changes when the recovered callback route is reachable.
/// Unsupported callback gates remain unknown and are not applied.
/// </summary>
internal sealed partial class CinematicScriptCallbackPlayback
{
    private sealed record TransferRuntime(CinematicScriptOwner Owner, SceneObject OwnerObject,
        SceneObject SourceObject, FaceMorphPreview SourcePreview, SceneObject TargetObject,
        FaceMorphPreview TargetPreview, CinematicScriptChannelTransfer Transfer,
        CinematicScriptFunctionPath EntryPath, CinematicScriptFunctionPath[] OutgoingPaths);

    private sealed record StateEffectRuntime(CinematicScriptOwner Owner, SceneObject OwnerObject,
        SceneObject TargetObject, CinematicScriptCallbackStateEffect Effect,
        CinematicScriptFunctionPath EntryPath, CinematicScriptFunctionPath[] OutgoingPaths,
        ushort? InitialCollisionActivationMask = null);

    private sealed record MorphSnapshot(SceneObject Target, FaceMorphPreview Preview,
        FaceMorphChannelState[]? Channels, float[] Weights);

    private readonly (float Time, CinematicLiteralCall Call)[] timelineCalls;
    private readonly TransferRuntime[] transfers;
    private readonly StateEffectRuntime[] stateEffects;
    private readonly MorphSnapshot[] morphSnapshots;
    private readonly CinematicObjectMorphPlayback? objectMorphPlayback;
    private readonly CinematicRliDisplayCallbackPlayback? rliDisplayPlayback;
    private readonly CinematicScriptMaterialConstantPlayback? materialConstantPlayback;
    private readonly CinematicScriptLocalAlphaPlayback? localAlphaPlayback;
    private IReadOnlyDictionary<uint, uint> materialConstants = new Dictionary<uint, uint>();
    private readonly Dictionary<string, CinematicScriptCallbackWindow?> callbackWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, (float Time, CinematicLiteralCall Call)[]> ownerTimelineCalls = [];
    private readonly Dictionary<uint, CinematicScriptCallbackStateEffect[]> ownerStateEffects = [];
    private readonly Dictionary<SceneObject, StateEffectRuntime[]> visibilityEffectsByTarget = [];
    private readonly Dictionary<SceneObject, int[]> visibilitySavedFlagsByTarget;
    private readonly Dictionary<string, bool> unreachablePaths = new(StringComparer.Ordinal);
    private readonly Dictionary<(uint Target, string Api), float[]> explicitStateCallTimes = [];
    private readonly SceneObject[] visibilityTargets;
    private IReadOnlyDictionary<int, bool?>? savedFlagAssumptions;
    private Dictionary<int, bool?>? savedFlagSnapshot;
    private string assumptionCacheKey = string.Empty;
    private float lastTime;
    internal Action<string>? DiagnosticTrace { get; set; }

    private CinematicScriptCallbackPlayback(
        (float Time, CinematicLiteralCall Call)[] timelineCalls,
        TransferRuntime[] transfers, StateEffectRuntime[] stateEffects,
        CinematicObjectMorphPlayback? objectMorphPlayback,
        CinematicRliDisplayCallbackPlayback? rliDisplayPlayback,
        CinematicScriptMaterialConstantPlayback? materialConstantPlayback,
        CinematicScriptLocalAlphaPlayback? localAlphaPlayback)
    {
        this.timelineCalls = timelineCalls;
        this.transfers = transfers;
        this.stateEffects = stateEffects;
        this.objectMorphPlayback = objectMorphPlayback;
        this.rliDisplayPlayback = rliDisplayPlayback;
        this.materialConstantPlayback = materialConstantPlayback;
        this.localAlphaPlayback = localAlphaPlayback;
        visibilityEffectsByTarget = stateEffects
            .Where(effect => IsVisibilityStateEffect(effect.Effect))
            .GroupBy(effect => effect.TargetObject)
            .ToDictionary(group => group.Key, group => group.ToArray());
        visibilityTargets = visibilityEffectsByTarget.Keys.ToArray();
        visibilitySavedFlagsByTarget = visibilityEffectsByTarget.ToDictionary(pair => pair.Key,
            pair => pair.Value.SelectMany(ReferencedSavedFlags).Distinct().ToArray());
        ownerStateEffects = stateEffects
            .GroupBy(effect => effect.Owner.OwnerKey)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Effect).ToArray());
        explicitStateCallTimes = timelineCalls
            .Select(item => (item.Call.Target, Api: CinematicEngineFunctionCatalog.NameFor(item.Call.Selector), item.Time))
            .Where(item => IsExplicitStateTimelineApi(item.Api))
            .GroupBy(item => (item.Target, item.Api))
            .ToDictionary(group => group.Key, group => group.Select(item => item.Time).Order().ToArray());
        morphSnapshots = transfers.Select(transfer => transfer.TargetObject)
            .Distinct().Select(target =>
            {
                FaceMorphPreview preview = target.MorphPreview!;
                return new MorphSnapshot(target, preview, preview.CopyChannelState(), preview.CopyWeights());
            }).ToArray();
    }

    internal IReadOnlyDictionary<int, bool?>? SavedFlagAssumptions
    {
        set
        {
            if (SameAssumptions(savedFlagSnapshot, value)) return;
            savedFlagAssumptions = value;
            savedFlagSnapshot = value?.ToDictionary(pair => pair.Key, pair => pair.Value);
            assumptionCacheKey = savedFlagSnapshot is not { Count: > 0 } assumptions
                ? string.Empty
                : string.Join(';', assumptions.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={(pair.Value is null ? "?" : pair.Value.Value ? "1" : "0")}"));
        }
    }

    internal string[] Diagnostics => transfers.Select(DescribeTransfer)
        .Concat(stateEffects.Select(item =>
            $"{item.Owner.Name} → {item.TargetObject.Name}: {item.Effect.Api} callback state effect "
            + "is previewed only on a resolved route; later EVE state takes precedence"))
        .Concat(rliDisplayPlayback?.Diagnostics ?? [])
        .Concat(materialConstantPlayback?.Diagnostics ?? [])
        .Concat(localAlphaPlayback?.Diagnostics ?? [])
        .Distinct(StringComparer.Ordinal).ToArray();

    internal IReadOnlyDictionary<uint, uint> MaterialConstants => materialConstants;
    internal IReadOnlyDictionary<(uint Material, int Level), byte> MaterialAlphas =>
        localAlphaPlayback?.Current ?? EmptyMaterialAlphas;
    private static IReadOnlyDictionary<(uint Material, int Level), byte> EmptyMaterialAlphas { get; } =
        new Dictionary<(uint Material, int Level), byte>();
    internal IEnumerable<SceneObject> VisibilityTargets => stateEffects
        .Where(effect => IsVisibilityStateEffect(effect.Effect))
        .Select(effect => effect.TargetObject)
        .Distinct();
    internal IEnumerable<float> VisibilityChangeTimes => stateEffects
        .Where(effect => IsVisibilityStateEffect(effect.Effect))
        .SelectMany(effect => (GetWindow(effect.Owner, effect.OwnerObject, effect.EntryPath,
                effect.OutgoingPaths, allowUnboundedStateMutation: true) is { } window
                ? new[] { window.Start } : [])
            .Concat(OwnerTimeline(effect.Owner).Select(item => item.Time))
            .Concat(timelineCalls.Where(item => item.Call.Target == effect.TargetObject.GaoKey
                && IsVisibilityStateTimelineCall(item.Call)).Select(item => item.Time)))
        .Where(float.IsFinite)
        .Distinct();
    internal bool RequiresDenseVisibilitySampling => stateEffects
        .Where(effect => IsVisibilityStateEffect(effect.Effect))
        .Any(effect => effect.Effect.BranchConditions
            .Concat(effect.EntryPath.Steps.SelectMany(step => step.BranchConditions))
            .Concat(effect.OutgoingPaths.SelectMany(path => path.Steps)
                .SelectMany(step => step.BranchConditions))
            .Any(condition => IsTimeDependentPredicate(condition.Expression)));

    internal static string DescribeTransfer(string ownerName, string targetName,
        int sourceChannel, int targetChannel, FaceMorphData? targetMorphData)
    {
        string channelDescription = targetMorphData == null
            || (uint)targetChannel >= (uint)targetMorphData.Channels.Length
                ? "target channel geometry is unresolved"
                : HasValidMorphGroupReference(targetMorphData, targetChannel)
                    ? "target channel has valid morph-group references"
                    : "target channel has no valid morph-group references, so the handoff cannot deform its geometry";
        return $"{ownerName} → {targetName}: source callback channel handoff "
            + $"{sourceChannel}→{targetChannel}; {channelDescription}; callback replacement bounds the handoff, "
            + "unknown gates remain unapplied";
    }

    internal static bool HasValidMorphGroupReference(FaceMorphData? morphData, int channelIndex) =>
        morphData != null && (uint)channelIndex < (uint)morphData.Channels.Length
        && morphData.Channels[channelIndex].GroupReferences.Any(group => group < morphData.Groups.Length);

    private static string DescribeTransfer(TransferRuntime runtime) => DescribeTransfer(
        runtime.Owner.Name, runtime.TargetObject.Name, runtime.Transfer.SourceChannel,
        runtime.Transfer.TargetChannel, runtime.TargetPreview.Data);

    internal static CinematicScriptCallbackPlayback? Create(AnimationClip clip,
        SceneDocument document, WorldData world, ResourceResolver resolver,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> nestedCommands,
        CinematicObjectMorphPlayback? objectMorphPlayback)
    {
        var sceneObjects = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct().ToArray();
        var objectGroups = sceneObjects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        CinematicScriptOwner[] scopedOwners = CinematicScriptScope.ForClip(
            document.CinematicScriptOwners.Values, clip.CinematicWorldKey,
            world.Actors.Select(actor => actor.Root.GaoKey),
            clip.Tracks.Select(track => track.Target),
            CinematicTimelineMessage.ReadTimeline(clip).Select(item => item.Message),
            ownerEventTracks: document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks));
        var scriptOwnersByObjectKey = scopedOwners
            .GroupBy(owner => owner.OwnerKey)
            .ToDictionary(group => group.Key, group => group.DistinctBy(owner => owner.Id).ToArray());
        var timelineCalls = CinematicLiteralCall.ReadTimeline(clip)
            .Concat(nestedCommands)
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time).ToArray();
        var transferRuntimes = new List<TransferRuntime>();
        var stateEffectRuntimes = new List<StateEffectRuntime>();
        CinematicRliDisplayCallbackPlayback? rliDisplayPlayback =
            CinematicRliDisplayCallbackPlayback.Create(document, world, timelineCalls);
        CinematicScriptMaterialConstantPlayback? materialConstantPlayback =
            CinematicScriptMaterialConstantPlayback.Create(document, world, resolver.Materials,
                timelineCalls);
        CinematicScriptLocalAlphaPlayback? localAlphaPlayback =
            CinematicScriptLocalAlphaPlayback.Create(document, world, resolver.Materials,
                timelineCalls);

        foreach (CinematicScriptOwner owner in scopedOwners)
        {
            if (!objectGroups.TryGetValue(owner.OwnerKey, out SceneObject[]? ownerMatches)
                || ownerMatches.Length != 1 || owner.ModelFunctionNames.Length == 0) continue;

            CinematicScriptReachableEngineCall[] calls = CinematicScriptReader.ReachableEngineCalls(owner);
            CinematicScriptChannelTransfer[] ownerTransfers =
                CinematicScriptChannelTransferAnalyzer.Analyze(owner, calls);
            CinematicScriptReachableEngineCall[] stateEffectCalls = calls
                .Where(IsPreviewableStateEffect)
                .ToArray();
            if (ownerTransfers.Length == 0 && stateEffectCalls.Length == 0) continue;

            var callbackPaths = CinematicScriptReader.NestedCallbackPaths(owner)
                .ToDictionary(path => path.FunctionName, StringComparer.Ordinal);
            foreach (CinematicScriptChannelTransfer transfer in ownerTransfers)
            {
                if (!transfer.SourcePathResolved
                    || !callbackPaths.ContainsKey(transfer.FunctionName)
                    || !objectGroups.TryGetValue(transfer.TargetKey, out SceneObject[]? targetMatches)
                    || targetMatches.Length != 1) continue;

                SceneObject ownerObject = ownerMatches[0];
                SceneObject target = targetMatches[0];
                if (!callbackPaths.TryGetValue(transfer.FunctionName,
                        out CinematicScriptFunctionPath? entryPath)) continue;
                CinematicScriptFunctionPath[] outgoingPaths = callbackPaths.Values
                    .Where(path => path.Steps.LastOrDefault()?.FromFunction == transfer.FunctionName)
                    .ToArray();
                if (!objectGroups.TryGetValue(owner.OwnerKey, out SceneObject[]? sourceMatches)
                    || sourceMatches.Length != 1) continue;
                SceneObject source = sourceMatches[0];
                FaceMorphPreview? sourcePreview = source.MorphPreview;
                if (sourcePreview == null || transfer.SourceChannel >= sourcePreview.ChannelCount
                    || !resolver.Geometries.TryGetValue(target.RenderGeometryKey, out Geometry? geometry)) continue;

                FaceMorphPreview? targetPreview = FaceMorphPreview.Get(target, geometry);
                if (targetPreview == null || transfer.TargetChannel >= targetPreview.ChannelCount) continue;
                transferRuntimes.Add(new(owner, ownerObject, source, sourcePreview, target,
                    targetPreview, transfer, entryPath, outgoingPaths));
            }

            if (stateEffectCalls.Length == 0) continue;
            var references = CinematicScriptReader.ReachableSourceReferences(owner);
            foreach (CinematicScriptCallbackStateEffect effect in
                     CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner, stateEffectCalls, references))
            {
                if (effect.ReceiverKey is not { } targetKey
                    || effect.Api is not ("OBJ_FlagInvisibleSet" or "OBJ_FlagInactiveSet" or "OBJDestroy"
                        or "OBJ_CapaSet" or "OBJDrawMaskSet" or "COLSetActivationSet")
                    || !objectGroups.TryGetValue(targetKey, out SceneObject[]? targetMatches)
                    || targetMatches.Length != 1) continue;
                if (effect.Api == "OBJDrawMaskSet"
                    && !HasResolvedVisualDrawMaskSupport(targetKey, scriptOwnersByObjectKey)) continue;

                ushort? initialCollisionActivationMask = null;
                if (effect.Api == "COLSetActivationSet")
                {
                    if (document.CollisionResources?.TryGet(targetMatches[0], out PcCollisionObjectResources collisionResources) != true
                        || collisionResources.Instance?.Instance is not { } collisionInstance)
                        continue;
                    initialCollisionActivationMask = collisionInstance.ActivationMask;
                }

                if (!callbackPaths.TryGetValue(effect.FunctionName,
                        out CinematicScriptFunctionPath? entryPath)) continue;
                CinematicScriptFunctionPath[] outgoingPaths = callbackPaths.Values
                    .Where(path => path.Steps.LastOrDefault()?.FromFunction == effect.FunctionName)
                    .ToArray();
                stateEffectRuntimes.Add(new(owner, ownerMatches[0], targetMatches[0], effect,
                    entryPath, outgoingPaths, initialCollisionActivationMask));
            }
        }

        if (transferRuntimes.Count == 0 && stateEffectRuntimes.Count == 0
            && rliDisplayPlayback == null && materialConstantPlayback == null
            && localAlphaPlayback == null) return null;
        StateEffectRuntime[] orderedEffects = stateEffectRuntimes
            .OrderBy(effect => effect.EntryPath.Steps.Length)
            .ThenBy(effect => effect.Effect.Source, StringComparer.Ordinal)
            .ThenBy(effect => effect.Effect.Line)
            .ToArray();
        return new(timelineCalls, transferRuntimes.ToArray(), orderedEffects,
            objectMorphPlayback, rliDisplayPlayback, materialConstantPlayback,
            localAlphaPlayback);
    }

    internal void Evaluate(float time)
    {
        if (!float.IsFinite(time)) return;
        branchFieldSnapshots.Clear();
        if (time < lastTime) ResetMorphTargets();
        DiagnosticTrace?.Invoke("callback state effects");
        ApplyLinkedStateEffects(time);
        DiagnosticTrace?.Invoke("callback channel transfers");
        ApplyChannelTransfers(time);
        DiagnosticTrace?.Invoke("callback RLI");
        rliDisplayPlayback?.Evaluate(time, savedFlagAssumptions, objectMorphPlayback);
        DiagnosticTrace?.Invoke("callback material constants");
        materialConstants = materialConstantPlayback?.Evaluate(time, savedFlagAssumptions)
            ?? new Dictionary<uint, uint>();
        DiagnosticTrace?.Invoke("callback local alpha");
        localAlphaPlayback?.Evaluate(time, savedFlagAssumptions);
        DiagnosticTrace?.Invoke("callback complete");
        lastTime = time;
    }

    internal void EvaluateVisibilityState(float time)
    {
        if (!float.IsFinite(time)) return;
        branchFieldSnapshots.Clear();
        ApplyLinkedStateEffects(time);
    }

    internal void Reset()
    {
        ResetMorphTargets();
        foreach (SceneObject target in stateEffects
                     .Where(effect => effect.Effect.Api == "COLSetActivationSet")
                     .Select(effect => effect.TargetObject).Distinct())
            target.PreviewCollisionActivationMask = null;
        rliDisplayPlayback?.Clear();
        localAlphaPlayback?.Clear();
        lastTime = 0;
    }

    private void ApplyLinkedStateEffects(float time)
    {
        Dictionary<SceneObject, uint> initialControlFlags = visibilityTargets
            .ToDictionary(target => target,
                target => target.PreviewControlFlags ?? target.ControlFlags);
        var activeEffects = new List<(StateEffectRuntime Runtime, float SampleTime)>();
        foreach (StateEffectRuntime runtime in stateEffects)
        {
            CinematicScriptCallbackWindow? window = GetWindow(runtime.Owner, runtime.OwnerObject,
                runtime.EntryPath, runtime.OutgoingPaths,
                allowUnboundedStateMutation: IsPersistentStateMutation(runtime.Effect.Api));
            if (window == null || time < window.Start) continue;
            float sampleTime = window.End is { } end ? Math.Min(time, end) : time;
            if (HasLaterExplicitStateCall(runtime.TargetObject, runtime.Effect.Api, sampleTime, time))
                continue;

            var receiverFields = runtime.Effect.ReceiverGuardAliases.ToDictionary(
                alias => alias, _ => (long?)1, StringComparer.Ordinal);
            AddCallResultFields(runtime, sampleTime, receiverFields);
            CinematicScriptBranchEvaluation branchResult = EvaluateBranch(
                runtime.Owner, runtime.OwnerObject, runtime.Effect.BranchConditions,
                sampleTime, receiverFields);
            if (branchResult.Value != CinematicScriptBranchValue.True) continue;
            activeEffects.Add((runtime, sampleTime));
        }

        var updatedFlags = new Dictionary<SceneObject, uint>();
        DiagnosticTrace?.Invoke($"state candidates complete ({stateEffects.Length} effects)");
        var updatedCapabilities = new Dictionary<SceneObject, uint>();
        var updatedDrawMasks = new Dictionary<SceneObject, uint>();
        var updatedCollisionActivationMasks = new Dictionary<SceneObject, ushort>();
        foreach (SceneObject target in stateEffects
                     .Where(effect => effect.Effect.Api == "COLSetActivationSet")
                     .Select(effect => effect.TargetObject).Distinct())
            target.PreviewCollisionActivationMask = null;
        foreach ((StateEffectRuntime runtime, _) in activeEffects
                     .OrderBy(effect => effect.SampleTime)
                     .ThenBy(effect => effect.Runtime.EntryPath.Steps.Length)
                     .ThenBy(effect => effect.Runtime.Effect.Source, StringComparer.Ordinal)
                     .ThenBy(effect => effect.Runtime.Effect.Line))
        {
            if (runtime.Effect.Api == "OBJ_CapaSet")
            {
                uint currentCapabilities = updatedCapabilities.GetValueOrDefault(runtime.TargetObject,
                    runtime.TargetObject.PreviewCapabilities ?? runtime.TargetObject.InitialCapabilities);
                if (TryApplyCapabilitySet(currentCapabilities, MutationArguments(runtime.Effect),
                        out uint nextCapabilities))
                    updatedCapabilities[runtime.TargetObject] = nextCapabilities;
                continue;
            }

            if (runtime.Effect.Api == "OBJDrawMaskSet")
            {
                uint currentDrawMask = updatedDrawMasks.GetValueOrDefault(runtime.TargetObject,
                    runtime.TargetObject.PreviewDrawFlags ?? runtime.TargetObject.DrawFlags);
                if (TryApplyDrawMask(currentDrawMask, MutationArguments(runtime.Effect),
                        out uint nextDrawMask))
                    updatedDrawMasks[runtime.TargetObject] = nextDrawMask;
                continue;
            }

            if (runtime.Effect.Api == "COLSetActivationSet")
            {
                if (runtime.InitialCollisionActivationMask is not { } initialMask) continue;
                ushort currentMask = updatedCollisionActivationMasks.GetValueOrDefault(
                    runtime.TargetObject, initialMask);
                if (TryApplyCollisionActivationSet(currentMask, MutationArguments(runtime.Effect),
                        out ushort nextMask))
                    updatedCollisionActivationMasks[runtime.TargetObject] = nextMask;
                continue;
            }

            uint currentFlags = updatedFlags.GetValueOrDefault(runtime.TargetObject,
                runtime.TargetObject.PreviewControlFlags ?? runtime.TargetObject.ControlFlags);
            updatedFlags[runtime.TargetObject] = ApplyStateEffect(currentFlags, runtime.Effect);
        }

        foreach ((SceneObject target, uint flags) in updatedFlags)
            target.PreviewControlFlags = flags;
        foreach ((SceneObject target, uint capabilities) in updatedCapabilities)
            target.PreviewCapabilities = capabilities;
        foreach ((SceneObject target, uint drawMask) in updatedDrawMasks)
            target.PreviewDrawFlags = drawMask;
        foreach ((SceneObject target, ushort activationMask) in updatedCollisionActivationMasks)
            target.PreviewCollisionActivationMask = activationMask;

        DiagnosticTrace?.Invoke("state mutations complete");
        ApplyEquivalentUnknownFlagVisibility(time, initialControlFlags);
        DiagnosticTrace?.Invoke("state flag alternatives complete");
    }

    private void ApplyEquivalentUnknownFlagVisibility(float time,
        IReadOnlyDictionary<SceneObject, uint> initialControlFlags)
    {
        foreach ((SceneObject target, uint initialFlags) in initialControlFlags)
        {
            StateEffectRuntime[] targetEffects = visibilityEffectsByTarget[target];
            int[] unknownFlags = visibilitySavedFlagsByTarget[target]
                .Where(flag => savedFlagAssumptions?.TryGetValue(flag, out bool? value) != true
                    || value == null)
                .ToArray();
            if (unknownFlags.Length != 1) continue;

            int savedFlag = unknownFlags[0];
            if (!TryEvaluateVisibilityOutcome(target, targetEffects, initialFlags, time,
                    savedFlag, false, out uint clearFlagOutcome)
                || !TryEvaluateVisibilityOutcome(target, targetEffects, initialFlags, time,
                    savedFlag, true, out uint setFlagOutcome)
                || !TryResolveEquivalentVisibility(clearFlagOutcome, setFlagOutcome,
                    out bool isVisible)) continue;

            uint currentFlags = target.PreviewControlFlags ?? target.ControlFlags;
            target.PreviewControlFlags = isVisible
                ? currentFlags & ~CinematicObjectState.Invisible
                : currentFlags | CinematicObjectState.Invisible;
        }
    }

    private bool TryEvaluateVisibilityOutcome(SceneObject target, StateEffectRuntime[] targetEffects,
        uint initialFlags, float time, int savedFlag, bool flagIsSet, out uint resultingFlags)
    {
        resultingFlags = initialFlags;
        var assumptions = savedFlagAssumptions?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<int, bool?>();
        assumptions[savedFlag] = flagIsSet;
        IReadOnlyDictionary<int, bool?>? originalAssumptions = savedFlagAssumptions;
        try
        {
            SavedFlagAssumptions = assumptions;
            var activeEffects = new List<(StateEffectRuntime Runtime, float SampleTime)>();
            foreach (StateEffectRuntime runtime in targetEffects)
            {
                CinematicScriptCallbackWindow? window = GetWindow(runtime.Owner,
                    runtime.OwnerObject, runtime.EntryPath, runtime.OutgoingPaths,
                    allowUnboundedStateMutation: IsPersistentStateMutation(runtime.Effect.Api));
                if (window == null)
                {
                    string pathKey = WindowCacheKey(runtime.Owner, runtime.EntryPath, true);
                    if (!unreachablePaths.TryGetValue(pathKey, out bool unreachable))
                    {
                        unreachable = CinematicScriptCallbackWindowResolver.IsPathProvenUnreachable(
                            runtime.Owner, runtime.OwnerObject, runtime.EntryPath,
                            OwnerTimeline(runtime.Owner), savedFlagAssumptions);
                        unreachablePaths[pathKey] = unreachable;
                    }
                    if (unreachable) continue;
                    return false;
                }
                if (time < window.Start) continue;

                float sampleTime = window.End is { } end ? Math.Min(time, end) : time;
                if (HasLaterExplicitStateCall(target, runtime.Effect.Api, sampleTime, time)) continue;

                var receiverFields = runtime.Effect.ReceiverGuardAliases.ToDictionary(
                    alias => alias, _ => (long?)1, StringComparer.Ordinal);
                AddCallResultFields(runtime, sampleTime, receiverFields);
                CinematicScriptBranchEvaluation branchResult = EvaluateBranch(
                    runtime.Owner, runtime.OwnerObject, runtime.Effect.BranchConditions,
                    sampleTime, receiverFields);
                if (branchResult.Value == CinematicScriptBranchValue.Unknown) return false;
                if (branchResult.Value == CinematicScriptBranchValue.True)
                    activeEffects.Add((runtime, sampleTime));
            }

            foreach ((StateEffectRuntime runtime, _) in activeEffects
                         .OrderBy(effect => effect.SampleTime)
                         .ThenBy(effect => effect.Runtime.EntryPath.Steps.Length)
                         .ThenBy(effect => effect.Runtime.Effect.Source, StringComparer.Ordinal)
                         .ThenBy(effect => effect.Runtime.Effect.Line))
            {
                if (!TryApplyVisibilityStateEffect(resultingFlags, runtime.Effect,
                        out uint nextFlags)) return false;
                resultingFlags = nextFlags;
            }
            return true;
        }
        finally
        {
            SavedFlagAssumptions = originalAssumptions;
        }
    }

    internal static bool TryResolveEquivalentVisibility(uint clearFlagFlags,
        uint setFlagFlags, out bool isVisible)
    {
        bool visibleWhenClear = (clearFlagFlags & CinematicObjectState.Invisible) == 0;
        bool visibleWhenSet = (setFlagFlags & CinematicObjectState.Invisible) == 0;
        isVisible = visibleWhenClear;
        return visibleWhenClear == visibleWhenSet;
    }

    private static IEnumerable<int> ReferencedSavedFlags(StateEffectRuntime runtime) =>
        CinematicScriptPredicateEvaluator.SavedFlagIndices(runtime.Effect.BranchConditions
            .Concat(runtime.EntryPath.Steps.SelectMany(step => step.BranchConditions))
            .Concat(runtime.OutgoingPaths.SelectMany(path => path.Steps)
                .SelectMany(step => step.BranchConditions)));

    private static bool IsVisibilityStateEffect(CinematicScriptCallbackStateEffect effect) =>
        effect.Api is "OBJ_FlagInvisibleSet" or "OBJDestroy";

    private static bool IsVisibilityStateTimelineCall(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) is "OBJ_FlagInvisibleSet"
            or "OBJ_FlagInactiveSet" or "OBJDestroy" or "OBJDrawMaskSet";

    private static bool IsTimeDependentPredicate(string expression) =>
        expression.Contains("TIM_gf_MainClock", StringComparison.OrdinalIgnoreCase)
        || expression.Contains("TIM_gf_DeltaTime", StringComparison.OrdinalIgnoreCase)
        || expression.Contains("f_time", StringComparison.OrdinalIgnoreCase)
        || expression.Contains("Timer", StringComparison.OrdinalIgnoreCase);

    private static bool TryApplyVisibilityStateEffect(uint flags,
        CinematicScriptCallbackStateEffect effect, out uint updatedFlags)
    {
        updatedFlags = flags;
        if (effect.Api == "OBJDestroy")
        {
            updatedFlags |= CinematicObjectState.Invisible;
            return true;
        }

        if (effect.Arguments.Length < 2
            || !int.TryParse(effect.Arguments[1], out int value)) return false;
        updatedFlags = value != 0
            ? flags | CinematicObjectState.Invisible
            : flags & ~CinematicObjectState.Invisible;
        return true;
    }

    private void ApplyChannelTransfers(float time)
    {
        foreach (TransferRuntime runtime in transfers)
        {
            CinematicScriptCallbackWindow? window = GetWindow(runtime.Owner, runtime.OwnerObject,
                runtime.EntryPath, runtime.OutgoingPaths);
            if (window == null || time < window.Start) continue;
            float sampleTime = window.End is { } end ? Math.Min(time, end) : time;

            var receiverFields = runtime.Transfer.ReceiverGuardAliases.ToDictionary(
                alias => alias, _ => (long?)1, StringComparer.Ordinal);
            CinematicScriptBranchEvaluation branchResult = EvaluateBranch(
                runtime.Owner, runtime.OwnerObject, runtime.Transfer.BranchConditions,
                sampleTime, receiverFields);
            if (branchResult.Value != CinematicScriptBranchValue.True) continue;

            FaceMorphChannelState[]? sourceChannels = ReadChannelState(runtime.SourcePreview);
            FaceMorphChannelState? sampledSource = objectMorphPlayback?.SampleChannelState(
                runtime.SourceObject, runtime.Transfer.SourceChannel, sampleTime);
            FaceMorphChannelState[]? targetChannels = ReadChannelState(runtime.TargetPreview);
            if (sourceChannels == null || targetChannels == null
                || runtime.Transfer.SourceChannel >= sourceChannels.Length
                || runtime.Transfer.TargetChannel >= targetChannels.Length) continue;

            FaceMorphChannelState sourceState = sampledSource
                ?? sourceChannels[runtime.Transfer.SourceChannel];
            FaceMorphChannelState targetState = targetChannels[runtime.Transfer.TargetChannel];
            if (!sourceState.IsValid || !targetState.IsValid) continue;
            runtime.TargetPreview.ApplyCinematicChannelState(runtime.Transfer.TargetChannel,
                targetState.Weight, sourceState.Progression);
            if (runtime.Transfer.ClearsInvisibleFlag
                && runtime.TargetObject.PreviewControlFlags is { } flags)
                runtime.TargetObject.PreviewControlFlags = flags & ~CinematicObjectState.Invisible;
        }
    }

    private static FaceMorphChannelState[]? ReadChannelState(FaceMorphPreview preview)
    {
        if (preview.ChannelCount == 0) return null;
        return preview.CopyChannelState()
            ?? Enumerable.Range(0, preview.ChannelCount).Select(preview.GetChannel).ToArray();
    }

    private CinematicScriptCallbackWindow? GetWindow(CinematicScriptOwner owner,
        SceneObject ownerObject, CinematicScriptFunctionPath entryPath,
        CinematicScriptFunctionPath[] outgoingPaths, bool allowUnboundedStateMutation = false)
    {
        string key = WindowCacheKey(owner, entryPath, allowUnboundedStateMutation);
        if (callbackWindows.TryGetValue(key, out CinematicScriptCallbackWindow? cached)) return cached;
        CinematicScriptCallbackWindow? resolved = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, entryPath.FunctionName, entryPath.Steps, [],
            new Dictionary<string, long?>(StringComparer.Ordinal), outgoingPaths,
            OwnerTimeline(owner), savedFlagAssumptions, allowUnboundedStateMutation);
        callbackWindows[key] = resolved;
        return resolved;
    }

    private string WindowCacheKey(CinematicScriptOwner owner, CinematicScriptFunctionPath entryPath,
        bool allowUnboundedStateMutation) =>
        $"{owner.WorldKey:X8}:{owner.OwnerKey:X8}:{entryPath.FunctionName}:{allowUnboundedStateMutation}:{assumptionCacheKey}";

    private IReadOnlyList<(float Time, CinematicLiteralCall Call)> OwnerTimeline(
        CinematicScriptOwner owner)
    {
        if (!ownerTimelineCalls.TryGetValue(owner.OwnerKey, out var calls))
        {
            calls = timelineCalls.Where(item => item.Call.Target == owner.OwnerKey).ToArray();
            ownerTimelineCalls[owner.OwnerKey] = calls;
        }
        return calls;
    }

    private void AddCallResultFields(StateEffectRuntime runtime, float sampleTime,
        Dictionary<string, long?> fields)
    {
        CinematicScriptCallbackStateEffect[] ownerEffects = ownerStateEffects
            .GetValueOrDefault(runtime.Owner.OwnerKey, []);
        foreach ((string name, long? value) in CinematicScriptCallResultEvaluator.Resolve(
                     runtime.Owner, runtime.OwnerObject, runtime.Effect, ownerEffects,
                     sampleTime, OwnerTimeline(runtime.Owner), savedFlagAssumptions))
            fields[name] = value;
    }

    private bool HasLaterExplicitStateCall(SceneObject target, string api,
        float sampleTime, float playhead) => HasLaterCall(target.GaoKey, api, sampleTime, playhead)
        || (api != "OBJDestroy" && HasLaterCall(target.GaoKey, "OBJDestroy", sampleTime, playhead));

    private bool HasLaterCall(uint targetKey, string api, float sampleTime, float playhead)
    {
        if (!explicitStateCallTimes.TryGetValue((targetKey, api), out float[]? times)) return false;
        int firstLaterIndex = Array.BinarySearch(times, sampleTime);
        if (firstLaterIndex < 0) firstLaterIndex = ~firstLaterIndex;
        else
        {
            while (firstLaterIndex < times.Length && times[firstLaterIndex] <= sampleTime)
                firstLaterIndex++;
        }
        return firstLaterIndex < times.Length && times[firstLaterIndex] <= playhead;
    }

    private static bool IsExplicitStateTimelineApi(string api) => api is "OBJ_FlagInvisibleSet"
        or "OBJ_FlagInactiveSet" or "OBJDestroy" or "OBJDrawMaskSet" or "OBJ_CapaSet"
        or "COLSetActivationSet";

    private static bool SameAssumptions(Dictionary<int, bool?>? current,
        IReadOnlyDictionary<int, bool?>? next) => current == null
        ? next == null || next.Count == 0
        : next != null && current.Count == next.Count
            && current.All(pair => next.TryGetValue(pair.Key, out bool? value) && value == pair.Value);

    private static bool IsPreviewableStateEffect(CinematicScriptReachableEngineCall call) =>
        call.Api is "OBJ_FlagInvisibleSet" or "OBJ_FlagInactiveSet" or "OBJDestroy"
            or "OBJ_CapaSet" or "OBJDrawMaskSet" or "COLSetActivationSet";

    private static bool IsPersistentStateMutation(string api) =>
        api is "OBJ_FlagInvisibleSet" or "OBJ_FlagInactiveSet" or "OBJDestroy"
            or "OBJ_CapaSet" or "OBJDrawMaskSet" or "COLSetActivationSet";

    internal static string[] MutationArguments(CinematicScriptCallbackStateEffect effect)
    {
        bool receiverIsImplicit = effect.ReceiverEvidence.StartsWith(
            "Iris CURRENT API variant", StringComparison.Ordinal);
        return receiverIsImplicit || effect.Arguments.Length == 0
            ? effect.Arguments
            : effect.Arguments[1..];
    }

    internal static bool TryApplyCollisionActivationSet(ushort currentMask,
        string[] arguments, out ushort updatedMask)
    {
        updatedMask = currentMask;
        if (arguments.Length < 2 || !TryParseInteger(arguments[0], out int onMask)
            || !TryParseInteger(arguments[1], out int offMask)) return false;

        updatedMask = unchecked((ushort)((currentMask & ~offMask) | onMask));
        return true;
    }

    internal static bool HasResolvedVisualDrawMaskSupport(uint targetKey,
        IReadOnlyDictionary<uint, CinematicScriptOwner[]> scriptOwnersByObjectKey)
    {
        const uint visualObjectFlag = 0x00004000;
        return scriptOwnersByObjectKey.TryGetValue(targetKey, out CinematicScriptOwner[]? targetOwners)
            && targetOwners.Length == 1
            && (targetOwners[0].SerializedGaoFlags & visualObjectFlag) != 0;
    }

    internal static bool TryApplyCapabilitySet(uint currentCapabilities, string[] arguments,
        out uint updatedCapabilities)
    {
        updatedCapabilities = currentCapabilities & ushort.MaxValue;
        if (arguments.Length < 2 || !TryParseInteger(arguments[0], out int setMask)
            || !TryParseInteger(arguments[1], out int removeMask)) return false;

        ushort set = unchecked((ushort)setMask);
        ushort remove = unchecked((ushort)removeMask);
        updatedCapabilities = (uint)(((ushort)currentCapabilities & ~remove) | set);
        return true;
    }

    internal static bool TryApplyDrawMask(uint currentDrawMask, string[] arguments,
        out uint updatedDrawMask)
    {
        updatedDrawMask = currentDrawMask;
        if (arguments.Length < 2 || !TryParseInteger(arguments[0], out int setMask)
            || !TryParseInteger(arguments[1], out int removeMask)) return false;

        updatedDrawMask = (currentDrawMask | unchecked((uint)setMask)) & ~unchecked((uint)removeMask);
        return true;
    }

    private static bool TryParseInteger(string text, out int value)
    {
        string normalized = text.Trim().TrimEnd('u', 'U', 'l', 'L');
        return normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(normalized.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            : int.TryParse(normalized, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static uint ApplyStateEffect(uint flags, CinematicScriptCallbackStateEffect effect)
    {
        if (effect.Api == "OBJDestroy")
            return flags | CinematicObjectState.Invisible | CinematicObjectState.Inactive;

        string[] arguments = MutationArguments(effect);
        if (arguments.Length == 0 || !int.TryParse(arguments[0], out int value)) return flags;
        uint mask = effect.Api == "OBJ_FlagInvisibleSet"
            ? CinematicObjectState.Invisible
            : CinematicObjectState.Inactive;
        return value != 0 ? flags | mask : flags & ~mask;
    }

    private void ResetMorphTargets()
    {
        foreach (MorphSnapshot snapshot in morphSnapshots)
        {
            snapshot.Preview.Reset();
            if (snapshot.Channels != null)
                snapshot.Preview.RestoreChannelState(snapshot.Channels);
            else
                for (int index = 0; index < snapshot.Weights.Length; index++)
                    if (snapshot.Weights[index] != 0)
                        snapshot.Preview.SetWeight(index, snapshot.Weights[index]);
        }
    }
}
