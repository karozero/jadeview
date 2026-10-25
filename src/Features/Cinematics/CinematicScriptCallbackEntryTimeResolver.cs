using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Carries source-written elapsed-clock fields into a uniquely resolved callback
/// entered through a script track replacement.
/// </summary>
internal static class CinematicScriptCallbackEntryTimeResolver
{
    internal const string RelativeClockAliasPrefix = "$relative-main-clock:";

    private static readonly Regex MemberField = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);

    internal static Dictionary<string, double?> Resolve(
        IReadOnlyList<CinematicScriptReachableOvaFieldAccess> accesses,
        string destinationFunction, string sourceFunction, int dispatchLine,
        float callbackStart,
        Func<CinematicScriptReachableOvaFieldAccess, bool> isDefinitelyActive)
    {
        var runtimeInputs = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        string[] fieldsRead = accesses
            .Where(access => access.FunctionName == destinationFunction
                && access.Access == "read")
            .SelectMany(access => MemberField.Matches(access.Expression)
                .Select(match => match.Groups["field"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string fieldName in fieldsRead)
        {
            CinematicScriptReachableOvaFieldAccess[] clockWrites = accesses
                .Where(access => access.FunctionName == sourceFunction
                    && access.Access == "write"
                    && access.VariableName.Equals(fieldName, StringComparison.OrdinalIgnoreCase)
                    && access.Line < dispatchLine
                    && IsElapsedMainClock(access.Expression))
                .Where(isDefinitelyActive)
                .ToArray();
            if (clockWrites.Length == 1)
            {
                runtimeInputs[fieldName] = callbackStart;
                runtimeInputs[RelativeClockAliasPrefix + fieldName] = 1;
            }
        }

        return runtimeInputs;
    }

    internal static bool TryResolveDispatchCallLine(
        CinematicScriptCallbackPathStep? transition,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        out int dispatchLine)
    {
        dispatchLine = 0;
        if (transition == null
            || !transition.Dispatch.Equals("replace-current-track", StringComparison.Ordinal))
            return false;

        CinematicScriptReachableEngineCall[] candidateCalls = engineCalls
            .Where(call => call.FunctionName == transition.FromFunction
                && call.Api.StartsWith("AITrackCurChangeNow", StringComparison.Ordinal))
            .ToArray();
        if (candidateCalls.Length == 1)
        {
            dispatchLine = candidateCalls[0].Line;
            return true;
        }

        CinematicScriptReachableEngineCall[] matchingCalls = candidateCalls
            .Where(call => transition.ReferenceVariable is { Length: > 0 }
                && call.ArgumentAssignments.Any(assignment => assignment.Variable.Equals(
                    transition.ReferenceVariable, StringComparison.Ordinal))
                || transition.ResolvedFunctionKey is { Length: > 0 } functionKey
                && call.ArgumentAssignments.Any(assignment => assignment.Expression.Contains(
                    functionKey, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matchingCalls.Length != 1) return false;
        dispatchLine = matchingCalls[0].Line;
        return true;
    }

    private static bool IsElapsedMainClock(string expression) =>
        expression.Contains("TIM_gf_MainClock", StringComparison.Ordinal)
        && expression.Contains("TIM_gf_LockTime", StringComparison.Ordinal)
        && expression.Contains('-');
}
