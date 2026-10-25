using System.Globalization;

namespace JadeView;

internal sealed record CinematicScriptCallbackRegistrationCandidate(
    string Operation,
    string CallbackSourceContext,
    string DestinationInstance,
    string CallbackSourceContextOperand,
    string DestinationInstanceOperand,
    uint? ResolvedContextKey,
    uint? ResolvedDestinationKey,
    string CallbackTypeOperand,
    int? CallbackType,
    string FunctionOperand,
    uint? FunctionKey,
    string FunctionName,
    string[] BranchConditions,
    string[] SerializedBranchFindings,
    string Source,
    int Line,
    string CallbackPath)
{
    internal string[] MatchingRemovalSites { get; init; } = [];
}

internal sealed record CinematicScriptCallbackResetCandidate(string Source, int Line,
    string CallbackPath, string[] BranchConditions, string[] SerializedBranchFindings);

internal static class CinematicScriptCallbackRegistrationAnalyzer
{
    private const int MinimumRegistrationArguments = 4;

    internal static CinematicScriptCallbackRegistrationCandidate[] Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> calls,
        IEnumerable<CinematicScriptReachableSourceReference> references)
    {
        ArgumentNullException.ThrowIfNull(owner);
        CinematicScriptReachableSourceReference[] sourceReferences = references.ToArray();
        CinematicScriptCallbackRegistrationCandidate[] candidates = calls
            .Where(call => call.Api is "AICBAdd" or "AICBDel")
            .Select(call => Analyze(owner, call, sourceReferences))
            .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
        CinematicScriptCallbackRegistrationCandidate[] removeCandidates = candidates
            .Where(candidate => candidate.Operation == "Remove candidate")
            .ToArray();
        return candidates.Select(candidate => candidate.Operation != "Register candidate"
                ? candidate
                : candidate with
                {
                    MatchingRemovalSites = removeCandidates
                        .Where(remove => HasSameResolvedTuple(candidate, remove))
                        .Select(remove => $"{remove.Source}:{remove.Line}")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                })
            .ToArray();
    }

    internal static CinematicScriptCallbackResetCandidate[] AnalyzeResetSites(
        CinematicScriptOwner owner, IEnumerable<CinematicScriptReachableEngineCall> calls)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return calls.Where(call => call.Api == "AIReinitVars")
            .Select(call => new CinematicScriptCallbackResetCandidate(call.Source, call.Line,
                call.CallbackPath,
                call.BranchConditions.Select(FormatBranch).ToArray(),
                CinematicScriptBranchAnalyzer.Describe(owner, call.BranchConditions)))
            .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
    }

    private static bool HasSameResolvedTuple(CinematicScriptCallbackRegistrationCandidate registration,
        CinematicScriptCallbackRegistrationCandidate removal) =>
        registration.Operation == "Register candidate"
        && removal.Operation == "Remove candidate"
        && registration.ResolvedContextKey is { } registrationContext
        && removal.ResolvedContextKey == registrationContext
        && registration.ResolvedDestinationKey is { } registrationDestination
        && removal.ResolvedDestinationKey == registrationDestination
        && registration.CallbackType is { } registrationType
        && removal.CallbackType == registrationType
        && registration.FunctionKey is { } registrationFunction
        && removal.FunctionKey == registrationFunction;

    private static CinematicScriptCallbackRegistrationCandidate Analyze(
        CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        CinematicScriptReachableSourceReference[] references)
    {
        string context = DescribeArgument(call, 0);
        string destination = DescribeArgument(call, 1);
        uint? contextKey = ResolveArgumentObjectKey(owner, call, 0, references);
        uint? destinationKey = ResolveArgumentObjectKey(owner, call, 1, references);
        string callbackTypeOperand = ArgumentAt(call, 2);
        string functionOperand = ArgumentAt(call, 3);
        int? callbackType = int.TryParse(callbackTypeOperand, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int parsedCallbackType)
            ? parsedCallbackType
            : null;
        uint? functionKey = ResolveFunctionKey(call, references);
        string functionName = functionKey is { } key
            ? CinematicScriptReader.FunctionNameFor(key) ?? $"function {key:X8}"
            : "unresolved callback function";
        string[] branches = call.BranchConditions
            .Select(condition => condition.Kind == "else"
                ? $"else of if ({condition.Expression}) at line {condition.Line}"
                : $"if ({condition.Expression}) at line {condition.Line}")
            .ToArray();
        string[] serializedBranchFindings = CinematicScriptBranchAnalyzer.Describe(owner,
            call.BranchConditions);

        return new(call.Api == "AICBAdd" ? "Register candidate" : "Remove candidate",
            context, destination, ArgumentAt(call, 0), ArgumentAt(call, 1), contextKey,
            destinationKey, callbackTypeOperand, callbackType, functionOperand, functionKey,
            functionName, branches, serializedBranchFindings, call.Source, call.Line,
            call.CallbackPath);
    }

    private static uint? ResolveArgumentObjectKey(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, int argumentIndex,
        IEnumerable<CinematicScriptReachableSourceReference> references)
    {
        if (CinematicScriptArgumentBindingResolver.ArgumentUsesCurrentGameObject(call, argumentIndex))
            return owner.OwnerKey;
        uint[] serializedOvaTargets = CinematicScriptArgumentBindingResolver
            .ResolveArgumentOwnerFields(call, argumentIndex, owner)
            .Where(binding => binding.ObjectKey != null)
            .Select(binding => binding.ObjectKey!.Value)
            .ToArray();
        uint[] sourceTargets = CinematicScriptArgumentBindingResolver
            .ResolveArgumentObjectReferences(call, argumentIndex, references)
            .Select(reference => reference.Key)
            .ToArray();
        uint[] matches = serializedOvaTargets.Concat(sourceTargets)
            .Distinct().Take(2).ToArray();
        return matches is [uint key]
            ? key
            : null;
    }

    private static uint? ResolveFunctionKey(CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> references)
    {
        if (call.Arguments.Length < MinimumRegistrationArguments) return null;
        string callbackArgument = call.Arguments[3];
        CinematicScriptReachableSourceReference[] matches = references
            .Where(reference => reference.Kind == "Function"
                && reference.FunctionName == call.FunctionName
                && ContainsSymbol(callbackArgument, call.FunctionName, reference.Symbol))
            .DistinctBy(reference => reference.Key)
            .ToArray();
        return matches.Length == 1 ? matches[0].Key : null;
    }

    private static string DescribeArgument(CinematicScriptReachableEngineCall call, int argumentIndex)
    {
        string argument = ArgumentAt(call, argumentIndex);
        if (argument.Length == 0) return "unresolved (missing argument)";
        string[] assignments = call.ArgumentAssignments
            .Where(assignment => ContainsIdentifier(argument, assignment.Variable))
            .Select(assignment =>
            {
                string confidence = assignment.IsUnique ? "assignment" : "ambiguous assignment";
                return $"{assignment.Variable} = {assignment.Expression} ({confidence}, line {assignment.Line})";
            })
            .Concat((call.ArgumentAssignmentAlternatives ?? [])
                .Where(assignment => ContainsIdentifier(argument, assignment.Variable))
                .Select(assignment =>
                {
                    string conditions = assignment.BranchConditions.Length == 0
                        ? "branch not recorded"
                        : string.Join(" and ", assignment.BranchConditions.Select(FormatBranch));
                    return $"{assignment.Variable} = {assignment.Expression} (alternative, {conditions}, line {assignment.Line})";
                }))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return assignments.Length == 0 ? argument : $"{argument} [{string.Join("; ", assignments)}]";
    }

    private static string FormatBranch(CinematicScriptBranchConditionSource condition) =>
        condition.Kind == "else"
            ? $"else of if ({condition.Expression}) at line {condition.Line}"
            : $"if ({condition.Expression}) at line {condition.Line}";

    private static string ArgumentAt(CinematicScriptReachableEngineCall call, int argumentIndex) =>
        call.Arguments.Length > argumentIndex ? call.Arguments[argumentIndex] : string.Empty;

    private static bool ContainsSymbol(string expression, string functionName, string symbol) =>
        ContainsIdentifier(expression, symbol)
        || ContainsIdentifier(expression, $"fct_{functionName}_{symbol}");

    private static bool ContainsIdentifier(string expression, string identifier)
    {
        if (identifier.Length == 0) return false;
        int searchFrom = 0;
        while (searchFrom < expression.Length)
        {
            int found = expression.IndexOf(identifier, searchFrom, StringComparison.Ordinal);
            if (found < 0) return false;
            int end = found + identifier.Length;
            bool beginsIdentifier = found == 0 || !IsIdentifierCharacter(expression[found - 1]);
            bool endsIdentifier = end == expression.Length || !IsIdentifierCharacter(expression[end]);
            if (beginsIdentifier && endsIdentifier) return true;
            searchFrom = end;
        }
        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
}
