using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Finds source-backed channel-progress copies from a callback's current object to a
/// uniquely resolved object reference. It reports candidates and does not execute them.
/// </summary>
internal static partial class CinematicScriptChannelTransferAnalyzer
{
    private const string ChannelGetter = "OBJ_GetChannelProg";
    private const string ChannelSetter = "OBJ_SetChannelProg";
    private const string InvisibleSetter = "OBJ_FlagInvisibleSet";

    [GeneratedRegex(@"AI_EvalFunc_OBJ_GetChannelProg_C\s*\(\s*AI_gpst_CurrentGameObject\s*,\s*([^\)]+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentObjectChannelGetter();

    internal static CinematicScriptChannelTransfer[] Analyze(CinematicScriptOwner owner,
        IReadOnlyList<CinematicScriptReachableEngineCall>? calls = null,
        IReadOnlyList<CinematicScriptReachableSourceReference>? sourceReferences = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        calls ??= CinematicScriptReader.ConfiguredModelEngineCalls(owner);
        sourceReferences ??= CinematicScriptReader.ConfiguredModelSourceReferences(owner);

        return calls.Where(call => call.Api == ChannelSetter && call.Arguments.Length >= 3)
            .Select(call => TryResolveTransfer(owner, call, calls, sourceReferences))
            .Where(transfer => transfer != null)
            .Select(transfer => transfer!)
            .DistinctBy(transfer => (transfer.FunctionName, transfer.TargetKey,
                transfer.SourceChannel, transfer.TargetChannel, transfer.SetterLine))
            .OrderBy(transfer => transfer.FunctionName, StringComparer.Ordinal)
            .ThenBy(transfer => transfer.SetterLine)
            .ToArray();
    }

    private static CinematicScriptChannelTransfer? TryResolveTransfer(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall setter,
        IReadOnlyList<CinematicScriptReachableEngineCall> calls,
        IReadOnlyList<CinematicScriptReachableSourceReference> sourceReferences)
    {
        CinematicScriptArgumentAssignmentSource[] getterAssignments = setter.ArgumentAssignments
            .Where(assignment => assignment.IsUnique
                && assignment.Variable.Equals(setter.Arguments[2], StringComparison.Ordinal))
            .ToArray();
        if (getterAssignments.Length != 1) return null;

        Match getterMatch = CurrentObjectChannelGetter().Match(getterAssignments[0].Expression);
        if (!getterMatch.Success
            || !TryReadInteger(getterMatch.Groups[1].Value, out int sourceChannel)
            || !TryReadInteger(setter.Arguments[1], out int targetChannel)) return null;

        CinematicScriptBranchConditionSource[]? getterBranches = FindGetterBranches(
            setter.FunctionName, getterAssignments[0], calls);
        if (getterBranches == null || !ContainsBranches(setter.BranchConditions, getterBranches)) return null;

        CinematicScriptOvaFieldBinding[] targetFields = CinematicScriptArgumentBindingResolver
            .ResolveOwnerFields(setter, owner)
            .Where(binding => binding.ObjectKey != null)
            .DistinctBy(binding => binding.ObjectKey)
            .ToArray();
        CinematicScriptReachableSourceReference[] sourceTargets =
            CinematicScriptArgumentBindingResolver.ResolveArgumentObjectReferences(
                setter, 0, sourceReferences);

        var targetMatches = targetFields.Select(binding =>
                (Key: binding.ObjectKey!.Value, Name: binding.TargetName ?? binding.VariableName,
                    Field: binding.VariableName))
            .Concat(sourceTargets.Select(reference =>
                (Key: reference.Key, Name: reference.Symbol, Field: reference.Symbol)))
            .DistinctBy(target => target.Key)
            .ToArray();
        if (targetMatches.Length != 1) return null;

        var invisibleClear = calls.Where(call => call.FunctionName == setter.FunctionName
                && call.Api == InvisibleSetter && call.Arguments.Length >= 2
                && IsZero(call.Arguments[1])
                && ReferencesTarget(call, targetMatches[0].Field, owner)
                && ContainsBranches(call.BranchConditions, getterBranches))
            .OrderBy(call => Math.Abs(call.Line - setter.Line))
            .FirstOrDefault();
        CinematicScriptFunctionPath? callbackPath = CinematicScriptReader.NestedCallbackPaths(owner)
            .FirstOrDefault(path => path.FunctionName == setter.FunctionName);
        string[] receiverGuardAliases = ResolveReceiverGuardAliases(setter,
            targetMatches[0].Field);

        return new(owner.OwnerKey, targetMatches[0].Key, targetMatches[0].Name,
            targetMatches[0].Field, sourceChannel, targetChannel, setter.FunctionName,
            setter.Source, getterAssignments[0].Line, setter.Line, setter.CallbackPath,
            !setter.CallbackPath.Contains("source callback path unresolved", StringComparison.Ordinal),
            setter.BranchConditions, invisibleClear != null, invisibleClear?.Line)
        {
            CallbackPathSteps = callbackPath?.Steps ?? [],
            ReceiverGuardAliases = receiverGuardAliases
        };
    }

    private static string[] ResolveReceiverGuardAliases(CinematicScriptReachableEngineCall call,
        string targetField)
    {
        return call.BranchConditions
            .Select(branch => Regex.Match(branch.Expression,
                @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.CultureInvariant))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Where(alias => call.ArgumentAssignments.Any(assignment => assignment.IsUnique
                && assignment.Variable == alias
                && assignment.Expression.Contains($"->{targetField}", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static CinematicScriptBranchConditionSource[]? FindGetterBranches(string functionName,
        CinematicScriptArgumentAssignmentSource getterAssignment,
        IReadOnlyList<CinematicScriptReachableEngineCall> calls)
    {
        CinematicScriptReachableEngineCall[] getters = calls.Where(call =>
                call.FunctionName == functionName && call.Api == ChannelGetter
                && call.Line == getterAssignment.Line)
            .ToArray();
        return getters.Length == 1 ? getters[0].BranchConditions : null;
    }

    private static bool ReferencesTarget(CinematicScriptReachableEngineCall call,
        string targetField, CinematicScriptOwner owner) =>
        call.ArgumentFields.Any(field => field.Name.Equals(targetField,
            StringComparison.OrdinalIgnoreCase))
        || CinematicScriptArgumentBindingResolver.ResolveOwnerFields(call, owner)
            .Any(binding => binding.VariableName.Equals(targetField,
                StringComparison.OrdinalIgnoreCase));

    private static bool ContainsBranches(IEnumerable<CinematicScriptBranchConditionSource> candidate,
        IEnumerable<CinematicScriptBranchConditionSource> required)
    {
        var branches = candidate.Select(BranchKey).ToHashSet(StringComparer.Ordinal);
        return required.All(branch => branches.Contains(BranchKey(branch)));
    }

    private static bool IsZero(string value) => TryReadInteger(value, out int parsed) && parsed == 0;

    private static string BranchKey(CinematicScriptBranchConditionSource branch) =>
        $"{branch.Kind}:{branch.Expression}:{branch.Line}";

    private static bool TryReadInteger(string value, out int parsed)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out parsed);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
}
