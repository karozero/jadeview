using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal static partial class CinematicScriptFlagFlowAnalyzer
{
    // Iris resolves Flag_speciaux_sauve with 4488 bytes; the first four bytes are
    // skipped before passing the int* to ARRBit* calls, leaving 4484 * 8 bit indices.
    private const int SavedFlagBufferBytes = 4488;
    private const int SavedFlagValueOffsetBytes = sizeof(int);
    private const int SavedFlagIndexLimit = (SavedFlagBufferBytes - SavedFlagValueOffsetBytes) * 8;

    private static readonly Regex GlobalFlagFieldExpression = new(
        @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*(?<operator>[+-])?\s*(?<offset>0[xX][0-9A-Fa-f]+|\d+)?\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex GlobalFlagIdentifier = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private static readonly Regex GlobalFlagTestComparison = new(
        @"AI_EvalFunc_(?:ARRGlobalIntFlagTest|ARRBitTest)_C\s*\([^)]*\)\s*(?<operator>==|!=)\s*(?<expected>[A-Za-z_][A-Za-z0-9_]*\s*->\s*[A-Za-z_][A-Za-z0-9_]*|[-+]?\d+)",
        RegexOptions.CultureInvariant);
    private static readonly Regex GlobalBitsetTest = new(
        @"(?<negated>!)?\s*AI_EvalFunc_ARRBitTest_C\s*\(", RegexOptions.CultureInvariant);

    private static void AddSourceGlobalFlagAccesses(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner)
    {
        if (!CinematicScriptReader.HasSourceGlobalFlagApi(owner)) return;

        foreach (var call in CinematicScriptReader.ReachableEngineCalls(owner))
        {
            if (call.Api is "ARRBitSetRange" or "ARRBitClearRange")
            {
                accesses.AddRange(ResolveSavedFlagRangeAccesses(owner, call));
                continue;
            }

            bool isGlobalBitsetAccess = call.Api is "ARRBitTest" or "ARRBitSet" or "ARRBitClear";
            string? access = call.Api switch
            {
                "ARRGlobalIntFlagTest" or "ARRBitTest" => "reads",
                "ARRGlobalIntFlagSet" or "ARRGlobalIntFlagClear" or "ARRBitSet" or "ARRBitClear" => "writes",
                _ => null
            };
            int flagArgumentIndex = isGlobalBitsetAccess ? 1 : 0;
            if (access == null || call.Arguments.Length <= flagArgumentIndex
                || isGlobalBitsetAccess && !ReferencesSavedFlagStorage(call, call.Arguments[0])
                || !TryResolveGlobalFlagArgument(owner, call, call.Arguments[flagArgumentIndex], out int flag)
                || flag <= 0)
                continue;

            string effect = call.Api switch
            {
                "ARRGlobalIntFlagTest" or "ARRBitTest" => "tests",
                "ARRGlobalIntFlagSet" or "ARRBitSet" => "sets",
                _ => "clears"
            };
            string branchContext = call.BranchConditions.Length == 0
                ? "no enclosing source branch recorded"
                : string.Join(" and ", call.BranchConditions.Select(branch => branch.Kind == "else"
                    ? $"else of if ({branch.Expression})" : $"if ({branch.Expression})"));
            bool? requiredSet = access == "reads"
                ? TryResolveGlobalFlagTestPredicate(owner, call) : null;
            string predicateContext = requiredSet is { } predicateState
                ? $"; configured comparison requires the flag {(predicateState ? "set" : "clear")}" : string.Empty;
            bool? writtenSet = call.Api switch
            {
                "ARRGlobalIntFlagSet" or "ARRBitSet" => true,
                "ARRGlobalIntFlagClear" or "ARRBitClear" => false,
                _ => null
            };
            string storageDescription = isGlobalBitsetAccess
                ? $" through saved-flag storage {call.Arguments[0]}" : string.Empty;
            string evidence = $"Iris {call.Api} {effect} this global flag{storageDescription} in {call.FunctionName} "
                + $"at {call.Source}:{call.Line}; {branchContext}; argument: {call.Arguments[flagArgumentIndex]}"
                + predicateContext;
            accesses.Add(new(flag, access, call.Api, evidence, requiredSet, writtenSet));
        }
    }

    internal static CinematicScriptFlagAccess[] ResolveSavedFlagRangeAccesses(
        CinematicScriptOwner owner, CinematicScriptReachableEngineCall call)
    {
        bool isSetRange = call.Api == "ARRBitSetRange";
        bool isClearRange = call.Api == "ARRBitClearRange";
        if ((!isSetRange && !isClearRange) || call.Arguments.Length < 3
            || !ReferencesSavedFlagStorage(call, call.Arguments[0])
            || !TryResolveGlobalFlagArgument(owner, call, call.Arguments[1], out int firstFlag)
            || !TryResolveGlobalFlagArgument(owner, call, call.Arguments[2], out int lastFlag)
            || firstFlag <= 0 || lastFlag < firstFlag || lastFlag >= SavedFlagIndexLimit)
            return [];

        bool writtenSet = isSetRange;
        string operation = writtenSet ? "sets" : "clears";
        string branchContext = call.BranchConditions.Length == 0
            ? "no enclosing source branch recorded"
            : string.Join(" and ", call.BranchConditions.Select(branch => branch.Kind == "else"
                ? $"else of if ({branch.Expression})" : $"if ({branch.Expression})"));
        int rangeLength = checked(lastFlag - firstFlag + 1);
        return Enumerable.Range(firstFlag, rangeLength).Select(flag => new CinematicScriptFlagAccess(
            flag, "writes", call.Api,
            $"Iris {call.Api} {operation} this saved flag in {call.FunctionName} "
            + $"at {call.Source}:{call.Line}; inclusive range {firstFlag}..{lastFlag}; "
            + $"buffer resolves to Flag_speciaux_sauve; {branchContext}",
            WrittenSet: writtenSet)).ToArray();
    }

    private static bool? TryResolveGlobalFlagTestPredicate(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call)
    {
        foreach (var branch in call.BranchConditions)
        {
            if (branch.Expression.Contains("||", StringComparison.Ordinal)) continue;
            Match comparison = GlobalFlagTestComparison.Match(branch.Expression);
            if (!comparison.Success
                || !TryResolveGlobalFlagArgument(owner, call, comparison.Groups["expected"].Value,
                    out int expectedValue)
                || expectedValue is not (0 or 1))
                continue;

            bool testedValue = expectedValue == 1;
            bool requiredSet = comparison.Groups["operator"].Value == "=="
                ? testedValue : !testedValue;
            return branch.Kind == "else" ? !requiredSet : requiredSet;
        }

        foreach (var branch in call.BranchConditions)
        {
            // A simple conjunction can still impose the test's polarity. An OR can be
            // satisfied without this test, so it is intentionally left unresolved.
            if (branch.Expression.Contains("||", StringComparison.Ordinal)) continue;
            Match bitTest = GlobalBitsetTest.Match(branch.Expression);
            if (!bitTest.Success) continue;
            bool requiredSet = !bitTest.Groups["negated"].Success;
            return branch.Kind == "else" ? !requiredSet : requiredSet;
        }

        return null;
    }

    private static bool ReferencesSavedFlagStorage(CinematicScriptReachableEngineCall call,
        string expression)
    {
        return ReferencesSavedFlagStorage(call, expression, 0, []);
    }

    private static bool ReferencesSavedFlagStorage(CinematicScriptReachableEngineCall call,
        string expression, int depth, HashSet<string> visitedVariables)
    {
        if (depth > 4) return false;
        if (expression.Contains("Flag_speciaux_sauve", StringComparison.OrdinalIgnoreCase)) return true;

        Match identifier = Regex.Match(expression.Trim(), @"^[*&()\s]*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[)]*$",
            RegexOptions.CultureInvariant);
        if (!identifier.Success) return false;
        string variable = identifier.Groups["name"].Value;
        if (!visitedVariables.Add(variable)) return false;
        var assignment = call.ArgumentAssignments
            .Where(candidate => candidate.Variable == variable && candidate.Line <= call.Line)
            .OrderByDescending(candidate => candidate.Line)
            .FirstOrDefault();
        return assignment != null && ReferencesSavedFlagStorage(call, assignment.Expression,
            depth + 1, visitedVariables);
    }

    internal static bool TryResolveGlobalFlagArgument(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, string expression, out int value)
    {
        return TryResolveGlobalFlagArgument(owner, call, expression, 0, [], out value);
    }

    private static bool TryResolveGlobalFlagArgument(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, string expression, int depth,
        HashSet<string> visitedVariables, out int value)
    {
        value = 0;
        if (depth > 4) return false;

        string normalized = TrimOuterParentheses(expression.Trim());
        if (TryParseInteger(normalized, out value)) return true;

        Match fieldMatch = GlobalFlagFieldExpression.Match(normalized);
        if (fieldMatch.Success)
        {
            CinematicScriptVariable? variable = owner.EffectiveVariable(fieldMatch.Groups["field"].Value);
            if (variable == null || !TryParseInteger(variable.Value, out int baseValue)) return false;

            if (fieldMatch.Groups["offset"].Success)
            {
                if (!TryParseInteger(fieldMatch.Groups["offset"].Value, out int offset)) return false;
                try
                {
                    baseValue = fieldMatch.Groups["operator"].Value == "-"
                        ? checked(baseValue - offset) : checked(baseValue + offset);
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            value = baseValue;
            return true;
        }

        if (!GlobalFlagIdentifier.IsMatch(normalized) || !visitedVariables.Add(normalized)) return false;
        var assignment = call.ArgumentAssignments
            .Where(candidate => candidate.Variable == normalized && candidate.Line <= call.Line)
            .OrderByDescending(candidate => candidate.Line)
            .FirstOrDefault();
        return assignment is { IsUnique: true } && TryResolveGlobalFlagArgument(owner, call,
            assignment.Expression, depth + 1, visitedVariables, out value);
    }

    private static string TrimOuterParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')')
        {
            int depth = 0;
            bool enclosesWholeExpression = true;
            for (int index = 0; index < expression.Length; index++)
            {
                if (expression[index] == '(') depth++;
                else if (expression[index] == ')') depth--;
                if (depth == 0 && index < expression.Length - 1)
                {
                    enclosesWholeExpression = false;
                    break;
                }
            }
            if (!enclosesWholeExpression || depth != 0) break;
            expression = expression[1..^1].Trim();
        }
        return expression;
    }

    private static bool TryParseInteger(string value, out int result)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out result);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
