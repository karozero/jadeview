using System.Globalization;

namespace JadeView;

internal sealed record CinematicScriptAiTrackMutationCandidate(
    string Api,
    string Effect,
    int? TrackIndex,
    string TrackDescription,
    string FunctionOperand,
    uint? FunctionKey,
    string FunctionDescription,
    string[] BranchConditions,
    string[] SerializedBranchFindings,
    int[] CurrentTrackSlotCandidates,
    bool HasUnknownCurrentTrackPath,
    string Source,
    int Line,
    string CallbackPath);

internal static class CinematicScriptAiTrackMutationAnalyzer
{
    private const int TrackCount = 5;

    internal static CinematicScriptAiTrackMutationCandidate[] Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> calls,
        IEnumerable<CinematicScriptReachableSourceReference> references,
        IReadOnlyDictionary<string, CinematicScriptAiTrackSlotProvenance>? trackSlotProvenance = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        CinematicScriptReachableSourceReference[] sourceReferences = references.ToArray();
        trackSlotProvenance ??= CinematicScriptReader.PossibleCallbackTrackSlots(owner);
        return calls.Where(call => IsTrackMutation(call.Api))
            .Select(call => Analyze(owner, call, sourceReferences, trackSlotProvenance))
            .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
    }

    private static CinematicScriptAiTrackMutationCandidate Analyze(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call,
        CinematicScriptReachableSourceReference[] references,
        IReadOnlyDictionary<string, CinematicScriptAiTrackSlotProvenance> trackSlotProvenance)
    {
        int trackArgumentIndex = call.Api is "AITrackChange" or "AITrackStop" ? 0 : -1;
        int functionArgumentIndex = call.Api switch
        {
            "AITrackChange" => 1,
            "AITrackCurChange" or "AITrackCurChangeNow" => 0,
            _ => -1
        };
        string trackOperand = ArgumentAt(call, trackArgumentIndex);
        int? trackIndex = TryResolveLiteralIndex(trackOperand);
        CinematicScriptAiTrackSlotProvenance? currentTrackProvenance =
            trackSlotProvenance.GetValueOrDefault(call.FunctionName);
        string trackDescription = DescribeTrack(call.Api, trackOperand, trackIndex, currentTrackProvenance);
        string functionOperand = ArgumentAt(call, functionArgumentIndex);
        uint? functionKey = ResolveFunctionKey(call, functionOperand, references);
        string functionDescription = functionKey is { } key
            ? CinematicScriptReader.FunctionNameFor(key) ?? $"function {key:X8}"
            : functionArgumentIndex < 0
                ? "not applicable"
                : "unresolved function target";
        string[] branches = call.BranchConditions.Select(FormatBranch).ToArray();

        return new(call.Api, DescribeEffect(call.Api), trackIndex, trackDescription,
            functionOperand, functionKey, functionDescription, branches,
            CinematicScriptBranchAnalyzer.Describe(owner, call.BranchConditions),
            currentTrackProvenance?.CandidateSlots ?? [], currentTrackProvenance?.HasUnknownPath ?? true,
            call.Source, call.Line, call.CallbackPath);
    }

    private static string DescribeTrack(string api, string operand, int? trackIndex,
        CinematicScriptAiTrackSlotProvenance? currentTrackProvenance)
    {
        if (api is "AITrackCurChange" or "AITrackCurChangeNow" or "AITrackCurStop")
            return DescribeCurrentTrack(currentTrackProvenance);
        if (trackIndex is { } index)
            return index is >= 0 and < TrackCount
                ? $"track {index}"
                : $"track index {index} is outside 0–{TrackCount - 1}";
        return string.IsNullOrEmpty(operand)
            ? "track index unresolved"
            : $"track index unresolved ({operand})";
    }

    private static string DescribeCurrentTrack(CinematicScriptAiTrackSlotProvenance? provenance)
    {
        if (provenance == null || provenance.CandidateSlots.Length == 0)
            return "current track slot unknown from available source paths";
        string slots = string.Join(", ", provenance.CandidateSlots);
        return provenance.HasUnknownPath
            ? $"possible current track slot(s) {slots}; other source paths unresolved"
            : $"possible current track slot(s) {slots}";
    }

    private static int? TryResolveLiteralIndex(string operand)
    {
        if (int.TryParse(operand, NumberStyles.Integer, CultureInfo.InvariantCulture, out int decimalValue))
            return decimalValue;
        if (operand.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(operand.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int hexadecimalValue))
            return hexadecimalValue;
        return null;
    }

    private static uint? ResolveFunctionKey(CinematicScriptReachableEngineCall call,
        string functionOperand, IEnumerable<CinematicScriptReachableSourceReference> references)
    {
        if (functionOperand.Length == 0) return null;
        uint[] keys = references.Where(reference => reference.Kind == "Function"
                && reference.FunctionName == call.FunctionName
                && ContainsSymbol(functionOperand, call.FunctionName, reference.Symbol))
            .Select(reference => reference.Key)
            .Distinct()
            .Take(2)
            .ToArray();
        return keys.Length == 1 && keys[0] is not (0 or uint.MaxValue) ? keys[0] : null;
    }

    private static string DescribeEffect(string api) => api switch
    {
        "AITrackChange" => "May replace a different numbered track function and reset its function cursor; it does not set clip time.",
        "AITrackStop" => "May clear the numbered track function and cursor without clearing its slot timer.",
        "AITrackCurChange" => "May replace a different current-track function and reset its cursor; same-target calls do nothing, with no immediate same-slot re-entry.",
        "AITrackCurChangeNow" => "May replace the current track and request immediate same-slot re-entry when the target differs; same-target calls continue without restarting.",
        "AITrackCurStop" => "May clear the current track function and cursor without clearing its slot timer.",
        _ => string.Empty
    };

    private static bool IsTrackMutation(string api) => api is
        "AITrackChange" or "AITrackStop" or "AITrackCurChange" or "AITrackCurChangeNow" or "AITrackCurStop";

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
            bool startsIdentifier = found == 0 || !IsIdentifierCharacter(expression[found - 1]);
            bool endsIdentifier = end == expression.Length || !IsIdentifierCharacter(expression[end]);
            if (startsIdentifier && endsIdentifier) return true;
            searchFrom = end;
        }
        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';

    private static string FormatBranch(CinematicScriptBranchConditionSource condition) =>
        condition.Kind == "else"
            ? $"else of if ({condition.Expression}) at line {condition.Line}"
            : $"if ({condition.Expression}) at line {condition.Line}";

    private static string ArgumentAt(CinematicScriptReachableEngineCall call, int argumentIndex) =>
        argumentIndex >= 0 && call.Arguments.Length > argumentIndex ? call.Arguments[argumentIndex] : string.Empty;
}
