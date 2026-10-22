using System.Text.RegularExpressions;

namespace JadeView;

internal static class CinematicScriptActionKitMergeResolver
{
    private static readonly Regex MemberField = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex BarePointerAlias = new(
        @"^(?:\([^)]*\)\s*)*(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.CultureInvariant);

    internal static CinematicScriptActionKitMergeAnalysis Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> sourceCalls,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> actionKits)
    {
        var resolved = new List<CinematicScriptActionKitMerge>();
        var unresolved = new List<CinematicScriptUnresolvedActionKitMerge>();
        foreach (var call in sourceCalls.Where(call => call.Api == "ACTKitMerge"))
        {
            if (call.Arguments.Length < 4)
            {
                unresolved.Add(new(call, "The native merge call does not provide destination, source, and range arguments."));
                continue;
            }
            if (IsNullObjectLiteral(call.Arguments[0])) continue;
            if (!TryResolveObjectArgument(owner, call, 0, out uint destinationKey, out string destinationBinding))
            {
                unresolved.Add(new(call, $"Destination action-kit owner is dynamic or ambiguous ({call.Arguments[0]})."));
                continue;
            }
            if (!TryResolveObjectArgument(owner, call, 1, out uint sourceKey, out string sourceBinding))
            {
                string flowDetail = string.IsNullOrWhiteSpace(sourceBinding) ? "" : $" Flow: {sourceBinding}.";
                unresolved.Add(new(call, $"Source action-kit owner is dynamic or ambiguous ({call.Arguments[1]}).{flowDetail}"));
                continue;
            }
            if (!CinematicScriptTrackIndexResolver.TryResolveSignedIntegerArgument(owner, call, 2,
                    out int firstAction, out string firstBinding)
                || !CinematicScriptTrackIndexResolver.TryResolveSignedIntegerArgument(owner, call, 3,
                    out int lastAction, out string lastBinding))
            {
                unresolved.Add(new(call, "The action range is not a literal or uniquely resolved scalar OVA expression."));
                continue;
            }
            if (firstAction < 0 || lastAction < firstAction)
            {
                unresolved.Add(new(call, $"The resolved action range {firstAction}..{lastAction} is invalid."));
                continue;
            }

            var destination = CinematicActionKitScopeResolver.Resolve(destinationKey, owner.WorldKey, actionKits);
            if (!destination.IsResolved)
            {
                unresolved.Add(new(call, $"Destination action table {destinationKey:X8}: {destination.Failure}"));
                continue;
            }
            var source = CinematicActionKitScopeResolver.Resolve(sourceKey, owner.WorldKey, actionKits);
            if (!source.IsResolved)
            {
                unresolved.Add(new(call, $"Source action table {sourceKey:X8}: {source.Failure}"));
                continue;
            }

            var destinationActions = destination.Actions!;
            int[] sourceSlots = source.Actions!.Keys.Where(slot => slot >= firstAction && slot <= lastAction).ToArray();
            int conflicts = sourceSlots.Count(destinationActions.ContainsKey);
            resolved.Add(new(call, call.Variant, destinationKey, destination.SourceWorldKey!.Value,
                sourceKey, source.SourceWorldKey!.Value, destinationBinding, sourceBinding,
                firstAction, lastAction, $"{firstBinding}; {lastBinding}", sourceSlots.Length, conflicts));
        }
        return new(resolved.ToArray(), unresolved.ToArray());
    }

    private static bool TryResolveObjectArgument(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, int argumentIndex, out uint ownerKey, out string binding)
    {
        ownerKey = 0;
        binding = "";
        if (argumentIndex >= call.Arguments.Length) return false;
        string argument = call.Arguments[argumentIndex].Trim();
        if (argument == "AI_gpst_CurrentGameObject")
        {
            ownerKey = owner.OwnerKey;
            binding = "current script owner";
            return true;
        }

        if (TryResolveObjectArgumentFlow(owner, call, argument, out ownerKey, out binding))
            return true;

        var fields = MemberField.Matches(argument).Select(match => match.Groups["field"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? pointerAlias = BarePointerAlias.Match(argument) is { Success: true } aliasMatch
            ? aliasMatch.Groups["alias"].Value : null;
        foreach (var reference in owner.ObjectReferences)
        {
            if (reference.ArrayIndex != null) continue;
            bool directField = fields.Contains(reference.VariableName);
            bool callAlias = pointerAlias != null && call.ArgumentFields.Any(field =>
                field.Name.Equals(reference.VariableName, StringComparison.OrdinalIgnoreCase)
                && field.AliasVariable == pointerAlias);
            if (!directField && !callAlias) continue;
            var effective = owner.EffectiveObjectReference(reference.VariableName);
            if (effective == null) continue;
            ownerKey = effective.TargetKey;
            binding = $"OVA {effective.VariableName} → {ownerKey:X8}";
            return true;
        }
        return false;
    }

    private static bool TryResolveObjectArgumentFlow(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, string argument, out uint ownerKey, out string binding)
    {
        ownerKey = 0;
        binding = "";
        if (call.ArgumentAssignmentAlternatives is not { Length: > 0 }) return false;

        var (localValues, fieldValues) = EvaluateObjectFlow(owner, call);
        ObjectPointerValue? resolved = ResolvePointerExpression(owner, argument, localValues, fieldValues);
        if (resolved?.Key is not { } key)
        {
            string locals = string.Join(", ", localValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={DescribePointerValue(pair.Value)}"));
            string fields = string.Join(", ", fieldValues.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={DescribePointerValue(pair.Value)}"));
            binding = $"abstract locals [{locals}], OVA updates [{fields}], final {argument}={DescribePointerValue(resolved)}";
            return false;
        }
        ownerKey = key;
        binding = resolved.Binding;
        return true;
    }

    private static bool IsNullObjectLiteral(string expression) =>
        expression.Trim() is "0" or "NULL" or "nullptr";

    private static (Dictionary<string, ObjectPointerValue?> LocalValues,
        Dictionary<string, ObjectPointerValue?> FieldValues) EvaluateObjectFlow(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call)
    {
        var localValues = new Dictionary<string, ObjectPointerValue?>(StringComparer.Ordinal);
        var fieldValues = new Dictionary<string, ObjectPointerValue?>(StringComparer.OrdinalIgnoreCase);
        var flowEvents = call.ArgumentAssignmentAlternatives!.Select(alternative =>
                new ObjectFlowEvent(alternative.Line, alternative.Variable, alternative.Expression,
                    alternative.BranchConditions, IsOvaWrite: false))
            .Concat((call.OvaFieldWrites ?? []).Select(write => new ObjectFlowEvent(write.Line,
                write.VariableName, write.Expression, write.BranchConditions, IsOvaWrite: true)))
            .OrderBy(flowEvent => flowEvent.Line)
            .ThenBy(flowEvent => flowEvent.IsOvaWrite ? 0 : 1)
            .ToArray();

        foreach (var flowEvent in flowEvents)
        {
            bool? branchPassed = EvaluateBranchConditions(owner, flowEvent.BranchConditions,
                localValues, fieldValues);
            if (branchPassed == false) continue;

            ObjectPointerValue? value = branchPassed == true
                ? ResolvePointerExpression(owner, flowEvent.Expression, localValues, fieldValues)
                : null;
            if (flowEvent.IsOvaWrite)
                fieldValues[flowEvent.Variable] = value;
            else
                localValues[flowEvent.Variable] = value;
        }
        return (localValues, fieldValues);
    }

    private static string DescribePointerValue(ObjectPointerValue? value) => value switch
    {
        null => "unknown",
        { IsDynamic: true } => $"dynamic ({value.Binding})",
        { IsNull: true } => "null",
        { Key: { } key } => $"{key:X8} ({value.Binding})",
        _ => value.Binding
    };

    private static bool? EvaluateBranchConditions(CinematicScriptOwner owner,
        CinematicScriptBranchConditionSource[]? conditions,
        IReadOnlyDictionary<string, ObjectPointerValue?> localValues,
        IReadOnlyDictionary<string, ObjectPointerValue?> fieldValues)
    {
        bool? result = true;
        foreach (var condition in conditions ?? [])
        {
            bool? current = EvaluateCondition(owner, condition.Expression, localValues, fieldValues);
            if (condition.Kind == "else" && current is { } branchResult)
                current = !branchResult;
            if (current == false) return false;
            if (current == null) result = null;
        }
        return result;
    }

    private static bool? EvaluateCondition(CinematicScriptOwner owner, string sourceExpression,
        IReadOnlyDictionary<string, ObjectPointerValue?> localValues,
        IReadOnlyDictionary<string, ObjectPointerValue?> fieldValues)
    {
        string expression = sourceExpression.Trim();
        if (expression.StartsWith('!') && !expression.StartsWith("!=", StringComparison.Ordinal))
        {
            bool? negated = EvaluateCondition(owner, expression[1..], localValues, fieldValues);
            return negated is { } negatedResult ? !negatedResult : null;
        }

        Match comparison = Regex.Match(expression,
            @"^(?<left>.+?)\s*(?<operator><=|>=|==|!=|(?<!-)[<>](?!>))\s*(?<right>.+)$",
            RegexOptions.CultureInvariant);
        if (comparison.Success)
        {
            if (!TryResolveComparisonValue(owner, comparison.Groups["left"].Value, localValues,
                    fieldValues, out long leftValue)
                || !TryResolveComparisonValue(owner, comparison.Groups["right"].Value, localValues,
                    fieldValues, out long rightValue))
                return null;
            return comparison.Groups["operator"].Value switch
            {
                "<=" => leftValue <= rightValue,
                ">=" => leftValue >= rightValue,
                "<" => leftValue < rightValue,
                ">" => leftValue > rightValue,
                "==" => leftValue == rightValue,
                "!=" => leftValue != rightValue,
                _ => null
            };
        }

        ObjectPointerValue? value = ResolvePointerExpression(owner, expression, localValues, fieldValues);
        if (value == null || value.IsDynamic) return null;
        return !value.IsNull;
    }

    private static bool TryResolveComparisonValue(CinematicScriptOwner owner, string expression,
        IReadOnlyDictionary<string, ObjectPointerValue?> localValues,
        IReadOnlyDictionary<string, ObjectPointerValue?> fieldValues, out long value)
    {
        value = 0;
        string text = expression.Trim();
        Match numeric = Regex.Match(text, @"^(?:0[xX](?<hex>[0-9A-Fa-f]+)|(?<decimal>-?\d+))[uUlL]*$",
            RegexOptions.CultureInvariant);
        if (numeric.Success)
        {
            value = numeric.Groups["hex"].Success
                ? Convert.ToInt64(numeric.Groups["hex"].Value, 16)
                : long.Parse(numeric.Groups["decimal"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        ObjectPointerValue? pointer = ResolvePointerExpression(owner, text, localValues, fieldValues);
        if (pointer == null || pointer.IsDynamic) return false;
        value = pointer.Key ?? 0;
        return true;
    }

    private static ObjectPointerValue? ResolvePointerExpression(CinematicScriptOwner owner, string sourceExpression,
        IReadOnlyDictionary<string, ObjectPointerValue?> localValues,
        IReadOnlyDictionary<string, ObjectPointerValue?> fieldValues)
    {
        string expression = Regex.Replace(sourceExpression.Trim(),
            @"\(\s*[A-Za-z_][A-Za-z0-9_]*(?:\s*\*)+\s*\)", "",
            RegexOptions.CultureInvariant).Trim();
        expression = Regex.Replace(expression, @"^\((?:unsigned\s+)?(?:int|uint32_t)\)\s*", "",
            RegexOptions.CultureInvariant).Trim();

        if (expression == "AI_gpst_CurrentGameObject")
            return new(owner.OwnerKey, IsNull: false, IsDynamic: false, "current script owner");
        if (expression == "AI_gpst_CurrentInstance")
            return new(1, IsNull: false, IsDynamic: false, "current callback instance context");
        if (expression == "AI_gpst_CurrentInstance->instanceVarDataCurrent")
            return new(owner.OwnerKey, IsNull: false, IsDynamic: false, "current callback instance data");
        if (expression.Contains("AI_MainActor[", StringComparison.Ordinal))
            return new(null, IsNull: false, IsDynamic: true, "AI_MainActor selection");

        Match member = MemberField.Match(expression);
        if (member.Success)
        {
            string fieldName = member.Groups["field"].Value;
            if (fieldValues.TryGetValue(fieldName, out var overridden)) return overridden;
            return ResolveOvaObjectField(owner, fieldName);
        }

        if (Regex.IsMatch(expression, @"^0+(?:[uUlL]*)$", RegexOptions.CultureInvariant))
            return new(null, IsNull: true, IsDynamic: false, "null pointer");
        Match numeric = Regex.Match(expression, @"^(?:0[xX](?<hex>[0-9A-Fa-f]+)|(?<decimal>\d+))[uUlL]*$",
            RegexOptions.CultureInvariant);
        if (numeric.Success)
        {
            uint number = numeric.Groups["hex"].Success
                ? Convert.ToUInt32(numeric.Groups["hex"].Value, 16)
                : uint.Parse(numeric.Groups["decimal"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return number switch
            {
                0 => new(null, IsNull: true, IsDynamic: false, "null pointer"),
                1 or 2 => new(null, IsNull: false, IsDynamic: true, $"AI_MainActor selector {number}"),
                _ => new(number, IsNull: false, IsDynamic: false, $"literal object key {number:X8}")
            };
        }

        Match alias = BarePointerAlias.Match(expression);
        if (alias.Success)
            return localValues.GetValueOrDefault(alias.Groups["alias"].Value);
        return null;
    }

    private static ObjectPointerValue? ResolveOvaObjectField(CinematicScriptOwner owner, string fieldName)
    {
        var reference = owner.EffectiveObjectReference(fieldName);
        if (reference != null)
            return new(reference.TargetKey, IsNull: reference.TargetKey == 0,
                IsDynamic: reference.TargetKey is 1 or 2,
                $"OVA {reference.VariableName} ({reference.Source})");

        var variable = owner.EffectiveVariable(fieldName);
        if (variable == null) return null;
        string text = variable.Value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint key))
            return null;
        return key switch
        {
            0 => new(null, IsNull: true, IsDynamic: false, $"OVA {fieldName} ({variable.Source})"),
            1 or 2 => new(null, IsNull: false, IsDynamic: true, $"OVA {fieldName} selects AI_MainActor {key}"),
            _ => new(key, IsNull: false, IsDynamic: false, $"OVA {fieldName} ({variable.Source})")
        };
    }

    private sealed record ObjectFlowEvent(int Line, string Variable, string Expression,
        CinematicScriptBranchConditionSource[] BranchConditions, bool IsOvaWrite);

    private sealed record ObjectPointerValue(uint? Key, bool IsNull, bool IsDynamic, string Binding);
}
