using System.Text.RegularExpressions;

namespace JadeView;

internal static class CinematicScriptCallbackStateEffectAnalyzer
{
    internal static CinematicScriptCallbackStateEffect[] Analyze(CinematicScriptOwner owner,
        IReadOnlyList<CinematicScriptReachableEngineCall>? calls = null,
        IReadOnlyList<CinematicScriptReachableSourceReference>? sourceReferences = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        calls ??= CinematicScriptReader.ReachableEngineCalls(owner);
        sourceReferences ??= CinematicScriptReader.ReachableSourceReferences(owner);

        return calls
            .Where(call => TryDescribeStateEffect(call.Api, call.Variant, out _))
            .Select(call =>
            {
                TryDescribeStateEffect(call.Api, call.Variant, out string effect);
                (uint? receiverKey, string receiverEvidence, string[] receiverGuardAliases) = ResolveReceiver(
                    owner, call, sourceReferences);
                return new CinematicScriptCallbackStateEffect(owner.OwnerKey, effect, call.Api,
                    call.Variant, call.Arguments, call.ResultVariable, call.BranchConditions, call.FunctionName,
                    call.Source, call.Line, call.CallbackPath, "SourceCallCandidate")
                {
                    ReceiverKey = receiverKey,
                    ReceiverEvidence = receiverEvidence,
                    ReceiverGuardAliases = receiverGuardAliases,
                    SerializedBranchFindings = CinematicScriptBranchAnalyzer.Describe(
                        owner, call.BranchConditions)
                };
            })
            .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
    }

    private static (uint? ReceiverKey, string Evidence, string[] GuardAliases) ResolveReceiver(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences)
    {
        if (call.Variant.Equals("CURRENT", StringComparison.Ordinal))
            return (owner.OwnerKey, "Iris CURRENT API variant uses the callback's current game object.", []);

        if (CinematicScriptArgumentBindingResolver.FirstArgumentUsesCurrentGameObject(call))
            return (owner.OwnerKey, "The source call passes AI_gpst_CurrentGameObject as its receiver.", []);

        var ovaTargets = CinematicScriptArgumentBindingResolver.ResolveArgumentOwnerFields(
                call, 0, owner)
            .Where(binding => binding.ObjectKey != null)
            .Select(binding => (Key: binding.ObjectKey!.Value, binding.VariableName, binding.Source))
            .ToArray();
        CinematicScriptReachableSourceReference[] sourceTargets =
            CinematicScriptArgumentBindingResolver.ResolveFirstArgumentObjectReferences(call, sourceReferences);
        var matches = ovaTargets.Select(target => (target.Key,
                Evidence: $"Serialized OVA object reference {target.VariableName} resolves to {target.Key:X8} ({target.Source})."))
            .Concat(sourceTargets.Select(reference => (reference.Key,
                Evidence: $"Source pointer {reference.Symbol} resolves to {reference.Key:X8} in {Path.GetFileName(reference.Source)}:{reference.Line}.")))
            .DistinctBy(target => target.Key)
            .ToArray();
        return matches.Length switch
        {
            1 => (matches[0].Key, matches[0].Evidence,
                ResolveReceiverGuardAliases(call, matches[0].Key, owner)),
            > 1 => (null, $"Multiple owner/source object references match this call: {string.Join(", ", matches.Select(match => $"{match.Key:X8}"))}.", []),
            _ => (null, "No unique OVA/source object reference is resolved from the callback receiver argument.", [])
        };
    }

    private static string[] ResolveReceiverGuardAliases(CinematicScriptReachableEngineCall call,
        uint receiverKey, CinematicScriptOwner owner)
    {
        string[] receiverFields = CinematicScriptArgumentBindingResolver.ResolveArgumentOwnerFields(
                call, 0, owner)
            .Where(binding => binding.ObjectKey == receiverKey)
            .Select(binding => binding.VariableName)
            .ToArray();
        if (receiverFields.Length == 0) return [];

        return call.BranchConditions
            .Select(branch => Regex.Match(branch.Expression,
                @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.CultureInvariant))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Where(alias => call.ArgumentAssignments.Any(assignment => assignment.IsUnique
                && assignment.Variable == alias
                && receiverFields.Any(field => assignment.Expression.Contains($"->{field}",
                    StringComparison.OrdinalIgnoreCase))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryDescribeStateEffect(string api, string variant, out string description)
    {
        description = (api, variant) switch
        {
            ("OBJ_FlagInvisibleSet", _) => "May set/clear the receiver's invisible runtime flag",
            ("OBJ_FlagInactiveSet", _) => "May set/clear the receiver's inactive runtime flag",
            ("COLSetActivationSet", _) => "May update the receiver collision-instance activation mask as (current & ~clear) | set",
            ("OBJDestroy", _) => "May destroy the selected target: authored objects become invisible/inactive; generated objects may be detached and removed",
            ("OBJDrawMaskSet", _) => "May add/remove visual draw-mask bits; Iris requires a basic visual component and runtime visual flag 0x4000",
            ("SetLocalAlpha", _) => "May write the selected material layer's five-bit local-alpha field",
            ("OBJ_CapaSet", "CURRENT") => "May update the current object's capability mask as (current & ~remove) | set",
            ("OBJ_CapaSet", _) => "May update a source-selected object's capability mask as (current & ~remove) | set",
            ("OBJMove", _) => "May add a position offset; Iris scales it by active event duration when ENG_gb_EVERunning is set",
            ("OBJSetPos", _) => "May set the receiver's absolute position and refresh hierarchy/flash/dynamic state",
            ("OBJSetLocalPos", _) => "May set the receiver's local position and refresh flash/dynamic state",
            ("OBJReinit", _) => "May reinitialize the receiver in the requested mode and reattach it to the spatial partition",
            ("OBJ_HierarchySet", _) => "May parent the receiver under the selected object and compute a local transform",
            ("OBJ_HierarchyReset", _) => "May clear the receiver's parent binding and remove its hierarchy-table entry",
            _ => string.Empty
        };
        return description.Length > 0;
    }
}
