using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Previews source-reachable MATSetConstant callback writes when their callback
/// route and receiver are unique. It evaluates source-derived colors only when
/// their value flow and runtime inputs are unambiguous.
/// </summary>
internal sealed class CinematicScriptMaterialConstantPlayback
{
    private sealed record Write(CinematicScriptOwner Owner, SceneObject OwnerObject,
        CinematicScriptReachableEngineCall Call,
        CinematicScriptFunctionPath EntryPath, CinematicScriptFunctionPath[] OutgoingPaths,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> Timeline,
        CinematicScriptRliValueFlowAssignment[] ValueAssignments,
        CinematicScriptReachableOvaFieldAccess[] OvaFieldAccesses,
        CinematicScriptReachableEngineCall[] EngineCalls,
        uint MaterialKey, string ColorExpression, uint? StaticColor);

    private static readonly Regex Identifier = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private readonly Write[] writes;
    private readonly Dictionary<uint, uint> evaluated = [];

    private CinematicScriptMaterialConstantPlayback(Write[] writes) => this.writes = writes;

    internal string[] Diagnostics => writes
        .GroupBy(write => write.Owner.OwnerKey)
        .Select(group =>
        {
            int literalCount = group.Count(write => write.StaticColor.HasValue);
            int expressionCount = group.Count(write => !write.StaticColor.HasValue
                && Identifier.IsMatch(write.ColorExpression));
            int unsupportedCount = group.Count() - literalCount - expressionCount;
            return $"{group.First().Owner.Name}: {literalCount} literal MATSetConstant callback write(s); {expressionCount} identifier-backed color expression(s) sampled when source inputs resolve; {unsupportedCount} unsupported expression(s) withheld";
        })
        .ToArray();

    internal IReadOnlyDictionary<uint, uint> Evaluate(float playhead,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        evaluated.Clear();
        foreach (IGrouping<uint, Write> materialWrites in writes.GroupBy(write => write.MaterialKey))
        {
            var activeWrites = new List<(Write Write, float Start)>();
            foreach (Write write in materialWrites)
            {
                CinematicScriptCallbackWindow? window = CinematicScriptCallbackWindowResolver.Resolve(
                    write.Owner, write.OwnerObject, write.Call.FunctionName, write.EntryPath.Steps,
                    [], new Dictionary<string, long?>(), write.OutgoingPaths, write.Timeline,
                    savedFlags, allowUnboundedStateMutation: true);
                if (window is not { } resolvedWindow || playhead < resolvedWindow.Start) continue;

                uint? originalCapabilities = write.OwnerObject.PreviewCapabilities;
                try
                {
                    write.OwnerObject.PreviewCapabilities =
                        CinematicScriptCallbackWindowResolver.CapabilitiesAt(write.OwnerObject,
                            resolvedWindow.Start, write.Timeline);
                    CinematicScriptBranchEvaluation branch =
                        CinematicScriptRuntimeBranchEvaluator.Evaluate(write.Owner,
                            write.OwnerObject, write.Call.BranchConditions, resolvedWindow.Start,
                            write.Timeline, savedFlags);
                    if (branch.Value != CinematicScriptBranchValue.True) continue;
                    activeWrites.Add((write, resolvedWindow.Start));
                }
                finally
                {
                    write.OwnerObject.PreviewCapabilities = originalCapabilities;
                }
            }

            if (activeWrites.Count == 0) continue;
            float latestStart = activeWrites.Max(item => item.Start);
            var latestWrites = activeWrites.Where(item => item.Start == latestStart)
                .OrderBy(item => item.Write.Call.FunctionName, StringComparer.Ordinal)
                .ThenBy(item => item.Write.Call.Line)
                .ToArray();

            // Multiple callback functions may write the same material at one
            // reconstructed start time. Without dispatch order, their result is
            // ambiguous and must not depend on source/catalog enumeration order.
            uint?[] colors = latestWrites.Select(item => ResolveColor(item.Write,
                    item.Start, playhead, savedFlags))
                .ToArray();
            if (colors.Distinct().Count() != 1) continue;
            if (colors[0] is { } color) evaluated[materialWrites.Key] = color;
        }

        return evaluated;
    }

    internal string[] Trace(float playhead, IReadOnlyDictionary<int, bool?>? savedFlags,
        uint materialKey)
    {
        var trace = new List<string>();
        foreach (Write write in writes.Where(write => write.MaterialKey == materialKey))
        {
            Dictionary<string, double?> messageFields = CinematicScriptMessageScalarStateResolver.Resolve(
                write.Owner, write.OwnerObject, write.Timeline, playhead, savedFlags);
            string[] queuedMessages = write.Timeline
                .Where(item => item.Time <= playhead
                    && CinematicScriptRuntimeBranchEvaluator.TryReadQueuedMessageForOwner(
                        item.Call, write.Owner.OwnerKey, out _))
                .Select(item => $"{item.Time:F3}s/{CinematicEngineFunctionCatalog.NameFor(item.Call.Selector)}")
                .ToArray();
            string messageState = messageFields.Count == 0 ? "unresolved" : string.Join(",",
                messageFields.Select(field => $"{field.Key}={field.Value?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"));
            trace.Add($"{write.Owner.Name}:{write.Call.FunctionName} source message state={messageState}; "
                + $"queued calls=[{string.Join(",", queuedMessages)}]");
            string callbackPath = string.Join(" -> ", write.EntryPath.Steps.Select(step =>
                $"{step.FromFunction}>{step.ToFunction}@{step.AssignmentLine?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));
            CinematicScriptCallbackWindow? window = CinematicScriptCallbackWindowResolver.Resolve(
                write.Owner, write.OwnerObject, write.Call.FunctionName, write.EntryPath.Steps,
                [], new Dictionary<string, long?>(), write.OutgoingPaths, write.Timeline,
                savedFlags, allowUnboundedStateMutation: true);
            if (window is not { } resolvedWindow)
            {
                trace.Add($"{write.Owner.Name}:{write.Call.FunctionName}:{write.Call.Line} route unresolved");
                continue;
            }
            if (playhead < resolvedWindow.Start)
            {
                trace.Add($"{write.Owner.Name}:{write.Call.FunctionName}:{write.Call.Line} starts {resolvedWindow.Start:F3}s later");
                continue;
            }

            uint? savedTraceCapabilities = write.OwnerObject.PreviewCapabilities;
            write.OwnerObject.PreviewCapabilities = CinematicScriptCallbackWindowResolver
                .CapabilitiesAt(write.OwnerObject, resolvedWindow.Start, write.Timeline);
            Dictionary<string, double?> runtimeInputs = RuntimeInputs(write,
                resolvedWindow.Start, playhead, savedFlags);
            trace.Add($"{write.Owner.Name}:{write.Call.FunctionName} callback path=[{callbackPath}]; "
                + $"runtime inputs=[{string.Join(",", runtimeInputs.Select(input =>
                    $"{input.Key}={input.Value?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"))}]");
            write.OwnerObject.PreviewCapabilities = savedTraceCapabilities;

            uint? originalCapabilities = write.OwnerObject.PreviewCapabilities;
            try
            {
                write.OwnerObject.PreviewCapabilities = CinematicScriptCallbackWindowResolver
                    .CapabilitiesAt(write.OwnerObject, resolvedWindow.Start, write.Timeline);
                CinematicScriptBranchEvaluation branch = CinematicScriptRuntimeBranchEvaluator
                    .Evaluate(write.Owner, write.OwnerObject, write.Call.BranchConditions,
                        resolvedWindow.Start, write.Timeline, savedFlags);
                uint? color = branch.Value == CinematicScriptBranchValue.True
                    ? ResolveColor(write, resolvedWindow.Start, playhead, savedFlags) : null;
                string interval = resolvedWindow.End is { } end
                    ? $"[{resolvedWindow.Start:F3},{end:F3}]" : $"[{resolvedWindow.Start:F3},open)";
                trace.Add($"{write.Owner.Name}:{write.Call.FunctionName}:{write.Call.Line} "
                    + $"window={interval} branch={branch.Value} color="
                    + (color is { } packed ? $"0x{packed:X8}" : "unresolved"));
            }
            finally
            {
                write.OwnerObject.PreviewCapabilities = originalCapabilities;
            }
        }
        return trace.ToArray();
    }

    internal static CinematicScriptMaterialConstantPlayback? Create(SceneDocument document,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);

        SceneObject[] sceneObjects = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct().ToArray();
        var objectGroups = sceneObjects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var writes = new List<Write>();

        foreach (CinematicScriptOwner owner in document.CinematicScriptOwners.Values
                     .Where(owner => owner.WorldKey == world.Key))
        {
            if (!objectGroups.TryGetValue(owner.OwnerKey, out SceneObject[]? ownerMatches)
                || ownerMatches.Length != 1 || owner.ModelFunctionNames.Length == 0) continue;

            CinematicScriptFunctionPath[] nestedPaths = CinematicScriptReader.NestedCallbackPaths(owner);
            var paths = nestedPaths.ToDictionary(path => path.FunctionName, StringComparer.Ordinal);
            foreach (string callbackName in owner.CallbackNames
                         .Where(owner.ModelFunctionNames.Contains).Distinct(StringComparer.Ordinal))
                paths.TryAdd(callbackName, new(callbackName, callbackName, []));

            CinematicScriptFunctionPath[] allPaths = paths.Values.ToArray();
            CinematicScriptRliValueFlowAssignment[] valueAssignments =
                CinematicScriptReader.ReachableRliValueFlowAssignments(owner);
            CinematicScriptReachableOvaFieldAccess[] ovaFieldAccesses =
                CinematicScriptReader.ReachableOvaFieldAccesses(owner);
            CinematicScriptReachableEngineCall[] engineCalls =
                CinematicScriptReader.ReachableEngineCalls(owner);
            CinematicScriptReachableEngineCall[] setterCalls = engineCalls
                    .Where(call => call.Api == "MATSetConstant" && call.Arguments.Length >= 3)
                    .ToArray();

            foreach (CinematicScriptReachableEngineCall call in setterCalls)
            {
                if (!paths.TryGetValue(call.FunctionName, out CinematicScriptFunctionPath? entryPath)
                    || !TryResolveReceiver(call, owner, sceneObjects, out SceneObject? receiver)
                    || receiver == null
                    || !TryParseSelector(call.Arguments[2], out uint selector)
                    || !TryResolveMaterialKey(receiver, selector, world, materials, out uint materialKey))
                    continue;

                uint? color = TryParsePackedColor(call.Arguments[1], out uint packedColor)
                    ? packedColor : null;
                CinematicScriptFunctionPath[] outgoingPaths = allPaths
                    .Where(path => path.Steps.LastOrDefault()?.FromFunction == call.FunctionName)
                    .ToArray();
                var ownerTimeline = timelineCalls
                    .Where(item => item.Call.Target == owner.OwnerKey).ToArray();
                writes.Add(new(owner, ownerMatches[0], call, entryPath,
                    outgoingPaths, ownerTimeline, valueAssignments, ovaFieldAccesses, engineCalls,
                    materialKey, call.Arguments[1], color));
            }
        }

        return writes.Count == 0 ? null : new(writes.ToArray());
    }

    private uint? ResolveColor(Write write, float callbackStart, float playhead,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        if (write.StaticColor is { } staticColor) return staticColor;
        if (!Identifier.IsMatch(write.ColorExpression)) return null;

        Dictionary<string, double?> runtimeInputs = RuntimeInputs(write, callbackStart, playhead,
            savedFlags);
        Dictionary<string, double?> messageFields = CinematicScriptMessageScalarStateResolver.Resolve(
            write.Owner, write.OwnerObject, write.Timeline, playhead, savedFlags);
        if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(write.Owner,
                write.OwnerObject, write.Call.FunctionName, write.ValueAssignments,
                write.EngineCalls, playhead, write.Timeline, _ => null, savedFlags,
                messageFields, runtimeInputs, out Dictionary<string, double?> values)
            || values.GetValueOrDefault(write.ColorExpression) is not { } packedValue
            || !TryConvertPackedColor(packedValue, out uint resolvedColor))
            return null;
        return resolvedColor;
    }

    private static Dictionary<string, double?> RuntimeInputs(Write write, float callbackStart,
        float playhead, IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        var runtimeInputs = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            // Cinematic playback time is relative to the clip's zero point. Iris
            // source computes elapsed AI time as MainClock minus LockTime.
            ["TIM_gf_MainClock"] = playhead,
            ["TIM_gf_LockTime"] = 0
        };
        CinematicScriptCallbackPathStep? entryTransition = write.EntryPath.Steps.LastOrDefault();
        if (entryTransition == null
            || !CinematicScriptCallbackEntryTimeResolver.TryResolveDispatchCallLine(
                entryTransition, write.EngineCalls, out int dispatchLine))
            return runtimeInputs;

        Dictionary<string, double?> entryTimes = CinematicScriptCallbackEntryTimeResolver.Resolve(
            write.OvaFieldAccesses, write.Call.FunctionName,
            entryTransition.FromFunction, dispatchLine, callbackStart,
            access => CinematicScriptRuntimeBranchEvaluator.Evaluate(
                write.Owner, write.OwnerObject, access.BranchConditions, callbackStart,
                write.Timeline, savedFlags).Value == CinematicScriptBranchValue.True);
        foreach ((string fieldName, double? entryTime) in entryTimes)
            runtimeInputs[fieldName] = entryTime;

        return runtimeInputs;
    }

    private static bool TryConvertPackedColor(double value, out uint packed)
    {
        packed = 0;
        if (!double.IsFinite(value) || value != Math.Truncate(value)) return false;
        if (value is >= int.MinValue and < 0)
        {
            packed = unchecked((uint)(int)value);
            return true;
        }
        if (value < uint.MinValue || value > uint.MaxValue) return false;
        packed = (uint)value;
        return true;
    }

    internal static bool TryResolveReceiver(CinematicScriptReachableEngineCall call,
        CinematicScriptOwner owner, IReadOnlyCollection<SceneObject> objects,
        out SceneObject? receiver)
    {
        receiver = null;
        if (call.Arguments[0] == "AI_gpst_CurrentGameObject")
        {
            SceneObject[] currentObjects = objects.Where(item => item.GaoKey == owner.OwnerKey).ToArray();
            if (currentObjects.Length != 1) return false;
            receiver = currentObjects[0];
            return true;
        }

        CinematicScriptReachableSourceReference[] references =
            CinematicScriptArgumentBindingResolver.ResolveArgumentObjectReferences(call, 0,
                CinematicScriptReader.ReachableSourceReferences(owner));
        uint[] referenceKeys = references.Select(reference => reference.Key).Distinct().ToArray();
        uint[] ownerFieldKeys = CinematicScriptArgumentBindingResolver
            .ResolveArgumentOwnerFields(call, 0, owner)
            .Where(binding => binding.ObjectKey.HasValue)
            .Select(binding => binding.ObjectKey!.Value)
            .Distinct().ToArray();
        uint[] candidateKeys = referenceKeys.Concat(ownerFieldKeys).Distinct().ToArray();
        if (candidateKeys.Length != 1) return false;

        SceneObject[] matches = objects.Where(item => item.GaoKey == candidateKeys[0]).ToArray();
        if (matches.Length != 1) return false;
        receiver = matches[0];
        return true;
    }

    private static bool TryResolveMaterialKey(SceneObject receiver, uint selector,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials, out uint materialKey)
    {
        if ((selector & 0xff000000) != 0)
        {
            materialKey = selector;
            return materials.TryGetValue(materialKey, out ResolvedMaterial? explicitMaterial)
                && explicitMaterial.Type is 3 or 5;
        }

        SceneObject[] receiverMatches = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Where(item => item.GaoKey == receiver.GaoKey).ToArray();
        if (receiverMatches.Length != 1
            || !materials.TryGetValue(receiverMatches[0].MaterialKey, out ResolvedMaterial? root))
        {
            materialKey = 0;
            return false;
        }

        if (root.Type != 4)
        {
            materialKey = root.Id.Key;
            return selector == 0 && root.Type is 3 or 5;
        }

        if (root.SubMaterialKeys.Count == 0)
        {
            materialKey = 0;
            return false;
        }

        int childIndex = (int)(selector % (uint)root.SubMaterialKeys.Count);
        materialKey = root.SubMaterialKeys[childIndex];
        return materials.TryGetValue(materialKey, out ResolvedMaterial? child)
            && child.Type is 3 or 5;
    }

    private static bool TryParseSelector(string expression, out uint selector) =>
        TryParsePackedColor(expression, out selector);

    private static bool TryParsePackedColor(string expression, out uint packed)
    {
        string normalized = expression.Trim().TrimEnd('u', 'U', 'l', 'L');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out packed);
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int signed))
        {
            packed = unchecked((uint)signed);
            return true;
        }
        if (uint.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out packed)) return true;
        packed = 0;
        return false;
    }
}
