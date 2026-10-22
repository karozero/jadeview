namespace JadeView;

internal static class CinematicScriptArgumentBindingResolver
{
    internal static CinematicScriptReachableSourceReference[] ResolveSourceObjectReferences(
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences) =>
        sourceReferences.Where(reference => reference.Kind == "Object"
                && reference.FunctionName == call.FunctionName
                && UsesIdentifier(call.Arguments, $"{call.FunctionName}_{reference.Symbol}",
                    $"fct_{call.FunctionName}_{reference.Symbol}"))
            .DistinctBy(reference => reference.Key)
            .OrderBy(reference => reference.Symbol, StringComparer.Ordinal)
            .ToArray();

    internal static CinematicScriptOvaFieldBinding[] ResolveOwnerFields(
        CinematicScriptReachableEngineCall call, CinematicScriptOwner owner)
    {
        var referencedFieldNames = owner.Variables.Select(variable => variable.Name)
            .Concat(owner.ObjectReferences.Where(reference => reference.ArrayIndex == null)
                .Select(reference => reference.VariableName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(fieldName => call.ArgumentFields.Any(field =>
                    field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                || call.ArgumentAssignments.Any(assignment =>
                    ReferencesMemberField(assignment.Expression, fieldName))
                || UsesIdentifier(call.Arguments, fieldName))
            .OrderBy(fieldName => fieldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return referencedFieldNames.Select(fieldName => BindField(owner, fieldName))
            .Where(binding => binding != null)
            .Select(binding => binding!)
            .ToArray();
    }

    private static CinematicScriptOvaFieldBinding? BindField(CinematicScriptOwner owner, string fieldName)
    {
        var objectReference = owner.EffectiveObjectReference(fieldName);
        if (objectReference != null)
            return new(fieldName, objectReference.Source, $"0x{objectReference.TargetKey:X8}",
                objectReference.TargetKey, objectReference.TargetName);

        var variable = owner.EffectiveVariable(fieldName);
        return variable == null ? null : new(fieldName, variable.Source, variable.Value, null, null);
    }

    private static bool UsesIdentifier(IEnumerable<string> expressions, params string[] identifiers) =>
        expressions.Any(expression => identifiers.Any(identifier => ContainsIdentifier(expression, identifier)));

    private static bool ReferencesMemberField(string expression, string fieldName) =>
        expression.Contains($"->{fieldName}", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIdentifier(string expression, string identifier)
    {
        int searchFrom = 0;
        while (searchFrom < expression.Length)
        {
            int found = expression.IndexOf(identifier, searchFrom, StringComparison.Ordinal);
            if (found < 0) return false;
            int end = found + identifier.Length;
            bool startsIdentifier = found == 0 || !IsIdentifierCharacter(expression[found - 1]);
            bool endsIdentifier = end == expression.Length || !IsIdentifierCharacter(expression[end]);
            if (startsIdentifier && endsIdentifier) return true;
            searchFrom = end;
        }
        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
}
