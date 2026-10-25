using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal sealed record CinematicScriptPredicateResult(
    CinematicScriptBranchValue Value,
    bool IsFullySupported);

/// <summary>Evaluates a bounded C-like scalar expression subset used by callback guards.</summary>
internal static class CinematicScriptPredicateEvaluator
{
    private static readonly Regex SavedFlagTest = new(
        @"AI_EvalFunc_ARRBitTest_C\s*\(\s*\(int\*\)\s*\([^)]*Flag_speciaux_sauve[^)]*\)\s*,\s*(?<flag>0[xX][0-9A-Fa-f]+|\d+)[uUlL]*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static CinematicScriptPredicateResult Evaluate(string expression, uint? capabilities,
        IReadOnlyDictionary<string, long?> fields, IReadOnlyDictionary<int, bool?>? savedFlagValues = null,
        double? currentTime = null)
        => Evaluate(expression, capabilities, fields.ToDictionary(pair => pair.Key,
            pair => pair.Value is { } value ? (double?)value : null,
            StringComparer.OrdinalIgnoreCase), savedFlagValues, currentTime);

    internal static CinematicScriptPredicateResult Evaluate(string expression, uint? capabilities,
        IReadOnlyDictionary<string, double?> fields, IReadOnlyDictionary<int, bool?>? savedFlagValues = null,
        double? currentTime = null)
    {
        var parser = new ExpressionParser(expression, capabilities, fields, savedFlagValues, currentTime);
        return new(parser.Evaluate(), parser.IsFullySupported);
    }

    internal static int[] SavedFlagIndices(IEnumerable<CinematicScriptBranchConditionSource> branches) =>
        branches.SelectMany(branch => SavedFlagTest.Matches(branch.Expression).Select(match =>
                ParseIntegerLiteral(match.Groups["flag"].Value)))
            .Where(flag => flag is >= 0 and <= int.MaxValue)
            .Select(flag => (int)flag!.Value)
            .Distinct()
            .Order()
            .ToArray();

    private static long? ParseIntegerLiteral(string value)
    {
        string normalized = value.Trim().TrimEnd('u', 'U', 'l', 'L');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out long hexadecimal) ? hexadecimal : null;
        return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long integer) ? integer : null;
    }

    private sealed class ExpressionParser(string expression, uint? capabilities,
        IReadOnlyDictionary<string, double?> fields, IReadOnlyDictionary<int, bool?>? savedFlagValues,
        double? currentTime)
    {
        private int position;
        internal bool IsFullySupported { get; private set; } = true;

        internal CinematicScriptBranchValue Evaluate()
        {
            double? result = EvaluateScalar();
            return ToTruth(result);
        }

        private double? EvaluateScalar()
        {
            double? result = ParseOr();
            SkipWhitespace();
            if (position != expression.Length) IsFullySupported = false;
            return result;
        }

        private double? ParseOr()
        {
            double? result = ParseAnd();
            while (TryConsume("||"))
                result = FromTruth(CinematicScriptRuntimeBranchEvaluator.Or(ToTruth(result), ToTruth(ParseAnd())));
            return result;
        }

        private double? ParseAnd()
        {
            double? result = ParseBitwiseOr();
            while (TryConsume("&&"))
                result = FromTruth(CinematicScriptRuntimeBranchEvaluator.And(ToTruth(result), ToTruth(ParseBitwiseOr())));
            return result;
        }

        private double? ParseBitwiseOr()
        {
            double? result = ParseBitwiseXor();
            while (TryConsumeSingle('|')) result = Bitwise(result, ParseBitwiseXor(), (left, right) => left | right);
            return result;
        }

        private double? ParseBitwiseXor()
        {
            double? result = ParseBitwiseAnd();
            while (TryConsume("^")) result = Bitwise(result, ParseBitwiseAnd(), (left, right) => left ^ right);
            return result;
        }

        private double? ParseBitwiseAnd()
        {
            double? result = ParseEquality();
            while (TryConsumeSingle('&')) result = Bitwise(result, ParseEquality(), (left, right) => left & right);
            return result;
        }

        private double? ParseEquality()
        {
            double? left = ParseComparison();
            if (TryConsume("==")) return Compare(left, ParseComparison(), equals: true);
            if (TryConsume("!=")) return Compare(left, ParseComparison(), equals: false);
            if (TryConsume("=")) return ParseComparison();
            return left;
        }

        private double? ParseComparison()
        {
            double? left = ParseShift();
            if (TryConsume("<=")) return Compare(left, ParseShift(), (a, b) => a <= b);
            if (TryConsume(">=")) return Compare(left, ParseShift(), (a, b) => a >= b);
            if (TryConsume("<")) return Compare(left, ParseShift(), (a, b) => a < b);
            if (TryConsume(">")) return Compare(left, ParseShift(), (a, b) => a > b);
            return left;
        }

        private double? ParseSum()
        {
            double? result = ParseProduct();
            while (true)
            {
                if (TryConsume("+")) result = Arithmetic(result, ParseProduct(), (left, right) => left + right);
                else if (TryConsume("-")) result = Arithmetic(result, ParseProduct(), (left, right) => left - right);
                else return result;
            }
        }

        private double? ParseProduct()
        {
            double? result = ParseUnary();
            while (true)
            {
                if (TryConsume("*")) result = Arithmetic(result, ParseUnary(), (left, right) => left * right);
                else if (TryConsume("/")) result = Arithmetic(result, ParseUnary(), (left, right) => right == 0 ? double.NaN : left / right);
                else if (TryConsume("%")) result = Arithmetic(result, ParseUnary(), (left, right) => right == 0 ? double.NaN : left % right);
                else return result;
            }
        }

        private double? ParseShift()
        {
            double? result = ParseSum();
            while (true)
            {
                if (TryConsume("<<")) result = Shift(result, ParseSum(), leftShift: true);
                else if (TryConsume(">>")) result = Shift(result, ParseSum(), leftShift: false);
                else return result;
            }
        }

        private double? ParseUnary()
        {
            if (TryConsume("!"))
                return FromTruth(CinematicScriptRuntimeBranchEvaluator.Not(ToTruth(ParseUnary())));
            if (TryConsume("+")) return ParseUnary();
            if (TryConsume("-")) return Negate(ParseUnary());
            if (TryConsume("~")) return BitwiseNot(ParseUnary());
            if (!TryConsume("(")) return ParseAtom();

            double? value = ParseOr();
            if (!TryConsume(")")) IsFullySupported = false;
            return value;
        }

        private double? ParseAtom()
        {
            SkipWhitespace();
            if (position >= expression.Length) return Unknown();
            if (char.IsDigit(expression[position])) return ParseNumber();
            if (!IsIdentifierStart(expression[position])) return Unknown();

            string identifier = ParseIdentifier();
            SkipWhitespace();
            if (TryConsume("->") || TryConsume("."))
            {
                string member = ParseIdentifier();
                identifier = member.Length > 0 ? member : identifier;
                while (TryConsume("->") || TryConsume("."))
                {
                    member = ParseIdentifier();
                    if (member.Length > 0) identifier = member;
                }
            }

            if (TryConsume("(")) return ParseCall(identifier);
            if (identifier.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1;
            if (identifier.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0;
            return fields.TryGetValue(identifier, out double? fieldValue) ? fieldValue : Unknown();
        }

        private double? ParseCall(string functionName)
        {
            SkipWhitespace();
            int argumentStart = position;
            int depth = 0;
            while (position < expression.Length)
            {
                char current = expression[position++];
                if (current == '(') depth++;
                else if (current == ')')
                {
                    if (depth == 0) break;
                    depth--;
                }
            }

            string argument = expression[argumentStart..Math.Max(argumentStart, position - 1)].Trim();
            if (functionName.Equals("AI_EvalFunc_TIMEElapsed_C", StringComparison.Ordinal)
                && TryEvaluateElapsedTime(argument, out double elapsedResult))
                return elapsedResult;
            if (functionName.Equals("AI_EvalFunc_MSGGetCount_C", StringComparison.Ordinal)
                && argument.Equals("AI_gpst_CurrentGameObject", StringComparison.Ordinal))
                return fields.TryGetValue(CinematicScriptRuntimeBranchEvaluator.CurrentQueueHasMessageField,
                out double? queueCount) ? queueCount : Unknown();
            if (functionName.Contains("OBJ_CapaTest_C", StringComparison.Ordinal)
                && ParseNumberLiteral(argument) is { } capabilityMask)
                return capabilities is { } currentCapabilities
                    ? (currentCapabilities & (uint)capabilityMask) != 0 ? 1 : 0
                    : Unknown();
            Match savedFlag = SavedFlagTest.Match(functionName + "(" + argument + ")");
            if (savedFlag.Success && ParseIntegerLiteral(savedFlag.Groups["flag"].Value) is { } flagIndex)
            {
                if (flagIndex <= int.MaxValue && savedFlagValues?.TryGetValue((int)flagIndex,
                        out bool? isSet) == true && isSet is { } flagValue)
                    return flagValue ? 1 : 0;
                return Unknown();
            }
            return Unknown();
        }

        private bool TryEvaluateElapsedTime(string argument, out double result)
        {
            result = 0;
            if (currentTime is not { } now || !double.IsFinite(now)
                || !TrySplitArguments(argument, out string startExpression, out string durationExpression)
                || !TryReadReferencedField(startExpression, out string timerField)
                || !fields.TryGetValue(CinematicScriptCallbackEntryTimeResolver.RelativeClockAliasPrefix
                    + timerField, out double? isRelativeTimer) || isRelativeTimer != 1
                || !TryEvaluateNested(startExpression, out double startTime)
                || !TryEvaluateNested(durationExpression, out double duration))
                return false;

            result = now - startTime >= duration ? 1 : 0;
            return true;
        }

        private bool TryEvaluateNested(string source, out double value)
        {
            var nested = new ExpressionParser(source, capabilities, fields, savedFlagValues, currentTime);
            double? result = nested.EvaluateScalar();
            IsFullySupported &= nested.IsFullySupported;
            value = result ?? 0;
            return result is { } resolved && double.IsFinite(resolved);
        }

        private static bool TrySplitArguments(string arguments, out string first, out string second)
        {
            int depth = 0;
            for (int index = 0; index < arguments.Length; index++)
            {
                char character = arguments[index];
                if (character == '(') depth++;
                else if (character == ')') depth--;
                else if (character == ',' && depth == 0)
                {
                    first = arguments[..index].Trim();
                    second = arguments[(index + 1)..].Trim();
                    return first.Length > 0 && second.Length > 0
                        && !second.Contains(',');
                }
            }
            first = string.Empty;
            second = string.Empty;
            return false;
        }

        private static bool TryReadReferencedField(string expression, out string field)
        {
            Match match = Regex.Match(expression.Trim(),
                @"(?:->|\.)\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*$",
                RegexOptions.CultureInvariant);
            field = match.Success ? match.Groups["field"].Value : string.Empty;
            return match.Success;
        }

        private double? ParseNumber()
        {
            int start = position;
            while (position < expression.Length
                   && (char.IsLetterOrDigit(expression[position])
                       || expression[position] is 'x' or 'X' or '.' or '+' or '-'))
            {
                if (position > start && expression[position] is '+' or '-'
                    && expression[position - 1] is not 'e' and not 'E') break;
                position++;
            }
            return ParseNumberLiteral(expression[start..position]) ?? Unknown();
        }

        private static double? ParseNumberLiteral(string value)
        {
            string normalized = value.Trim().TrimEnd('u', 'U', 'l', 'L');
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(normalized.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long hexadecimal) ? hexadecimal : null;
            normalized = normalized.TrimEnd('f', 'F');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double number) && double.IsFinite(number) ? number : null;
        }

        private double? Compare(double? left, double? right, bool equals)
        {
            if (left is null || right is null) return Unknown();
            return (left == right) == equals ? 1 : 0;
        }

        private double? Compare(double? left, double? right, Func<double, double, bool> comparison)
        {
            if (left is null || right is null) return Unknown();
            return comparison(left.Value, right.Value) ? 1 : 0;
        }

        private double? Arithmetic(double? left, double? right, Func<double, double, double> operation)
        {
            if (left is null || right is null) return Unknown();
            double result = operation(left.Value, right.Value);
            return double.IsFinite(result) ? result : Unknown();
        }

        private double? Bitwise(double? left, double? right, Func<uint, uint, uint> operation)
        {
            if (left is not { } leftValue || right is not { } rightValue
                || !TryGetInteger(leftValue, out uint leftInteger)
                || !TryGetInteger(rightValue, out uint rightInteger)) return Unknown();
            return operation(leftInteger, rightInteger);
        }

        private double? Shift(double? value, double? count, bool leftShift)
        {
            if (value is not { } sourceValue || count is not { } shiftValue
                || !TryGetInteger(sourceValue, out uint sourceInteger)
                || !TryGetInteger(shiftValue, out uint shiftAmount) || shiftAmount >= 32)
                return Unknown();
            return leftShift ? sourceInteger << (int)shiftAmount : sourceInteger >> (int)shiftAmount;
        }

        private double? BitwiseNot(double? value)
        {
            if (value is not { } number || !TryGetInteger(number, out uint integer)) return Unknown();
            return ~integer;
        }

        private double? Negate(double? value) => value is { } number ? -number : Unknown();

        private static bool TryGetInteger(double value, out uint integer)
        {
            integer = 0;
            if (!double.IsFinite(value) || value != Math.Truncate(value)
                || value < long.MinValue || value > uint.MaxValue) return false;
            integer = unchecked((uint)(long)value);
            return true;
        }

        private double? Unknown()
        {
            IsFullySupported = false;
            return null;
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            int start = position;
            if (position >= expression.Length || !IsIdentifierStart(expression[position])) return "";
            position++;
            while (position < expression.Length && IsIdentifierCharacter(expression[position])) position++;
            return expression[start..position];
        }

        private bool TryConsume(string token)
        {
            SkipWhitespace();
            if (!expression.AsSpan(position).StartsWith(token, StringComparison.Ordinal)) return false;
            position += token.Length;
            return true;
        }

        private bool TryConsumeSingle(char token)
        {
            SkipWhitespace();
            if (position >= expression.Length || expression[position] != token
                || position + 1 < expression.Length && expression[position + 1] == token) return false;
            position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < expression.Length && char.IsWhiteSpace(expression[position])) position++;
        }

        private static bool IsIdentifierStart(char value) =>
            value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

        private static bool IsIdentifierCharacter(char value) =>
            IsIdentifierStart(value) || value is >= '0' and <= '9';

        private static CinematicScriptBranchValue ToTruth(double? value) => value switch
        {
            null => CinematicScriptBranchValue.Unknown,
            0 => CinematicScriptBranchValue.False,
            _ => CinematicScriptBranchValue.True
        };

        private static double? FromTruth(CinematicScriptBranchValue value) => value switch
        {
            CinematicScriptBranchValue.True => 1,
            CinematicScriptBranchValue.False => 0,
            _ => null
        };
    }
}
