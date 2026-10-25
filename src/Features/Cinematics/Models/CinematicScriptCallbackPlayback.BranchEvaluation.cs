namespace JadeView;

internal sealed partial class CinematicScriptCallbackPlayback
{
    // Reuse message-derived OVA snapshots across effects of the same owner in
    // one evaluation. Capabilities and sample time are part of the identity;
    // changing the playhead always clears these runtime snapshots.
    private readonly Dictionary<(CinematicScriptOwnerId Owner, float Time, uint? Capabilities),
        Dictionary<string, long?>> branchFieldSnapshots = [];

    private CinematicScriptBranchEvaluation EvaluateBranch(CinematicScriptOwner owner,
        SceneObject ownerObject, CinematicScriptBranchConditionSource[] conditions,
        float sampleTime, IReadOnlyDictionary<string, long?> additionalFields)
    {
        if (conditions.Length == 0)
            return new(CinematicScriptBranchValue.True, [], "The callback call site has no recorded enclosing branch.");

        var key = (owner.Id, sampleTime, ownerObject.PreviewCapabilities);
        if (!branchFieldSnapshots.TryGetValue(key, out var fields))
        {
            fields = CinematicScriptRuntimeBranchEvaluator.ResolveFieldValues(owner,
                ownerObject, sampleTime, OwnerTimeline(owner));
            branchFieldSnapshots.Add(key, fields);
        }

        if (additionalFields.Count > 0)
        {
            fields = new Dictionary<string, long?>(fields, StringComparer.OrdinalIgnoreCase);
            foreach (var field in additionalFields) fields[field.Key] = field.Value;
        }
        return CinematicScriptRuntimeBranchEvaluator.Evaluate(owner,
            ownerObject.PreviewCapabilities, conditions, fields, savedFlagAssumptions, sampleTime);
    }
}
