using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Samples source-reachable SetLocalAlpha callback writes when the owner route,
/// material selector, layer, branch and alpha value can all be resolved.
/// </summary>
internal sealed class CinematicScriptLocalAlphaPlayback
{
    private sealed record Write(CinematicScriptOwner Owner, SceneObject OwnerObject,
        CinematicScriptReachableEngineCall Call, uint MaterialKey, int Level,
        string AlphaExpression, CinematicScriptFunctionPath EntryPath,
        CinematicScriptFunctionPath[] OutgoingPaths,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> Timeline,
        CinematicScriptRliValueFlowAssignment[] ValueAssignments,
        CinematicScriptReachableOvaFieldAccess[] OvaFieldAccesses,
        CinematicScriptReachableEngineCall[] EngineCalls);

    private static readonly Regex MemberReference = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex VectorComponentReference = new(
        @"\b(?<vector>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<component>[xyz])\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CurrentVectorOutput = new(
        @"^\s*&\s*(?<vector>[A-Za-z_][A-Za-z0-9_]*)\s*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Cast = new(
        @"\(\s*(?:(?:unsigned|signed)\s+)?(?:char|short|int|long|float|double)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly Write[] writes;
    private readonly WorldData world;
    private readonly IReadOnlyDictionary<uint, ResolvedMaterial> materials;
    private readonly IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls;
    private readonly Dictionary<(uint Material, int Level), byte> evaluated = [];

    private CinematicScriptLocalAlphaPlayback(Write[] writes, WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        this.writes = writes;
        this.world = world;
        this.materials = materials;
        this.timelineCalls = timelineCalls;
    }

    internal IReadOnlyDictionary<(uint Material, int Level), byte> Current => evaluated;
    internal void Clear() => evaluated.Clear();

    internal IReadOnlyDictionary<(uint Material, int Level), byte> Evaluate(float playhead,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        evaluated.Clear();
        if (!float.IsFinite(playhead)) return evaluated;

        foreach (IGrouping<(uint Material, int Level), Write> materialWrites in
                 writes.GroupBy(write => (write.MaterialKey, write.Level)))
        {
            var activeWrites = new List<(Write Write, float Start, byte Alpha)>();
            foreach (Write write in materialWrites)
            {
                CinematicScriptCallbackWindow? window = ResolveWindow(write, savedFlags);
                if (window is not { } callbackWindow || playhead < callbackWindow.Start
                    || callbackWindow.End is { } end && playhead > end
                    || HasLaterTimelineAlpha(write, callbackWindow.Start, playhead))
                    continue;

                uint? originalCapabilities = write.OwnerObject.PreviewCapabilities;
                try
                {
                    write.OwnerObject.PreviewCapabilities =
                        CinematicScriptCallbackWindowResolver.CapabilitiesAt(
                            write.OwnerObject, callbackWindow.Start, write.Timeline);
                    CinematicScriptBranchEvaluation branch = CinematicScriptRuntimeBranchEvaluator
                        .Evaluate(write.Owner, write.OwnerObject, write.Call.BranchConditions,
                            playhead, write.Timeline, savedFlags);
                    if (branch.Value != CinematicScriptBranchValue.True
                        || !TryResolveAlpha(write, callbackWindow.Start, playhead, savedFlags,
                            out byte alpha))
                        continue;
                    activeWrites.Add((write, callbackWindow.Start, alpha));
                }
                finally
                {
                    write.OwnerObject.PreviewCapabilities = originalCapabilities;
                }
            }

            if (activeWrites.Count == 0) continue;
            float latestStart = activeWrites.Max(write => write.Start);
            byte[] latestValues = activeWrites.Where(write => write.Start == latestStart)
                .Select(write => write.Alpha).Distinct().ToArray();
            if (latestValues.Length == 1)
                evaluated[materialWrites.Key] = latestValues[0];
        }

        return evaluated;
    }

    internal string[] Diagnostics => writes
        .GroupBy(write => write.Owner.OwnerKey)
        .Select(group => $"{group.First().Owner.Name}: {group.Count()} source-reachable SetLocalAlpha callback write(s); only uniquely resolved routes and values are previewed")
        .ToArray();

    internal static CinematicScriptLocalAlphaPlayback? Create(SceneDocument document,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        SceneObject[] sceneObjects = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct().ToArray();
        var writes = new List<Write>();

        foreach (CinematicScriptOwner owner in document.CinematicScriptOwners.Values
                     .Where(owner => owner.WorldKey == world.Key && owner.ModelFunctionNames.Length > 0))
        {
            SceneObject[] ownerMatches = sceneObjects.Where(item => item.GaoKey == owner.OwnerKey).ToArray();
            if (ownerMatches.Length != 1) continue;

            CinematicScriptReachableEngineCall[] engineCalls =
                CinematicScriptReader.ReachableEngineCalls(owner);
            CinematicScriptReachableEngineCall[] alphaCalls = engineCalls
                .Where(call => call.Api == "SetLocalAlpha" && call.Arguments.Length >= 4)
                .ToArray();
            if (alphaCalls.Length == 0) continue;

            CinematicScriptFunctionPath[] paths = CinematicScriptReader.NestedCallbackPaths(owner);
            var pathsByName = paths.ToDictionary(path => path.FunctionName, StringComparer.Ordinal);
            foreach (string callbackName in owner.CallbackNames
                         .Where(owner.ModelFunctionNames.Contains).Distinct(StringComparer.Ordinal))
                pathsByName.TryAdd(callbackName, new(callbackName, callbackName, []));
            CinematicScriptFunctionPath[] allPaths = pathsByName.Values.ToArray();
            CinematicScriptRliValueFlowAssignment[] assignments =
                CinematicScriptReader.ReachableRliValueFlowAssignments(owner);
            CinematicScriptReachableOvaFieldAccess[] ovaAccesses =
                CinematicScriptReader.ReachableOvaFieldAccesses(owner);
            var ownerTimeline = timelineCalls.Where(item => item.Call.Target == owner.OwnerKey).ToArray();

            foreach (CinematicScriptReachableEngineCall call in alphaCalls)
            {
                if (!pathsByName.TryGetValue(call.FunctionName,
                        out CinematicScriptFunctionPath? entryPath)
                    || !int.TryParse(call.Arguments[2], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int level)
                    || level < 0
                    || !CinematicScriptMaterialConstantPlayback.TryResolveReceiver(call, owner,
                        sceneObjects, out SceneObject? receiver)
                    || receiver == null
                    || !CinematicMaterialState.TryParseMaterialSelector(call.Arguments[1],
                        out uint selector))
                    continue;

                uint materialKey = CinematicMaterialState.ResolveMaterialKey(selector,
                    receiver.GaoKey, world, materials);
                if (!materials.TryGetValue(materialKey, out ResolvedMaterial? material)
                    || material.Type is not (3 or 5)
                    || level >= JadeMaterialLayer.Read(material.SourceBytes).Count)
                    continue;

                CinematicScriptFunctionPath[] outgoingPaths = allPaths
                    .Where(path => path.Steps.LastOrDefault()?.FromFunction == call.FunctionName)
                    .ToArray();
                CinematicScriptRliValueFlowAssignment[] callValueAssignments =
                    ValueFlowAssignmentsForCall(call, assignments);
                writes.Add(new(owner, ownerMatches[0], call, materialKey, level,
                    call.Arguments[3], entryPath, outgoingPaths, ownerTimeline,
                    callValueAssignments, ovaAccesses, engineCalls));
            }
        }

        return writes.Count == 0 ? null : new(writes.ToArray(), world, materials, timelineCalls);
    }

    internal static CinematicScriptRliValueFlowAssignment[] ValueFlowAssignmentsForCall(
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptRliValueFlowAssignment> indexedAssignments)
    {
        CinematicScriptRliValueFlowAssignment[] callAssignments = call.ArgumentAssignments
            .Where(assignment => assignment.IsUnique && assignment.Line <= call.Line)
            .Select(assignment => new CinematicScriptRliValueFlowAssignment(call.FunctionName,
                assignment.Variable, assignment.Expression, assignment.BranchConditions ?? [], call.Source,
                assignment.Line, call.CallbackPath))
            .Concat((call.ArgumentAssignmentAlternatives ?? [])
                .Where(alternative => alternative.Line <= call.Line)
                .Select(alternative => new CinematicScriptRliValueFlowAssignment(call.FunctionName,
                    alternative.Variable, alternative.Expression, alternative.BranchConditions,
                    call.Source, alternative.Line, call.CallbackPath)))
            .ToArray();
        return indexedAssignments.Where(assignment => assignment.FunctionName == call.FunctionName)
            .Concat(callAssignments)
            .DistinctBy(assignment => (assignment.FunctionName, assignment.Variable,
                assignment.Line, assignment.Expression,
                Branches: string.Join("\u001f", assignment.BranchConditions.Select(branch =>
                    $"{branch.Kind}:{branch.Expression}:{branch.Line}"))))
            .OrderBy(assignment => assignment.Line)
            .ToArray();
    }

    private static CinematicScriptCallbackWindow? ResolveWindow(Write write,
        IReadOnlyDictionary<int, bool?>? savedFlags) =>
        CinematicScriptCallbackWindowResolver.Resolve(write.Owner, write.OwnerObject,
            write.Call.FunctionName, write.EntryPath.Steps, [],
            new Dictionary<string, long?>(StringComparer.Ordinal),
            write.OutgoingPaths, write.Timeline, savedFlags,
            allowUnboundedStateMutation: true);

    private bool HasLaterTimelineAlpha(Write write, float callbackStart, float playhead)
    {
        foreach ((float time, CinematicLiteralCall call) in timelineCalls)
        {
            if (time < callbackStart || time > playhead || call.Selector != 15008
                || call.Arguments.Length < 3
                || call.Arguments[1] > int.MaxValue)
                continue;
            int level = (int)call.Arguments[1];
            uint materialKey = CinematicMaterialState.ResolveMaterialKey(call.Arguments[0],
                call.Target, world, materials);
            if (materialKey == write.MaterialKey && level == write.Level) return true;
        }
        return false;
    }

    private static bool TryResolveAlpha(Write write, float callbackStart, float playhead,
        IReadOnlyDictionary<int, bool?>? savedFlags, out byte alpha)
    {
        alpha = 0;
        string expression = Cast.Replace(write.AlphaExpression.Trim(), string.Empty);
        expression = MemberReference.Replace(expression, match => match.Groups["field"].Value);
        expression = VectorComponentReference.Replace(expression,
            match => $"{match.Groups["vector"].Value}_{match.Groups["component"].Value}");
        if (TryParseNumber(expression, out double numeric))
            return TryConvertAlpha(numeric, out alpha);

        var runtimeInputs = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TIM_gf_MainClock"] = playhead,
            ["TIM_gf_LockTime"] = 0
        };
        AddCurrentObjectPositionInputs(write.Owner, write.OwnerObject,
            write.Call.FunctionName, write.Call.Line, write.EngineCalls, playhead,
            write.Timeline, savedFlags, runtimeInputs);
        CinematicScriptCallbackPathStep? entryTransition = write.EntryPath.Steps.LastOrDefault();
        if (entryTransition != null
            && CinematicScriptCallbackEntryTimeResolver.TryResolveDispatchCallLine(
                entryTransition, write.EngineCalls, out int dispatchLine))
        {
            Dictionary<string, double?> entryTimes = CinematicScriptCallbackEntryTimeResolver.Resolve(
                write.OvaFieldAccesses, write.Call.FunctionName, entryTransition.FromFunction,
                dispatchLine, callbackStart,
                access => CinematicScriptRuntimeBranchEvaluator.Evaluate(write.Owner,
                    write.OwnerObject, access.BranchConditions, callbackStart, write.Timeline,
                    savedFlags).Value == CinematicScriptBranchValue.True);
            foreach ((string field, double? entryTime) in entryTimes)
                runtimeInputs[field] = entryTime;
        }

        if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(write.Owner,
                write.OwnerObject, write.Call.FunctionName, write.ValueAssignments,
                write.EngineCalls, playhead, write.Timeline, _ => null, savedFlags,
                null, runtimeInputs, out Dictionary<string, double?> values))
        {
            values = write.Owner.Variables.Where(variable => variable.ArrayValueCount == null)
                .ToDictionary(variable => variable.Name, variable => ParseNumber(variable.Value),
                    StringComparer.OrdinalIgnoreCase);
        }

        if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateSourceExpression(expression,
                values, out numeric))
            return false;
        return TryConvertAlpha(numeric, out alpha);
    }

    internal static void AddCurrentObjectPositionInputs(CinematicScriptOwner owner,
        SceneObject ownerObject, string functionName, int beforeLine,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls, float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags,
        IDictionary<string, double?> runtimeInputs)
    {
        CinematicScriptReachableEngineCall[] positionCalls = engineCalls.Where(call =>
                call.FunctionName == functionName && call.Line < beforeLine
                && call.Api == "OBJGetPos" && call.Variant == "CURRENT"
                && call.Arguments.Length == 1
                && CurrentVectorOutput.IsMatch(call.Arguments[0])
                && CinematicScriptRuntimeBranchEvaluator.Evaluate(owner, ownerObject,
                    call.BranchConditions, playhead, timelineCalls, savedFlags).Value
                    == CinematicScriptBranchValue.True)
            .ToArray();
        if (positionCalls.Length == 0) return;

        Vector3 jadePosition = TransformData.FromRender(
            Vector3.Transform(Vector3.Zero, ownerObject.RenderMatrix));
        if (!float.IsFinite(jadePosition.X) || !float.IsFinite(jadePosition.Y)
            || !float.IsFinite(jadePosition.Z)) return;

        foreach (CinematicScriptReachableEngineCall call in positionCalls)
        {
            string vectorName = CurrentVectorOutput.Match(call.Arguments[0])
                .Groups["vector"].Value;
            runtimeInputs[$"{vectorName}_x"] = jadePosition.X;
            runtimeInputs[$"{vectorName}_y"] = jadePosition.Y;
            runtimeInputs[$"{vectorName}_z"] = jadePosition.Z;
        }
    }

    private static bool TryConvertAlpha(double value, out byte alpha)
    {
        alpha = 0;
        if (!double.IsFinite(value) || value < 0 || value > byte.MaxValue) return false;
        alpha = (byte)Math.Truncate(value);
        return true;
    }

    internal static bool TryParseNumber(string expression, out double value)
    {
        string normalized = expression.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.TrimEnd('u', 'U', 'l', 'L');
            if (uint.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint hexadecimal))
            {
                value = hexadecimal;
                return true;
            }
            value = 0;
            return false;
        }
        normalized = normalized.TrimEnd('u', 'U', 'l', 'L', 'f', 'F');
        return double.TryParse(normalized, NumberStyles.Float,
                   CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static double? ParseNumber(string expression) =>
        TryParseNumber(expression, out double value) ? value : null;
}
