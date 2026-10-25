using System.Text.RegularExpressions;

namespace JadeView;

/// <summary>
/// Carries scalar OVA fields written by source-confirmed message readers into
/// later callback previews when the matching message is present in the clip.
/// </summary>
internal static partial class CinematicScriptMessageScalarStateResolver
{
    [GeneratedRegex(@"=\s*(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*(?:->|\.)\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*;?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MessageFieldAssignment();

    internal static Dictionary<string, double?> Resolve(CinematicScriptOwner owner,
        SceneObject? ownerObject,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        float playhead,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        var resultingFields = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        CinematicScriptReachableOvaFieldAccess[] fieldAccesses =
            CinematicScriptReader.ReachableOvaFieldAccesses(owner);
        var readerCache = new Dictionary<int, CinematicScriptMessageReader[]>();

        foreach ((float messageTime, CinematicLiteralCall call) in timelineCalls
                     .Where(item => item.Time <= playhead
                         && CinematicScriptRuntimeBranchEvaluator.TryReadQueuedMessageForOwner(
                             item.Call, owner.OwnerKey, out _))
                     .OrderBy(item => item.Time))
        {
            if (!CinematicEngineFunctionCatalog.TryReadMessageGateFields(call,
                    out int messageId, out _, out _)) continue;
            if (!readerCache.TryGetValue(messageId, out CinematicScriptMessageReader[]? readers))
            {
                readers = CinematicScriptReader.ReachableMessageReaders(owner, messageId);
                readerCache[messageId] = readers;
            }
            if (readers.Length == 0) continue;

            var branchFields = CinematicScriptRuntimeBranchEvaluator.ResolveFieldValues(owner,
                ownerObject, messageTime, timelineCalls);
            branchFields[CinematicScriptRuntimeBranchEvaluator.CurrentQueueHasMessageField] = 1;

            var writes = readers.SelectMany(reader => fieldAccesses
                    .Where(access => access.FunctionName == reader.FunctionName
                        && access.Access is "write" or "read/write")
                    .Select(access => (Reader: reader, Access: access)))
                .ToArray();
            foreach (var fieldGroup in writes.GroupBy(item => item.Access.VariableName,
                         StringComparer.OrdinalIgnoreCase))
            {
                var outcomes = fieldGroup.Select(item => ResolveWrite(owner, ownerObject,
                        call, branchFields, savedFlags, item.Access))
                    .ToArray();
                if (outcomes.All(outcome => outcome.Status == WriteStatus.Skipped)) continue;

                double?[] resolvedValues = outcomes
                    .Where(outcome => outcome.Status == WriteStatus.Resolved)
                    .Select(outcome => outcome.Value).Distinct().ToArray();
                bool hasUnknownWrite = outcomes.Any(outcome => outcome.Status == WriteStatus.Unknown);
                resultingFields[fieldGroup.Key] = !hasUnknownWrite && resolvedValues.Length == 1
                    ? resolvedValues[0]
                    : null;
            }
        }

        return resultingFields;
    }

    private static WriteResult ResolveWrite(CinematicScriptOwner owner, SceneObject? ownerObject,
        CinematicLiteralCall call, IReadOnlyDictionary<string, long?> branchFields,
        IReadOnlyDictionary<int, bool?>? savedFlags, CinematicScriptReachableOvaFieldAccess access)
    {
        CinematicScriptBranchEvaluation branch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, ownerObject?.PreviewCapabilities, access.BranchConditions, branchFields, savedFlags);
        if (branch.Value == CinematicScriptBranchValue.False)
            return new(WriteStatus.Skipped, null);
        if (branch.Value == CinematicScriptBranchValue.Unknown)
            return new(WriteStatus.Unknown, null);

        Match assignment = MessageFieldAssignment().Match(access.Expression);
        if (!assignment.Success)
            return new(WriteStatus.Skipped, null);
        if (!TryReadMessageField(call, assignment.Groups["field"].Value, out double value))
            return new(WriteStatus.Unknown, null);
        return new(WriteStatus.Resolved, value);
    }

    private static bool TryReadMessageField(CinematicLiteralCall call, string fieldName,
        out double value)
    {
        string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        if (function == "MSGSendAll" && call.Arguments.Length == 13)
        {
            uint[] arguments = call.Arguments;
            value = fieldName.ToLowerInvariant() switch
            {
                "f1" => BitConverter.UInt32BitsToSingle(arguments[10]),
                "f2" => BitConverter.UInt32BitsToSingle(arguments[9]),
                "int1" => unchecked((int)arguments[11]),
                "int2" => unchecked((int)arguments[12]),
                "msgid" or "type" => unchecked((int)arguments[0]),
                "gao" => arguments[1],
                "gao2" => arguments[2],
                _ => double.NaN
            };
            return double.IsFinite(value);
        }

        if (function == "MSGSendEx" && call.Arguments.Length >= 2
            && fieldName.Equals("f2", StringComparison.OrdinalIgnoreCase))
        {
            value = unchecked((int)call.Arguments[1]);
            return true;
        }

        value = double.NaN;
        return double.IsFinite(value);
    }

    private enum WriteStatus { Skipped, Resolved, Unknown }
    private readonly record struct WriteResult(WriteStatus Status, double? Value);
}
