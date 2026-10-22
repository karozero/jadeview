using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal static class CinematicScriptTrackIndexResolver
{
    private const int MaximumExpandedLoopIterations = 1024;
    private static readonly Regex MemberReference = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*->\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.CultureInvariant);

    internal static bool TryResolve(CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        out int trackIndex, out string binding)
    {
        trackIndex = -1;
        binding = string.Empty;
        if (!TryResolveIndices(owner, call, out var indices, out binding) || indices.Length != 1)
            return false;
        trackIndex = indices[0];
        return true;
    }

    internal static bool TryResolveIntegerArgument(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, int argumentIndex, out int value, out string binding)
    {
        value = -1;
        binding = string.Empty;
        return call.LoopContexts is not { Length: > 0 }
            && argumentIndex >= 0 && argumentIndex < call.Arguments.Length
            && TryEvaluateIndex(owner, call, call.Arguments[argumentIndex], out value, out binding);
    }

    internal static bool TryResolveSignedIntegerArgument(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, int argumentIndex, out int value, out string binding)
    {
        value = 0;
        binding = string.Empty;
        if (call.LoopContexts is { Length: > 0 }
            || argumentIndex < 0 || argumentIndex >= call.Arguments.Length
            || !TryEvaluateIndexExpression(owner, call, call.Arguments[argumentIndex], out long resolved,
                out var usedFields)
            || resolved < int.MinValue || resolved > int.MaxValue)
            return false;
        value = (int)resolved;
        binding = usedFields.Count == 0
            ? "literal or constant source expression"
            : $"OVA {string.Join(", ", usedFields.OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                .Select(field => $"{field.Key}={field.Value}"))}";
        return true;
    }

    internal static bool TryResolveIndices(CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        out int[] trackIndices, out string binding)
    {
        trackIndices = [];
        binding = string.Empty;
        if (call.Arguments.Length < 2) return false;

        if (call.LoopContexts is not { Length: > 0 })
        {
            if (!TryEvaluateIndex(owner, call, call.Arguments[1], out int resolvedIndex, out binding))
                return false;
            trackIndices = [resolvedIndex];
            return true;
        }

        if (call.LoopContexts.Length != 1 || call.LoopContexts[0].Kind != "for"
            || !TryResolveForLoop(owner, call, call.LoopContexts[0], out trackIndices, out binding))
            return false;
        return true;
    }

    private static bool TryResolveForLoop(CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        CinematicScriptLoopContextSource loop, out int[] trackIndices, out string binding)
    {
        trackIndices = [];
        binding = string.Empty;
        string[] clauses = loop.Expression.Split(';', StringSplitOptions.TrimEntries);
        if (clauses.Length != 3 || loop.InductionVariableModified) return false;
        Match initialization = Regex.Match(clauses[0],
            @"^(?:int\s+)?(?<variable>[A-Za-z_]\w*)\s*=\s*(?<value>.+)$",
            RegexOptions.CultureInvariant);
        if (!initialization.Success) return false;

        string loopVariable = initialization.Groups["variable"].Value;
        Match condition = Regex.Match(clauses[1],
            @"^(?<variable>[A-Za-z_]\w*)\s*(?<operator><=|<|>=|>)\s*(?<value>.+)$",
            RegexOptions.CultureInvariant);
        if (!condition.Success || !condition.Groups["variable"].Value.Equals(loopVariable, StringComparison.Ordinal))
            return false;

        int step = GetUnitLoopStep(clauses[2], loopVariable);
        if (step == 0
            || !TryEvaluateIndexExpression(owner, call, initialization.Groups["value"].Value, out long firstIndex,
                out var usedFields)
            || !TryEvaluateIndexExpression(owner, call, condition.Groups["value"].Value, out long limit,
                out var limitFields))
            return false;
        foreach (var field in limitFields) usedFields[field.Key] = field.Value;

        if (!TryGetIterationCount(firstIndex, limit, condition.Groups["operator"].Value, step,
                out int iterationCount)
            || iterationCount is < 1 or > MaximumExpandedLoopIterations)
            return false;

        var resolvedIndices = new int[iterationCount];
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            long loopValue = checked(firstIndex + (long)iteration * step);
            var indexedCall = call with
            {
                ArgumentAssignments = [.. call.ArgumentAssignments.Where(assignment =>
                        !assignment.Variable.Equals(loopVariable, StringComparison.Ordinal)),
                    new(loopVariable, loopValue.ToString(CultureInfo.InvariantCulture), loop.Line, 0)]
            };
            if (!TryEvaluateIndex(owner, indexedCall, call.Arguments[1], out resolvedIndices[iteration],
                    out _))
                return false;
        }

        trackIndices = resolvedIndices.Distinct().Order().ToArray();
        if (trackIndices.Length != iterationCount) return false;
        string fieldSummary = usedFields.Count == 0
            ? "literal source bounds"
            : $"OVA {string.Join(", ", usedFields.OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                .Select(field => $"{field.Key}={field.Value}"))}";
        binding = $"{fieldSummary}; for {loopVariable}={firstIndex} ({condition.Groups["operator"].Value} {limit}, step {step})";
        return true;
    }

    private static int GetUnitLoopStep(string increment, string variable)
    {
        string escapedVariable = Regex.Escape(variable);
        if (Regex.IsMatch(increment, $@"^(?:\+\+\s*{escapedVariable}|{escapedVariable}\s*\+\+)$",
                RegexOptions.CultureInvariant))
            return 1;
        if (Regex.IsMatch(increment, $@"^(?:--\s*{escapedVariable}|{escapedVariable}\s*--)$",
                RegexOptions.CultureInvariant))
            return -1;
        if (Regex.IsMatch(increment, $@"^{escapedVariable}\s*\+=\s*1$", RegexOptions.CultureInvariant))
            return 1;
        if (Regex.IsMatch(increment, $@"^{escapedVariable}\s*-\s*=\s*1$", RegexOptions.CultureInvariant))
            return -1;
        return 0;
    }

    private static bool TryGetIterationCount(long first, long limit, string comparison, int step,
        out int iterationCount)
    {
        iterationCount = 0;
        bool positiveStep = step > 0;
        bool positiveCondition = positiveStep && (comparison is "<" or "<=");
        bool negativeCondition = !positiveStep && (comparison is ">" or ">=");
        if (positiveCondition && first <= limit || negativeCondition && first >= limit)
        {
            try
            {
                long difference = positiveStep ? checked(limit - first) : checked(first - limit);
                bool inclusive = comparison is "<=" or ">=";
                long count = inclusive ? checked(difference + 1) : difference;
                if (count < 0 || count > MaximumExpandedLoopIterations) return false;
                iterationCount = (int)count;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        // A directionally compatible loop that starts past its bound executes zero times.
        if (positiveCondition && first > limit || negativeCondition && first < limit)
        {
            iterationCount = 0;
            return true;
        }

        return false;
    }

    private static bool TryEvaluateIndex(CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        string expression, out int index, out string binding)
    {
        index = -1;
        binding = string.Empty;
        if (!TryEvaluateIndexExpression(owner, call, expression, out long value, out var usedFields)
            || value < 0 || value > int.MaxValue)
            return false;
        index = (int)value;
        binding = usedFields.Count == 0
            ? "literal or constant source expression"
            : $"OVA {string.Join(", ", usedFields.OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                .Select(field => $"{field.Key}={field.Value}"))}";
        return true;
    }

    private static bool TryEvaluateIndexExpression(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, string expression, out long value,
        out Dictionary<string, long> usedFields)
    {
        usedFields = new(StringComparer.OrdinalIgnoreCase);
        var resolvingVariables = new HashSet<string>(StringComparer.Ordinal);
        return TryEvaluate(owner, call, expression, usedFields, resolvingVariables, 0, out value);
    }

    private static bool TryEvaluate(CinematicScriptOwner owner, CinematicScriptReachableEngineCall call,
        string expression, Dictionary<string, long> usedFields, HashSet<string> resolvingVariables,
        int depth, out long value)
    {
        value = 0;
        if (depth > 8) return false;

        string expanded = MemberReference.Replace(expression, match =>
        {
            string fieldName = match.Groups["field"].Value;
            return TryResolveVariable(owner, call, fieldName, usedFields, resolvingVariables,
                depth + 1, out long fieldValue)
                ? fieldValue.ToString(CultureInfo.InvariantCulture)
                : match.Value;
        });
        if (MemberReference.IsMatch(expanded)) return false;

        var aliases = call.ArgumentFields.Where(field => field.AliasVariable is not null)
            .GroupBy(field => field.AliasVariable!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(field => field.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.Ordinal);
        var identifiers = Identifier.Matches(expanded).Select(match => match.Value).Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string identifier in identifiers)
        {
            if (TryResolveVariable(owner, call, identifier, usedFields, resolvingVariables,
                    depth + 1, out long directValue))
            {
                expanded = ReplaceIdentifier(expanded, identifier, directValue.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            string? aliasField = aliases.TryGetValue(identifier, out var aliasFields) && aliasFields.Length == 1
                ? aliasFields[0] : null;
            if (aliasField is not null && TryResolveVariable(owner, call, aliasField, usedFields,
                    resolvingVariables, depth + 1, out long aliasValue))
            {
                expanded = ReplaceIdentifier(expanded, identifier, aliasValue.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            return false;
        }

        return IntegerExpression.TryParse(expanded, out value);
    }

    private static bool TryResolveVariable(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call, string variableName, Dictionary<string, long> usedFields,
        HashSet<string> resolvingVariables, int depth, out long value)
    {
        value = 0;
        var assignment = call.ArgumentAssignments.FirstOrDefault(candidate =>
            candidate.Variable.Equals(variableName, StringComparison.Ordinal));
        if (assignment is null)
            return TryReadOvaInteger(owner, variableName, usedFields, out value);
        if (!assignment.IsUnique) return false;
        if (!resolvingVariables.Add(variableName)) return false;
        bool resolved = TryEvaluate(owner, call, assignment.Expression, usedFields,
            resolvingVariables, depth + 1, out value);
        resolvingVariables.Remove(variableName);
        return resolved;
    }

    private static bool TryReadOvaInteger(CinematicScriptOwner owner, string fieldName,
        Dictionary<string, long> usedFields, out long value)
    {
        value = 0;
        var variable = owner.EffectiveVariable(fieldName);
        if (variable == null || variable.ArrayValueCount != null
            || !IntegerExpression.TryParse(variable.Value, out value))
            return false;
        usedFields[fieldName] = value;
        return true;
    }

    private static string ReplaceIdentifier(string expression, string identifier, string replacement) =>
        Regex.Replace(expression, $@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
            replacement, RegexOptions.CultureInvariant);

    private sealed class IntegerExpression
    {
        private readonly string _text;
        private int _position;

        private IntegerExpression(string text) => _text = text;

        internal static bool TryParse(string text, out long value)
        {
            var parser = new IntegerExpression(text);
            try
            {
                value = parser.ParseSum();
                parser.SkipWhitespace();
                return parser._position == parser._text.Length;
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException)
            {
                value = 0;
                return false;
            }
        }

        private long ParseSum()
        {
            long value = ParseProduct();
            while (true)
            {
                char operation;
                if (TryConsume('+')) operation = '+';
                else if (TryConsume('-')) operation = '-';
                else return value;
                long right = ParseProduct();
                value = operation == '+' ? checked(value + right) : checked(value - right);
            }
        }

        private long ParseProduct()
        {
            long value = ParseUnary();
            while (true)
            {
                if (TryConsume('*')) value = checked(value * ParseUnary());
                else if (TryConsume('/')) value /= ParseUnary();
                else if (TryConsume('%')) value %= ParseUnary();
                else return value;
            }
        }

        private long ParseUnary()
        {
            if (TryConsume('+')) return ParseUnary();
            if (TryConsume('-')) return checked(-ParseUnary());
            if (TryConsume('('))
            {
                long value = ParseSum();
                if (!TryConsume(')')) throw new FormatException();
                return value;
            }
            return ParseInteger();
        }

        private long ParseInteger()
        {
            SkipWhitespace();
            int start = _position;
            if (_position + 2 <= _text.Length && _text.AsSpan(_position).StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                _position += 2;
                while (_position < _text.Length && Uri.IsHexDigit(_text[_position])) _position++;
                if (_position == start + 2 || !long.TryParse(_text.AsSpan(start + 2, _position - start - 2),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexValue))
                    throw new FormatException();
                return hexValue;
            }
            while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
            if (_position == start || !long.TryParse(_text.AsSpan(start, _position - start),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long decimalValue))
                throw new FormatException();
            return decimalValue;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (_position >= _text.Length || _text[_position] != expected) return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }
    }
}
