using System.Globalization;
using System.Text.RegularExpressions;

namespace JadeView;

internal enum CinematicScriptBranchValue
{
    Unknown,
    False,
    True
}

internal sealed record CinematicScriptBranchEvaluation(
    CinematicScriptBranchValue Value,
    string[] Conditions,
    string Evidence);

/// <summary>
/// Evaluates the small set of callback predicates whose inputs can be reconstructed
/// from the current EVE playhead. Unsupported runtime inputs remain unknown.
/// </summary>
internal static partial class CinematicScriptRuntimeBranchEvaluator
{
    internal const string CurrentQueueHasMessageField = "$current-object-queue-has-message";

    [GeneratedRegex(@"^\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*->\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[^;]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex OvaAssignment();

    internal static CinematicScriptBranchEvaluation Evaluate(CinematicScriptOwner owner,
        SceneObject? ownerObject, IEnumerable<CinematicScriptBranchConditionSource> branches,
        float playhead, IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlagValues = null,
        IReadOnlyDictionary<string, long?>? additionalFields = null)
    {
        var fields = ResolveFieldValues(owner, ownerObject, playhead, timelineCalls);
        if (additionalFields != null)
            foreach (var field in additionalFields) fields[field.Key] = field.Value;
        return Evaluate(owner, ownerObject?.PreviewCapabilities, branches, fields,
            savedFlagValues, playhead);
    }

    internal static Dictionary<string, long?> ResolveFieldValues(CinematicScriptOwner owner,
        SceneObject? ownerObject, float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        var fields = InitialFieldValues(owner);
        ApplyTimelineMessageWrites(owner, ownerObject, fields, playhead, timelineCalls);
        return fields;
    }

    internal static CinematicScriptBranchEvaluation EvaluateCallbackPath(CinematicScriptOwner owner,
        SceneObject? ownerObject, IEnumerable<CinematicScriptCallbackPathStep> pathSteps,
        float playhead, IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlagValues = null)
    {
        CinematicScriptCallbackPathStep[] steps = pathSteps.ToArray();
        var conditions = new List<string>();
        var evidence = new List<string>();
        CinematicScriptBranchValue value = CinematicScriptBranchValue.True;
        foreach (CinematicScriptCallbackPathStep step in steps)
        {
            if (step.Dispatch != "replace-current-track" || !step.DispatchConditionsResolved)
                return new(CinematicScriptBranchValue.Unknown, conditions.ToArray(),
                    $"Callback transition {step.FromFunction} → {step.ToFunction} is not uniquely resolved.");
            CinematicScriptBranchEvaluation result = Evaluate(owner, ownerObject,
                step.BranchConditions, playhead, timelineCalls, savedFlagValues);
            conditions.AddRange(result.Conditions);
            evidence.Add(result.Evidence);
            value = And(value, result.Value);
            if (value == CinematicScriptBranchValue.False) break;
        }
        return new(value, conditions.ToArray(), evidence.Count == 0
            ? "The source callback path has no unresolved transition gates."
            : string.Join("; ", evidence));
    }

    internal static CinematicScriptBranchEvaluation Evaluate(CinematicScriptOwner owner,
        uint? capabilities, IEnumerable<CinematicScriptBranchConditionSource> branches,
        IReadOnlyDictionary<string, long?> fields,
        IReadOnlyDictionary<int, bool?>? savedFlagValues = null, double? currentTime = null)
        => Evaluate(owner, capabilities, branches, fields.ToDictionary(pair => pair.Key,
            pair => pair.Value is { } value ? (double?)value : null,
            StringComparer.OrdinalIgnoreCase), savedFlagValues, currentTime);

    internal static CinematicScriptBranchEvaluation Evaluate(CinematicScriptOwner owner,
        uint? capabilities, IEnumerable<CinematicScriptBranchConditionSource> branches,
        IReadOnlyDictionary<string, double?> fields,
        IReadOnlyDictionary<int, bool?>? savedFlagValues = null, double? currentTime = null)
    {
        CinematicScriptBranchConditionSource[] sourceBranches = branches.ToArray();
        if (sourceBranches.Length == 0)
            return new(CinematicScriptBranchValue.True, [], "The callback call site has no recorded enclosing branch.");

        var evaluated = sourceBranches.Select(branch => EvaluateBranch(branch,
            capabilities, fields, savedFlagValues, currentTime)).ToArray();
        CinematicScriptBranchValue combined = evaluated.Aggregate(CinematicScriptBranchValue.True,
            (current, next) => CinematicScriptRuntimeBranchEvaluator.And(current, next.Value));
        string evidence = string.Join("; ", evaluated.Select(result => result.Evidence));
        return new(combined, evaluated.Select(result => result.Condition).ToArray(), evidence);
    }

    private static (string Condition, CinematicScriptBranchValue Value, string Evidence) EvaluateBranch(
        CinematicScriptBranchConditionSource branch, uint? capabilities,
        IReadOnlyDictionary<string, double?> fields, IReadOnlyDictionary<int, bool?>? savedFlagValues,
        double? currentTime)
    {
        CinematicScriptPredicateResult predicate =
            CinematicScriptPredicateEvaluator.Evaluate(branch.Expression, capabilities, fields,
                savedFlagValues, currentTime);
        CinematicScriptBranchValue value = predicate.Value;
        if (branch.Kind.Equals("else", StringComparison.OrdinalIgnoreCase))
            value = CinematicScriptRuntimeBranchEvaluator.Not(value);
        string label = branch.Kind.Equals("else", StringComparison.OrdinalIgnoreCase)
            ? $"else of ({branch.Expression})" : branch.Expression;
        string evidence = predicate.IsFullySupported
            ? $"{label} is {(value == CinematicScriptBranchValue.True ? "true" : value == CinematicScriptBranchValue.False ? "false" : "unknown")}."
            : $"{label} includes runtime inputs JadeView cannot resolve.";
        return (label, value, evidence);
    }

    private static Dictionary<string, long?> InitialFieldValues(CinematicScriptOwner owner)
    {
        var fields = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        foreach (string variableName in owner.Variables.Select(variable => variable.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CinematicScriptVariable? variable = owner.EffectiveVariable(variableName);
            if (variable != null) fields[variableName] = ParseInteger(variable.Value);
        }
        return fields;
    }

    private static void ApplyTimelineMessageWrites(CinematicScriptOwner owner, SceneObject? ownerObject,
        Dictionary<string, long?> fields, float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls)
    {
        if (!float.IsFinite(playhead)) return;

        var messageReaders = new Dictionary<int, CinematicScriptMessageReader[]>();
        CinematicScriptReachableOvaFieldAccess[] fieldAccesses =
            CinematicScriptReader.ReachableOvaFieldAccesses(owner);
        foreach (var (time, call) in timelineCalls.OrderBy(item => item.Time))
        {
            if (time > playhead) continue;
            if (!TryReadQueuedMessageForOwner(call, owner.OwnerKey, out int messageId)) continue;

            if (!messageReaders.TryGetValue(messageId, out CinematicScriptMessageReader[]? readers))
            {
                readers = CinematicScriptReader.ReachableMessageReaders(owner, messageId);
                messageReaders[messageId] = readers;
            }
            if (readers.Length == 0) continue;

            var writes = readers.SelectMany(reader => fieldAccesses.Where(access =>
                    access.FunctionName == reader.FunctionName
                    && access.Access is "write" or "read/write")
                .Select(access => (Reader: reader, Access: access)))
                .ToArray();
            var messageFields = new Dictionary<string, long?>(fields, StringComparer.OrdinalIgnoreCase)
            {
                [CurrentQueueHasMessageField] = 1
            };
            foreach (var fieldGroup in writes.GroupBy(item => item.Access.VariableName,
                         StringComparer.OrdinalIgnoreCase))
            {
                var results = fieldGroup.Select(item => ResolveMessageWrite(owner,
                        ownerObject?.PreviewCapabilities, messageFields, item.Access))
                    .ToArray();
                if (results.All(result => result.Status == FieldWriteStatus.Skipped)) continue;
                bool hasUnknownWrite = results.Any(result => result.Status == FieldWriteStatus.Unknown);
                long?[] values = results.Where(result => result.Status == FieldWriteStatus.Written)
                    .Select(result => result.Value).Distinct().ToArray();
                fields[fieldGroup.Key] = !hasUnknownWrite && values.Length == 1 ? values[0] : null;
            }
        }
    }

    internal static bool TryReadQueuedMessageForOwner(CinematicLiteralCall call, uint ownerKey,
        out int messageId)
    {
        if (!CinematicEngineFunctionCatalog.TryReadMessageGateFields(call,
                out messageId, out _, out CinematicMessageQueueDestination destination))
            return false;

        // MSGSendEx/MSGSendAll queue on the current execution object. Their GAO
        // payload fields are message data, not the queue recipient. MSGSendAllOwner
        // uses a separate runtime registration and cannot be resolved from the call.
        return destination != CinematicMessageQueueDestination.RegisteredOwner
            && call.Target == ownerKey;
    }

    private static FieldWriteResult ResolveMessageWrite(CinematicScriptOwner owner, uint? capabilities,
        IReadOnlyDictionary<string, long?> fields, CinematicScriptReachableOvaFieldAccess access)
    {
        CinematicScriptBranchEvaluation branchResult = Evaluate(owner, capabilities,
            access.BranchConditions, fields);
        if (branchResult.Value == CinematicScriptBranchValue.False)
            return new(FieldWriteStatus.Skipped, null);
        if (branchResult.Value == CinematicScriptBranchValue.Unknown)
            return new(FieldWriteStatus.Unknown, null);

        Match assignment = OvaAssignment().Match(access.Expression);
        long? assignedValue = assignment.Success ? ParseInteger(assignment.Groups["value"].Value) : null;
        return assignedValue is { } value
            ? new(FieldWriteStatus.Written, value)
            : new(FieldWriteStatus.Unknown, null);
    }

    private static long? ParseInteger(string expression)
    {
        string value = expression.Trim().TrimEnd('u', 'U', 'l', 'L');
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(value.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out long hexadecimal) ? hexadecimal : null;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long integer) ? integer : null;
    }

    internal static CinematicScriptBranchValue Not(CinematicScriptBranchValue value) => value switch
    {
        CinematicScriptBranchValue.True => CinematicScriptBranchValue.False,
        CinematicScriptBranchValue.False => CinematicScriptBranchValue.True,
        _ => CinematicScriptBranchValue.Unknown
    };

    internal static CinematicScriptBranchValue And(CinematicScriptBranchValue left,
        CinematicScriptBranchValue right) => (left, right) switch
    {
        (CinematicScriptBranchValue.False, _) or (_, CinematicScriptBranchValue.False) => CinematicScriptBranchValue.False,
        (CinematicScriptBranchValue.True, CinematicScriptBranchValue.True) => CinematicScriptBranchValue.True,
        _ => CinematicScriptBranchValue.Unknown
    };

    internal static CinematicScriptBranchValue Or(CinematicScriptBranchValue left,
        CinematicScriptBranchValue right) => (left, right) switch
    {
        (CinematicScriptBranchValue.True, _) or (_, CinematicScriptBranchValue.True) => CinematicScriptBranchValue.True,
        (CinematicScriptBranchValue.False, CinematicScriptBranchValue.False) => CinematicScriptBranchValue.False,
        _ => CinematicScriptBranchValue.Unknown
    };

    private enum FieldWriteStatus { Skipped, Written, Unknown }
    private readonly record struct FieldWriteResult(FieldWriteStatus Status, long? Value);
}
