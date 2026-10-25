using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal static class CinematicScriptScalarValueFlowEvaluator
{
    private static readonly Regex MemberReference = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex VectorComponentReference = new(
        @"\b(?<vector>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<component>[xyz])\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static bool TryEvaluateField(CinematicScriptOwner owner, SceneObject ownerObject,
        string functionName, string fieldName,
        IReadOnlyList<CinematicScriptRliValueFlowAssignment> assignments,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        Func<int, float?> channelProgressionResolver,
        IReadOnlyDictionary<int, bool?>? savedFlags, out float value)
    {
        value = 0;
        if (!TryEvaluateFunction(owner, ownerObject, functionName, assignments, engineCalls,
                playhead, timelineCalls, channelProgressionResolver, savedFlags, null,
                out Dictionary<string, double?> resultingFields)
            || resultingFields.GetValueOrDefault(fieldName) is not { } resolvedValue
            || !TryConvertToFloat(resolvedValue, out value))
            return false;
        return true;
    }

    internal static bool TryEvaluateFunction(CinematicScriptOwner owner, SceneObject ownerObject,
        string functionName,
        IReadOnlyList<CinematicScriptRliValueFlowAssignment> assignments,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        Func<int, float?> channelProgressionResolver,
        IReadOnlyDictionary<int, bool?>? savedFlags,
        IReadOnlyDictionary<string, double?>? persistentFields,
        out Dictionary<string, double?> resultingFields)
        => TryEvaluateFunction(owner, ownerObject, functionName, assignments, engineCalls,
            playhead, timelineCalls, channelProgressionResolver, savedFlags, persistentFields,
            null, out resultingFields);

    internal static bool TryEvaluateFunction(CinematicScriptOwner owner, SceneObject ownerObject,
        string functionName,
        IReadOnlyList<CinematicScriptRliValueFlowAssignment> assignments,
        IReadOnlyList<CinematicScriptReachableEngineCall> engineCalls,
        float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        Func<int, float?> channelProgressionResolver,
        IReadOnlyDictionary<int, bool?>? savedFlags,
        IReadOnlyDictionary<string, double?>? persistentFields,
        IReadOnlyDictionary<string, double?>? runtimeInputs,
        out Dictionary<string, double?> resultingFields)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ownerObject);
        ArgumentNullException.ThrowIfNull(channelProgressionResolver);
        var values = InitialValues(owner);
        foreach ((string name, long? fieldValue) in
                 CinematicScriptRuntimeBranchEvaluator.ResolveFieldValues(owner, ownerObject,
                     playhead, timelineCalls))
            values[name] = fieldValue;
        if (persistentFields != null)
        {
            foreach ((string name, double? fieldValue) in persistentFields)
                if (owner.Variables.Any(variable => variable.Name.Equals(name,
                        StringComparison.OrdinalIgnoreCase)))
                    values[name] = fieldValue;
        }
        if (runtimeInputs != null)
            foreach ((string name, double? runtimeValue) in runtimeInputs)
                values[name] = runtimeValue;
        CinematicScriptRliValueFlowAssignment[] functionAssignments = assignments
            .Where(assignment => assignment.FunctionName == functionName)
            .OrderBy(assignment => assignment.Line)
            .ToArray();
        if (functionAssignments.Length == 0)
        {
            resultingFields = [];
            return false;
        }

        foreach (CinematicScriptRliValueFlowAssignment assignment in functionAssignments)
        {
            CinematicScriptBranchEvaluation branch = EvaluateBranches(owner, ownerObject,
                assignment.BranchConditions, playhead, timelineCalls, values, savedFlags);
            if (branch.Value == CinematicScriptBranchValue.False) continue;
            if (branch.Value == CinematicScriptBranchValue.Unknown)
            {
                values[assignment.Variable] = null;
                continue;
            }

            double? assignedValue = ResolveEngineCallResult(assignment, functionName,
                engineCalls, channelProgressionResolver, values);
            assignedValue ??= EvaluateExpression(assignment.Expression, values);
            values[assignment.Variable] = assignedValue;
        }

        resultingFields = new(values, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryConvertToFloat(double value, out float result)
    {
        result = 0;
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
            return false;
        result = (float)value;
        return float.IsFinite(result);
    }

    private static Dictionary<string, double?> InitialValues(CinematicScriptOwner owner)
    {
        var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        foreach (CinematicScriptVariable variable in owner.Variables)
        {
            if (variable.ArrayValueCount != null) continue;
            values[variable.Name] = ParseNumber(variable.Value);
        }
        return values;
    }

    private static CinematicScriptBranchEvaluation EvaluateBranches(CinematicScriptOwner owner,
        SceneObject ownerObject, CinematicScriptBranchConditionSource[] branches,
        float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<string, double?> values,
        IReadOnlyDictionary<int, bool?>? savedFlags)
        => CinematicScriptRuntimeBranchEvaluator.Evaluate(owner,
            ownerObject.PreviewCapabilities, branches, values, savedFlags, playhead);

    private static double? ResolveEngineCallResult(
        CinematicScriptRliValueFlowAssignment assignment, string functionName,
        IReadOnlyList<CinematicScriptReachableEngineCall> calls,
        Func<int, float?> channelProgressionResolver,
        IReadOnlyDictionary<string, double?> values)
    {
        CinematicScriptReachableEngineCall[] matchingCalls = calls.Where(call =>
                call.FunctionName == functionName && call.Line == assignment.Line
                && call.ResultVariable == assignment.Variable)
            .ToArray();
        if (matchingCalls.Length != 1) return null;

        CinematicScriptReachableEngineCall call = matchingCalls[0];
        if (call.Api == "COLORBlend" && call.Arguments.Length == 3
            && TryEvaluateExpression(call.Arguments[0], values, out double startValue)
            && TryEvaluateExpression(call.Arguments[1], values, out double endValue)
            && TryEvaluateExpression(call.Arguments[2], values, out double factorValue)
            && TryPackedColor(startValue, out uint startColor)
            && TryPackedColor(endValue, out uint endColor)
            && TryConvertToFloat(factorValue, out float factor)
            && float.IsFinite(factor))
            return CinematicPackedColorBlend.Evaluate(startColor, endColor, factor);

        if (call.Api != "OBJ_GetChannelProg" || call.Arguments.Length < 2
            || call.Arguments[0] != "AI_gpst_CurrentGameObject"
            || !int.TryParse(call.Arguments[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int channelIndex)
            || channelProgressionResolver(channelIndex) is not { } progression
            || !float.IsFinite(progression))
            return null;

        return progression;
    }

    private static double? EvaluateExpression(string expression,
        IReadOnlyDictionary<string, double?> values)
    {
        return TryEvaluateExpression(expression, values, out double value) ? value : null;
    }

    private static bool TryEvaluateExpression(string expression,
        IReadOnlyDictionary<string, double?> values, out double value)
    {
        string normalized = MemberReference.Replace(expression,
            match => match.Groups["field"].Value);
        normalized = VectorComponentReference.Replace(normalized,
            match => $"{match.Groups["vector"].Value}_{match.Groups["component"].Value}");
        return ScalarExpression.TryEvaluate(normalized, values, out value);
    }

    internal static bool TryEvaluateSourceExpression(string expression,
        IReadOnlyDictionary<string, double?> values, out double value) =>
        TryEvaluateExpression(expression, values, out value);

    private static bool TryPackedColor(double value, out uint packed)
    {
        packed = 0;
        if (!double.IsFinite(value) || value != Math.Truncate(value)) return false;
        if (value is >= int.MinValue and < 0)
        {
            packed = unchecked((uint)(int)value);
            return true;
        }
        if (value < uint.MinValue || value > uint.MaxValue) return false;
        packed = (uint)value;
        return true;
    }

    private static double? ParseNumber(string text)
    {
        string normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            string hexadecimal = normalized.TrimEnd('u', 'U', 'l', 'L');
            if (uint.TryParse(hexadecimal.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint packed))
                return packed;
            return null;
        }

        normalized = normalized.TrimEnd('f', 'F');
        return double.TryParse(normalized, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) ? value : null;
    }

    private sealed class ScalarExpression(string text,
        IReadOnlyDictionary<string, double?> values)
    {
        private readonly string text = text;
        private readonly IReadOnlyDictionary<string, double?> values = values;
        private int position;

        internal static bool TryEvaluate(string text, IReadOnlyDictionary<string, double?> values,
            out double value)
        {
            value = 0;
            var parser = new ScalarExpression(text, values);
            try
            {
                value = parser.ParseSum();
                parser.SkipWhitespace();
                return parser.position == parser.text.Length && double.IsFinite(value);
            }
            catch (Exception exception) when (exception is FormatException
                or OverflowException or DivideByZeroException)
            {
                value = 0;
                return false;
            }
        }

        private double ParseSum()
        {
            double result = ParseProduct();
            while (true)
            {
                if (TryConsume('+')) result += ParseProduct();
                else if (TryConsume('-')) result -= ParseProduct();
                else return result;
            }
        }

        private double ParseProduct()
        {
            double result = ParseUnary();
            while (true)
            {
                if (TryConsume('*')) result *= ParseUnary();
                else if (TryConsume('/')) result /= ParseUnary();
                else return result;
            }
        }

        private double ParseUnary()
        {
            if (TryConsume('+')) return ParseUnary();
            if (TryConsume('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                double result = ParseSum();
                if (!TryConsume(')')) throw new FormatException();
                return result;
            }

            if (position < text.Length && (char.IsDigit(text[position])
                || text[position] == '.'))
                return ParseLiteral();

            string name = ParseIdentifier();
            if (values.TryGetValue(name, out double? value) && value is { } resolved)
                return resolved;
            throw new FormatException();
        }

        private double ParseLiteral()
        {
            SkipWhitespace();
            if (position + 2 < text.Length && text[position] == '0'
                && text[position + 1] is 'x' or 'X')
            {
                position += 2;
                int startHex = position;
                while (position < text.Length && Uri.IsHexDigit(text[position])) position++;
                if (position == startHex || !uint.TryParse(text.AsSpan(startHex, position - startHex),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hexadecimal))
                    throw new FormatException();
                while (position < text.Length && text[position] is 'u' or 'U' or 'l' or 'L') position++;
                return hexadecimal;
            }

            int start = position;
            while (position < text.Length && (char.IsDigit(text[position])
                || text[position] is '.' or 'e' or 'E'
                || ((text[position] is '+' or '-') && position > start
                    && (text[position - 1] is 'e' or 'E'))))
                position++;
            if (position < text.Length && text[position] is 'f' or 'F') position++;
            string token = text[start..position].TrimEnd('f', 'F');
            if (!double.TryParse(token, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new FormatException();
            return value;
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            int start = position;
            if (position >= text.Length || !(char.IsLetter(text[position]) || text[position] == '_'))
                throw new FormatException();
            position++;
            while (position < text.Length
                && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
                position++;
            return text[start..position];
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (position >= text.Length || text[position] != expected) return false;
            position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
    }
}
