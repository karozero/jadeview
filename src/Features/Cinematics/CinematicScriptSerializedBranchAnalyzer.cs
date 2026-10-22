using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal static partial class CinematicScriptBranchAnalyzer
{
    [GeneratedRegex(@"(?<negation>!\s*)?AI_EvalFunc_OBJ_FlagsIdentityTest_C\s*\(\s*(?:AI_gpst_CurrentGameObject|AI_gpst_CurrentUltra)\s*,\s*(?<mask>0[xX][0-9A-Fa-f]+|[0-9]+)[uUlL]*\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentOwnerIdentityFlagTest();

    [GeneratedRegex(@"(?<negation>!\s*)?AI_EvalFunc_OBJ_CapaTest_C(?:_CURRENT)?\s*\(\s*(?:(?:AI_gpst_CurrentGameObject|AI_gpst_CurrentUltra)\s*,\s*)?(?<mask>0[xX][0-9A-Fa-f]+|[0-9]+)[uUlL]*\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentOwnerCapabilityTest();

    internal static string[] Describe(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptBranchConditionSource> branchConditions)
    {
        var findings = new List<string>();
        foreach (var branch in branchConditions)
        {
            foreach (Match capabilityMatch in CurrentOwnerCapabilityTest().Matches(branch.Expression))
            {
                if (!TryParseMask(capabilityMatch.Groups["mask"].Value, out uint rawMask)
                    || rawMask > ushort.MaxValue)
                    continue;

                ushort capabilityMask = (ushort)rawMask;
                string polarity = capabilityMatch.Groups["negation"].Success ? "negated " : "";
                findings.Add($"This branch uses a {polarity}runtime capability test for mask 0x{capabilityMask:X4} "
                    + "on the current owner; Iris passes when any masked capability bit is set. "
                    + "The archive does not store the live capability value, so this branch result is unknown.");
            }

            Match match = CurrentOwnerIdentityFlagTest().Match(branch.Expression);
            if (!match.Success || !TryParseMask(match.Groups["mask"].Value, out uint flagMask))
                continue;

            bool predicateIsTrue = (owner.SerializedGaoFlags & flagMask) != 0;
            bool isNegated = match.Groups["negation"].Success;
            bool branchIsActive = predicateIsTrue != isNegated;
            if (branch.Kind.Equals("else", StringComparison.OrdinalIgnoreCase))
                branchIsActive = !branchIsActive;

            string branchKind = branch.Kind.Equals("else", StringComparison.OrdinalIgnoreCase)
                ? "else branch" : "if branch";
            findings.Add($"Serialized identity-flag check 0x{flagMask:X8} makes this {branchKind} "
                + $"{(branchIsActive ? "true" : "false")} for GAO flags 0x{owner.SerializedGaoFlags:X8}; "
                + "runtime flag changes before callback execution are not tracked.");
        }

        return findings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool TryParseMask(string value, out uint flagMask)
    {
        bool isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        string digits = isHex ? value[2..] : value;
        NumberStyles style = isHex ? NumberStyles.HexNumber : NumberStyles.None;
        return uint.TryParse(digits, style, CultureInfo.InvariantCulture, out flagMask);
    }
}
