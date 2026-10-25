using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Resolves paired draw hooks when registration gates, callback paths, and scalar
/// field state can be reconstructed from bounded cinematic preview inputs.
/// Unsupported or ambiguous runtime state remains unresolved instead of guessed.
/// </summary>
internal static class CinematicRliDisplayCallbackResolver
{
    private static readonly Regex OwnerField = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);

    internal static bool TryResolvePairedLiteralOverride(
        IReadOnlyList<CinematicScriptRliDisplayCallbackCandidate> callbacks,
        out CinematicRliDrawState state)
    {
        state = default;
        CinematicScriptRliDisplayCallbackCandidate[] beforeCallbacks = callbacks
            .Where(callback => callback.CallbackType == 0).ToArray();
        CinematicScriptRliDisplayCallbackCandidate[] afterCallbacks = callbacks
            .Where(callback => callback.CallbackType == 1).ToArray();
        if (beforeCallbacks.Length != 1 || afterCallbacks.Length != 1) return false;

        CinematicScriptRliDisplayCallbackCandidate before = beforeCallbacks[0];
        CinematicScriptRliDisplayCallbackCandidate after = afterCallbacks[0];
        if (before.ContextKey is not { } contextKey
            || before.TargetKey is null
            || after.ContextKey != contextKey
            || after.TargetKey != before.TargetKey
            || !before.HasPairedCallback || !after.HasPairedCallback
            || before.HasMatchingRemovalSite || after.HasMatchingRemovalSite
            || IsUnresolvedPath(before.CallbackPath) || IsUnresolvedPath(after.CallbackPath)
            || before.BranchConditions.Length != 0
            || after.BranchConditions.Length != 0
            || before.EffectBranchConditions.Length != 0
            || after.EffectBranchConditions.Length != 0
            || before.FactorIsUsed is not { } factorIsUsed
            || !TryReadFloat(before.FactorExpression, out float factor)
            || !TryReadPackedColor(before.DestinationColorExpression, out Vector4 color))
            return false;

        state = new(color, factor, factorIsUsed);
        return true;
    }

    internal static bool TryResolvePairedOverride(CinematicScriptOwner owner,
        SceneObject ownerObject, IReadOnlyList<CinematicScriptRliDisplayCallbackCandidate> callbacks,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        IReadOnlyList<CinematicScriptRliValueFlowAssignment> assignments,
        IReadOnlyDictionary<string, CinematicScriptFunctionPath> callbackPaths,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags, float playhead,
        Func<int, float, float?> channelProgressionResolver,
        out CinematicRliDrawState state,
        IReadOnlyList<CinematicScriptReachableOvaFieldAccess>? ovaFieldAccesses = null)
    {
        state = default;
        CinematicScriptRliDisplayCallbackCandidate[] beforeCallbacks = callbacks
            .Where(callback => callback.CallbackType == 0).ToArray();
        CinematicScriptRliDisplayCallbackCandidate[] afterCallbacks = callbacks
            .Where(callback => callback.CallbackType == 1).ToArray();
        if (beforeCallbacks.Length != 1 || afterCallbacks.Length != 1) return false;

        CinematicScriptRliDisplayCallbackCandidate before = beforeCallbacks[0];
        CinematicScriptRliDisplayCallbackCandidate after = afterCallbacks[0];
        if (before.ContextKey != owner.OwnerKey || before.TargetKey is null
            || after.ContextKey != before.ContextKey || after.TargetKey != before.TargetKey
            || !before.HasPairedCallback || !after.HasPairedCallback
            || before.HasMatchingRemovalSite || after.HasMatchingRemovalSite
            || IsUnresolvedPath(before.CallbackPath) || IsUnresolvedPath(after.CallbackPath)
            || before.EffectBranchConditions.Length != 0
            || after.EffectBranchConditions.Length != 0
            || before.FactorIsUsed is not { } factorIsUsed
            || !TryReadPackedColor(before.DestinationColorExpression, out Vector4 color)
            || !IsRegistrationActive(owner, ownerObject, before, engineCalls,
                timelineCalls, savedFlags)) return false;

        float factor;
        if (TryReadFloat(before.FactorExpression, out factor))
        {
            state = new(color, factor, factorIsUsed);
            return true;
        }

        if (!TryReadOwnerField(before.FactorExpression, out string fieldName)
            || !TryEvaluateLatestField(owner, ownerObject, fieldName, assignments,
                engineCalls, callbackPaths, timelineCalls, savedFlags, playhead,
                channelProgressionResolver, out factor, out string? sourceFunction,
                ovaFieldAccesses)) return false;

        state = new(color, factor, factorIsUsed, sourceFunction);
        return true;
    }

    internal static bool IsRegistrationActive(CinematicScriptOwner owner, SceneObject ownerObject,
        CinematicScriptRliDisplayCallbackCandidate before,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        CinematicScriptReachableEngineCall[] registrations = engineCalls.Where(call =>
                call.Api == "AICBAdd" && call.Source == before.Source && call.Line == before.Line)
            .ToArray();
        if (registrations.Length != 1) return false;

        CinematicScriptReachableEngineCall registration = registrations[0];
        uint? originalCapabilities = ownerObject.PreviewCapabilities;
        try
        {
            ownerObject.PreviewCapabilities ??= ownerObject.InitialCapabilities & ushort.MaxValue;
            var resolvedArguments = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            if (registration.Arguments.Length > 1
                && IsIdentifier(registration.Arguments[1].Trim()))
                resolvedArguments[registration.Arguments[1].Trim()] = 1;
            foreach (CinematicScriptOvaFieldBinding binding in
                     CinematicScriptArgumentBindingResolver.ResolveArgumentOwnerFields(
                         registration, 1, owner))
            {
                if (binding.ObjectKey == before.TargetKey)
                    resolvedArguments[binding.VariableName] = 1;
            }
            return CinematicScriptRuntimeBranchEvaluator.Evaluate(owner, ownerObject,
                registration.BranchConditions, 0, timelineCalls, savedFlags, resolvedArguments).Value
                == CinematicScriptBranchValue.True;
        }
        finally
        {
            ownerObject.PreviewCapabilities = originalCapabilities;
        }
    }

    internal static bool TryEvaluateLatestField(CinematicScriptOwner owner, SceneObject ownerObject,
        string fieldName, IReadOnlyList<CinematicScriptRliValueFlowAssignment> assignments,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        IReadOnlyDictionary<string, CinematicScriptFunctionPath> callbackPaths,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags, float playhead,
        Func<int, float, float?> channelProgressionResolver, out float value,
        out string? sourceFunction,
        IReadOnlyList<CinematicScriptReachableOvaFieldAccess>? ovaFieldAccesses = null)
    {
        value = 0;
        sourceFunction = null;
        CinematicScriptRliValueFlowAssignment[] writers = assignments
            .Where(assignment => assignment.Variable.Equals(fieldName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] writerFunctions = writers.Select(assignment => assignment.FunctionName)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (writerFunctions.Length == 0) return false;

        var executions = new List<(string Function, float Start, float SampleTime)>();
        foreach (string functionName in writerFunctions)
        {
            if (!callbackPaths.TryGetValue(functionName, out CinematicScriptFunctionPath? entryPath))
                return false;
            CinematicScriptFunctionPath[] outgoingPaths = callbackPaths.Values
                .Where(path => path.Steps.LastOrDefault()?.FromFunction == functionName)
                .ToArray();
            CinematicScriptCallbackWindow? window = CinematicScriptCallbackWindowResolver.Resolve(
                owner, ownerObject, functionName, entryPath.Steps, [],
                new Dictionary<string, long?>(), outgoingPaths, timelineCalls, savedFlags,
                allowUnboundedStateMutation: true);
            if (window == null) return false;
            if (playhead < window.Start) continue;

            float sampleTime = window.End is { } end && playhead >= end
                ? MathF.BitDecrement(end)
                : playhead;
            executions.Add((functionName, window.Start, sampleTime));
        }

        if (executions.Count == 0) return false;
        if (executions.GroupBy(execution => execution.Start)
            .Any(group => group.Count() > 1)) return false;
        uint? originalCapabilities = ownerObject.PreviewCapabilities;
        try
        {
            var persistentFields = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (var execution in executions.OrderBy(execution => execution.Start)
                         .ThenBy(execution => execution.Function, StringComparer.Ordinal))
            {
                ownerObject.PreviewCapabilities = CinematicScriptCallbackWindowResolver.CapabilitiesAt(
                    ownerObject, execution.Start, timelineCalls);
                if (CinematicScriptRuntimeBranchEvaluator.EvaluateCallbackPath(owner, ownerObject,
                        callbackPaths[execution.Function].Steps, execution.Start,
                        timelineCalls, savedFlags).Value != CinematicScriptBranchValue.True)
                    return false;

                ownerObject.PreviewCapabilities = CinematicScriptCallbackWindowResolver.CapabilitiesAt(
                    ownerObject, execution.SampleTime, timelineCalls);
                CinematicScriptRliValueFlowAssignment[] functionAssignments = assignments
                    .Where(assignment => assignment.FunctionName == execution.Function)
                    .ToArray();
                Dictionary<string, double?> messageFields =
                    CinematicScriptMessageScalarStateResolver.Resolve(owner, ownerObject,
                        timelineCalls, execution.SampleTime, savedFlags);
                foreach ((string name, double? previousValue) in persistentFields)
                    if (!messageFields.ContainsKey(name)) messageFields[name] = previousValue;
                Dictionary<string, double?> runtimeInputs = RuntimeInputs(owner, ownerObject,
                    execution.Function, callbackPaths, engineCalls, ovaFieldAccesses,
                    timelineCalls, savedFlags, execution.Start, execution.SampleTime);
                if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(owner,
                        ownerObject, execution.Function, functionAssignments, engineCalls,
                        execution.SampleTime, timelineCalls,
                        channel => channelProgressionResolver(channel, execution.SampleTime),
                        savedFlags, messageFields, runtimeInputs,
                        out Dictionary<string, double?> resultingFields))
                    return false;
                persistentFields = resultingFields;
                sourceFunction = execution.Function;
            }

            if (persistentFields.GetValueOrDefault(fieldName) is not { } resolvedValue
                || !double.IsFinite(resolvedValue) || resolvedValue < -float.MaxValue
                || resolvedValue > float.MaxValue) return false;
            value = (float)resolvedValue;
            return float.IsFinite(value);
        }
        finally
        {
            ownerObject.PreviewCapabilities = originalCapabilities;
        }
    }

    private static Dictionary<string, double?> RuntimeInputs(CinematicScriptOwner owner,
        SceneObject ownerObject, string functionName,
        IReadOnlyDictionary<string, CinematicScriptFunctionPath> callbackPaths,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        IReadOnlyList<CinematicScriptReachableOvaFieldAccess>? ovaFieldAccesses,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags, float callbackStart, float playhead)
    {
        var runtimeInputs = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TIM_gf_MainClock"] = playhead,
            ["TIM_gf_LockTime"] = 0
        };
        if (ovaFieldAccesses == null
            || !callbackPaths.TryGetValue(functionName, out CinematicScriptFunctionPath? entryPath))
            return runtimeInputs;

        CinematicScriptCallbackPathStep? transition = entryPath.Steps.LastOrDefault();
        if (!CinematicScriptCallbackEntryTimeResolver.TryResolveDispatchCallLine(
                transition, engineCalls, out int dispatchLine)) return runtimeInputs;

        uint? originalCapabilities = ownerObject.PreviewCapabilities;
        try
        {
            ownerObject.PreviewCapabilities = CinematicScriptCallbackWindowResolver.CapabilitiesAt(
                ownerObject, callbackStart, timelineCalls);
            Dictionary<string, double?> entryTimes = CinematicScriptCallbackEntryTimeResolver.Resolve(
                ovaFieldAccesses, functionName, transition!.FromFunction, dispatchLine,
                callbackStart, access => CinematicScriptRuntimeBranchEvaluator.Evaluate(
                    owner, ownerObject, access.BranchConditions, callbackStart,
                    timelineCalls, savedFlags).Value == CinematicScriptBranchValue.True);
            foreach ((string fieldName, double? entryTime) in entryTimes)
                runtimeInputs[fieldName] = entryTime;
        }
        finally
        {
            ownerObject.PreviewCapabilities = originalCapabilities;
        }
        return runtimeInputs;
    }

    private static bool TryReadOwnerField(string expression, out string fieldName)
    {
        Match match = OwnerField.Match(expression);
        fieldName = match.Success ? match.Groups["field"].Value : string.Empty;
        return match.Success;
    }

    private static bool IsIdentifier(string expression) => expression.Length > 0
        && (char.IsLetter(expression[0]) || expression[0] == '_')
        && expression.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsUnresolvedPath(string path) =>
        path.Contains("callback path unresolved", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadFloat(string expression, out float value)
    {
        string normalized = expression.Trim().TrimEnd('f', 'F');
        return float.TryParse(normalized, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }

    private static bool TryReadPackedColor(string expression, out Vector4 color)
    {
        string normalized = expression.Trim();
        if (normalized.StartsWith("(", StringComparison.Ordinal))
        {
            int closing = normalized.LastIndexOf(')');
            if (closing >= 0) normalized = normalized[(closing + 1)..].Trim();
        }

        long parsed;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint hexadecimal))
            {
                color = default;
                return false;
            }
            parsed = hexadecimal;
        }
        else if (!long.TryParse(normalized, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out parsed))
        {
            color = default;
            return false;
        }

        uint packed = unchecked((uint)parsed);
        color = new((packed & 0xff) / 255f,
            ((packed >> 8) & 0xff) / 255f,
            ((packed >> 16) & 0xff) / 255f,
            ((packed >> 24) & 0xff) / 255f);
        return true;
    }
}
