namespace JadeView;

/// <summary>
/// Resolves only callback-local return values whose Iris behavior and receiver
/// identity are source-confirmed. Other engine call results remain unknown.
/// </summary>
internal static class CinematicScriptCallResultEvaluator
{
    internal static Dictionary<string, long?> Resolve(CinematicScriptOwner owner,
        SceneObject ownerObject, CinematicScriptCallbackStateEffect currentEffect,
        IEnumerable<CinematicScriptCallbackStateEffect> effects, float playhead,
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> timelineCalls,
        IReadOnlyDictionary<int, bool?>? savedFlags)
    {
        var results = new Dictionary<string, long?>(StringComparer.Ordinal);
        CinematicScriptCallbackStateEffect[] resultCalls = effects
            .Where(effect => effect.Api == "OBJDestroy" && effect.ResultVariable != null
                && effect.FunctionName == currentEffect.FunctionName
                && effect.Line < currentEffect.Line)
            .OrderBy(effect => effect.Line)
            .ToArray();

        foreach (IGrouping<string, CinematicScriptCallbackStateEffect> variableCalls in
                 resultCalls.GroupBy(effect => effect.ResultVariable!, StringComparer.Ordinal))
        {
            long? resolvedValue = null;
            bool hasPossibleWrite = false;
            foreach (CinematicScriptCallbackStateEffect call in variableCalls)
            {
                var receiverFields = call.ReceiverGuardAliases.ToDictionary(alias => alias,
                    _ => (long?)1, StringComparer.Ordinal);
                CinematicScriptBranchEvaluation branch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
                    owner, ownerObject, call.BranchConditions, playhead, timelineCalls,
                    savedFlags, receiverFields);
                if (branch.Value == CinematicScriptBranchValue.False) continue;

                hasPossibleWrite = true;
                resolvedValue = branch.Value == CinematicScriptBranchValue.True
                    && call.ReceiverKey is { } targetKey && targetKey != owner.OwnerKey
                        ? 1000
                        : null;
            }

            if (hasPossibleWrite) results[variableCalls.Key] = resolvedValue;
        }

        return results;
    }
}
