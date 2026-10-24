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
                (uint? receiverKey, string receiverEvidence) = ResolveReceiver(
                    owner, call, sourceReferences);
                return new CinematicScriptCallbackStateEffect(owner.OwnerKey, effect, call.Api,
                    call.Variant, call.Arguments, call.BranchConditions, call.FunctionName,
                    call.Source, call.Line, call.CallbackPath, "SourceCallCandidate")
                {
                    ReceiverKey = receiverKey,
                    ReceiverEvidence = receiverEvidence,
                    SerializedBranchFindings = CinematicScriptBranchAnalyzer.Describe(
                        owner, call.BranchConditions)
                };
            })
            .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
    }

    private static (uint? ReceiverKey, string Evidence) ResolveReceiver(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall call,
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences)
    {
        if (call.Variant.Equals("CURRENT", StringComparison.Ordinal))
            return (owner.OwnerKey, "Iris CURRENT API variant uses the callback's current game object.");

        if (CinematicScriptArgumentBindingResolver.FirstArgumentUsesCurrentGameObject(call))
            return (owner.OwnerKey, "The source call passes AI_gpst_CurrentGameObject as its receiver.");

        CinematicScriptReachableSourceReference[] matches =
            CinematicScriptArgumentBindingResolver.ResolveFirstArgumentObjectReferences(call, sourceReferences);
        return matches.Length switch
        {
            1 => (matches[0].Key,
                $"Source pointer {matches[0].Symbol} resolves to {matches[0].Key:X8} in {Path.GetFileName(matches[0].Source)}:{matches[0].Line}."),
            > 1 => (null, $"Multiple source object references match this call: {string.Join(", ", matches.Select(match => $"{match.Symbol}={match.Key:X8}"))}."),
            _ => (null, "No unique receiver pointer is resolved from the callback arguments.")
        };
    }

    private static bool TryDescribeStateEffect(string api, string variant, out string description)
    {
        description = (api, variant) switch
        {
            ("OBJ_FlagInvisibleSet", _) => "May set/clear the receiver's invisible runtime flag",
            ("OBJ_FlagInactiveSet", _) => "May set/clear the receiver's inactive runtime flag",
            ("OBJDestroy", _) => "May destroy the selected target: authored objects become invisible/inactive; generated objects may be detached and removed",
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
