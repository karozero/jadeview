namespace JadeView;

internal static class CinematicScriptArgumentBindingResolver
{
    internal static CinematicScriptReachableSourceReference[] ResolveSourceObjectReferences(
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences)
    {
        string[] argumentFlow = call.Arguments.Concat(call.ArgumentAssignments.Select(assignment => assignment.Expression))
            .Concat(call.ArgumentAssignmentAlternatives?.Select(assignment => assignment.Expression) ?? []).ToArray();
        return ResolveSourceObjectReferences(call, sourceReferences, argumentFlow);
    }

    internal static CinematicScriptReachableSourceReference[] ResolveFirstArgumentObjectReferences(
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences)
    {
        return ResolveArgumentObjectReferences(call, 0, sourceReferences);
    }

    internal static bool FirstArgumentUsesCurrentGameObject(CinematicScriptReachableEngineCall call)
        => ArgumentUsesCurrentGameObject(call, 0);

    internal static CinematicScriptReachableSourceReference[] ResolveArgumentObjectReferences(
        CinematicScriptReachableEngineCall call, int argumentIndex,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences)
    {
        if (argumentIndex < 0 || call.Arguments.Length <= argumentIndex) return [];
        string[] argumentFlow = ResolveArgumentFlow(call, argumentIndex);
        if (argumentFlow.Any(expression => ContainsIdentifier(expression, "AI_MainActor"))) return [];
        return ResolveSourceObjectReferences(call, sourceReferences, argumentFlow);
    }

    internal static bool ArgumentUsesCurrentGameObject(CinematicScriptReachableEngineCall call,
        int argumentIndex) => argumentIndex >= 0 && call.Arguments.Length > argumentIndex
        && ResolveArgumentFlow(call, argumentIndex)
            .Any(expression => ContainsIdentifier(expression, "AI_gpst_CurrentGameObject"));

    private static string[] ResolveArgumentFlow(CinematicScriptReachableEngineCall call, int argumentIndex)
    {
        string argument = call.Arguments[argumentIndex];
        var expressions = new List<string> { argument };
        var unresolvedVariables = new Queue<string>(ExtractIdentifiers(argument));
        var visitedVariables = new HashSet<string>(StringComparer.Ordinal);
        while (unresolvedVariables.TryDequeue(out string? variable))
        {
            if (!visitedVariables.Add(variable)) continue;
            CinematicScriptArgumentAssignmentSource[] assignments = call.ArgumentAssignments
                .Where(assignment => assignment.IsUnique && assignment.Variable == variable)
                .ToArray();
            if (assignments.Length != 1) continue;
            string expression = assignments[0].Expression;
            expressions.Add(expression);
            foreach (string dependency in ExtractIdentifiers(expression))
                if (!visitedVariables.Contains(dependency)) unresolvedVariables.Enqueue(dependency);
        }
        return expressions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ExtractIdentifiers(string expression)
    {
        for (int index = 0; index < expression.Length;)
        {
            if (!IsIdentifierStart(expression[index]))
            {
                index++;
                continue;
            }
            int identifierStart = index++;
            while (index < expression.Length && IsIdentifierCharacter(expression[index])) index++;
            yield return expression[identifierStart..index];
        }
    }

    private static CinematicScriptReachableSourceReference[] ResolveSourceObjectReferences(
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences,
        IEnumerable<string> argumentFlow)
    {
        return sourceReferences.Where(reference => reference.Kind == "Object"
                && CinematicObjectKey.IsArchiveObject(reference.Key)
                && reference.FunctionName == call.FunctionName
                && UsesIdentifier(argumentFlow, $"{call.FunctionName}_{reference.Symbol}",
                    $"fct_{call.FunctionName}_{reference.Symbol}"))
            .DistinctBy(reference => reference.Key)
            .OrderBy(reference => reference.Symbol, StringComparer.Ordinal)
            .ToArray();
    }

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
                || call.ArgumentAssignmentAlternatives?.Any(assignment =>
                    ReferencesMemberField(assignment.Expression, fieldName)) == true
                || call.BranchConditions.Any(condition =>
                    ReferencesMemberField(condition.Expression, fieldName))
                || UsesIdentifier(call.Arguments, fieldName))
            .OrderBy(fieldName => fieldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return referencedFieldNames.Select(fieldName => BindField(owner, fieldName))
            .Where(binding => binding != null)
            .Select(binding => binding!)
            .ToArray();
    }

    internal static CinematicScriptOvaFieldBinding[] ResolveArgumentOwnerFields(
        CinematicScriptReachableEngineCall call, int argumentIndex, CinematicScriptOwner owner)
    {
        if (argumentIndex < 0 || argumentIndex >= call.Arguments.Length) return [];
        string[] argumentFlow = ResolveArgumentFlow(call, argumentIndex);
        return ResolveOwnerFields(call, owner)
            .Where(binding => argumentFlow.Any(expression =>
                ReferencesMemberField(expression, binding.VariableName)
                || ContainsIdentifier(expression, binding.VariableName)))
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

    private static bool IsIdentifierStart(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
}
