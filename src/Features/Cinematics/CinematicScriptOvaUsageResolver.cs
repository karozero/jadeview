namespace JadeView;

internal static class CinematicScriptOvaUsageResolver
{
    internal static CinematicScriptOvaFieldUsage[] Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall>? reachableCalls = null)
    {
        var knownFields = owner.Variables.Select(variable => variable.Name)
            .Concat(owner.ObjectReferences.Select(reference => reference.VariableName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (knownFields.Length == 0) return [];

        var usages = new List<CinematicScriptOvaFieldUsage>();
        if (reachableCalls == null)
        {
            foreach (var access in CinematicScriptReader.ReachableOvaFieldAccesses(owner))
            {
                if (!knownFields.Contains(access.VariableName, StringComparer.OrdinalIgnoreCase)) continue;
                usages.Add(new(access.VariableName, access.Access, access.FunctionName,
                    access.CallbackPath, access.SourceFile, access.Line, [access.Expression], [],
                    access.BranchConditions));
            }
        }

        foreach (var call in reachableCalls ?? CinematicScriptReader.ReachableEngineCalls(owner))
        {
            string[] readExpressions = call.Arguments
                .Concat(call.ArgumentAssignments.Select(assignment => assignment.Expression))
                .Concat(call.ArgumentAssignmentAlternatives?.Select(assignment => assignment.Expression) ?? [])
                .Concat(call.BranchConditions.Select(condition => condition.Expression))
                .Concat(call.MessageFields.Select(field => field.Expression))
                .ToArray();
            var parserBoundFields = call.ArgumentFields.Select(field => field.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var writtenFields = (call.OvaFieldWrites ?? [])
                .Select(write => write.VariableName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string fieldName in knownFields)
            {
                bool isRead = parserBoundFields.Contains(fieldName)
                    || readExpressions.Any(expression => ReferencesMember(expression, fieldName));
                bool isWritten = writtenFields.Contains(fieldName);
                if (!isRead && !isWritten) continue;

                var expressions = readExpressions
                    .Where(expression => ReferencesMember(expression, fieldName))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var matchingWrites = (call.OvaFieldWrites ?? [])
                    .Where(write => write.VariableName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                string access = isRead && isWritten ? "read/write" : isWritten ? "write" : "read";
                usages.Add(new(fieldName, access, call.FunctionName, call.CallbackPath,
                    call.Source, call.Line, expressions, matchingWrites, call.BranchConditions));
            }
        }

        return usages.DistinctBy(usage => (usage.VariableName, usage.Access,
                usage.FunctionName, usage.Source, usage.Line))
            .OrderBy(usage => usage.VariableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.Source, StringComparer.Ordinal)
            .ThenBy(usage => usage.Line)
            .ToArray();
    }

    private static bool ReferencesMember(string expression, string fieldName) =>
        FindMemberReference(expression, fieldName);

    private static bool FindMemberReference(string expression, string fieldName)
    {
        int searchFrom = 0;
        while (searchFrom < expression.Length)
        {
            int fieldStart = expression.IndexOf(fieldName, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (fieldStart < 0) return false;
            int fieldEnd = fieldStart + fieldName.Length;
            bool fieldEndsCleanly = fieldEnd == expression.Length || !IsIdentifierCharacter(expression[fieldEnd]);
            int operatorEnd = fieldStart - 1;
            while (operatorEnd >= 0 && char.IsWhiteSpace(expression[operatorEnd])) operatorEnd--;
            bool hasMemberOperator = operatorEnd >= 0 && expression[operatorEnd] == '.'
                || operatorEnd >= 1 && expression[operatorEnd] == '>' && expression[operatorEnd - 1] == '-';
            if (fieldEndsCleanly && hasMemberOperator) return true;
            searchFrom = fieldEnd;
        }
        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
}

internal sealed record CinematicScriptOvaFieldUsage(string VariableName, string Access,
    string FunctionName, string CallbackPath, string Source, int Line, string[] SourceExpressions,
    CinematicScriptOvaFieldWrite[] Writes, CinematicScriptBranchConditionSource[] BranchConditions);
