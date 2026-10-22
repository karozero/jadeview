namespace JadeView;

internal static class CinematicScriptReferencedObjectCallResolver
{
    internal static CinematicScriptReferencedObjectCall[] Resolve(CinematicScriptOwner owner)
    {
        var calls = CinematicScriptReader.ReachableEngineCalls(owner);
        return calls.SelectMany(call => ResolveCall(owner, call))
            .DistinctBy(operation => (operation.VariableName, operation.ObjectKey,
                operation.FunctionName, operation.Source, operation.Line, operation.Api))
            .OrderBy(operation => operation.FunctionName, StringComparer.Ordinal)
            .ThenBy(operation => operation.Source, StringComparer.Ordinal)
            .ThenBy(operation => operation.Line)
            .ToArray();
    }

    private static IEnumerable<CinematicScriptReferencedObjectCall> ResolveCall(
        CinematicScriptOwner owner, CinematicScriptReachableEngineCall call)
    {
        foreach (var argumentField in call.ArgumentFields)
        {
            var binding = owner.EffectiveObjectReference(argumentField.Name);
            if (binding == null || binding.TargetKey is 0 or uint.MaxValue) continue;

            yield return new(argumentField.Name, binding.TargetKey, binding.TargetName,
                argumentField.AliasVariable, argumentField.AssignmentLine, call.Api,
                call.Arguments, call.FunctionName, call.Source, call.Line,
                call.CallbackPath, call.BranchConditions.Select(FormatBranch).ToArray());
        }
    }

    private static string FormatBranch(CinematicScriptBranchConditionSource branch) =>
        branch.Kind == "else" ? $"else of ({branch.Expression})" : $"if ({branch.Expression})";
}

internal sealed record CinematicScriptReferencedObjectCall(string VariableName, uint ObjectKey,
    string? TargetName, string? AliasVariable, int? AssignmentLine, string Api,
    string[] Arguments, string FunctionName,
    string Source, int Line, string CallbackPath, string[] Branches);
