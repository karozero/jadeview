namespace JadeView;

internal sealed record CinematicScriptRliDisplayCallbackCandidate(
    uint? ContextKey,
    uint? TargetKey,
    int CallbackType,
    string FunctionName,
    string FactorExpression,
    string DestinationColorExpression,
    bool? FactorIsUsed,
    bool HasPairedCallback,
    string Source,
    int Line,
    string[] BranchConditions,
    string CallbackPath)
{
    internal bool HasMatchingRemovalSite { get; init; }
    internal string[] EffectBranchConditions { get; init; } = [];
}

/// <summary>
/// Correlates dynamic per-object Before/After Display registrations with source
/// calls that manipulate the renderer's RLI state.
/// </summary>
internal static class CinematicScriptRliDisplayCallbackAnalyzer
{
    internal static CinematicScriptRliDisplayCallbackCandidate[] Analyze(
        CinematicScriptOwner owner,
        IReadOnlyList<CinematicScriptReachableEngineCall>? calls = null,
        IReadOnlyList<CinematicScriptReachableSourceReference>? references = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        calls ??= CinematicScriptReader.ReachableEngineCalls(owner);
        references ??= CinematicScriptReader.ReachableSourceReferences(owner);

        CinematicScriptCallbackRegistrationCandidate[] registrations =
            CinematicScriptCallbackRegistrationAnalyzer.Analyze(owner, calls, references)
                .Where(candidate => candidate.Operation == "Register candidate"
                    && candidate.CallbackType is 0 or 1
                    && candidate.FunctionKey != null)
                .ToArray();
        return registrations.Select(registration => CreateCandidate(registration,
                registrations, calls))
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.TargetKey)
            .ThenBy(candidate => candidate.CallbackType)
            .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
    }

    private static CinematicScriptRliDisplayCallbackCandidate? CreateCandidate(
        CinematicScriptCallbackRegistrationCandidate registration,
        IReadOnlyList<CinematicScriptCallbackRegistrationCandidate> registrations,
        IReadOnlyList<CinematicScriptReachableEngineCall> calls)
    {
        CinematicScriptReachableEngineCall[] effectCalls = calls
            .Where(call => call.FunctionName == registration.FunctionName
                && call.Api is "LightRLIFactorUsed" or "LightSetRLIFactor" or "LightSetRLIDestColor")
            .ToArray();
        if (effectCalls.Length == 0) return null;

        string factorExpression = SingleArgument(effectCalls, "LightSetRLIFactor");
        string colorExpression = SingleArgument(effectCalls, "LightSetRLIDestColor");
        bool? factorIsUsed = ReadFactorUsage(effectCalls);
        string[] effectBranches = effectCalls.SelectMany(call => call.BranchConditions)
            .Select(FormatBranch)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        bool hasPairedCallback = registrations.Any(candidate =>
            candidate.ResolvedContextKey == registration.ResolvedContextKey
            && candidate.ResolvedDestinationKey == registration.ResolvedDestinationKey
            && candidate.CallbackType == 1 - registration.CallbackType
            && candidate.FunctionKey != registration.FunctionKey);

        return new(registration.ResolvedContextKey, registration.ResolvedDestinationKey,
            registration.CallbackType!.Value, registration.FunctionName, factorExpression,
            colorExpression, factorIsUsed, hasPairedCallback, registration.Source,
            registration.Line, registration.BranchConditions, registration.CallbackPath)
        {
            HasMatchingRemovalSite = registration.MatchingRemovalSites.Length > 0,
            EffectBranchConditions = effectBranches
        };
    }

    private static string SingleArgument(IReadOnlyList<CinematicScriptReachableEngineCall> calls,
        string api)
    {
        string[] expressions = calls.Where(call => call.Api == api && call.Arguments.Length > 0)
            .Select(call => call.Arguments[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return expressions.Length switch
        {
            0 => "not set by this callback",
            1 => expressions[0],
            _ => $"multiple values: {string.Join("; ", expressions)}"
        };
    }

    private static bool? ReadFactorUsage(IReadOnlyList<CinematicScriptReachableEngineCall> calls)
    {
        string[] values = calls.Where(call => call.Api == "LightRLIFactorUsed"
                && call.Arguments.Length > 0)
            .Select(call => call.Arguments[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length != 1) return null;
        return values[0] switch
        {
            "0" => false,
            "1" => true,
            _ => null
        };
    }

    private static string FormatBranch(CinematicScriptBranchConditionSource condition) =>
        condition.Kind == "else"
            ? $"else of if ({condition.Expression}) at line {condition.Line}"
            : $"if ({condition.Expression}) at line {condition.Line}";
}
