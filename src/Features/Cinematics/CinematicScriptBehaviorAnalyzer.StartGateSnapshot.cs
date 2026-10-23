namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const int CapabilityStartGate = 0x008;

    private static string[] AnalyzeCapabilityGateSnapshot(CinematicScriptOwner owner,
        int enabledGateMask, IEnumerable<SceneObject>? worldObjects)
    {
        if ((enabledGateMask & CapabilityStartGate) == 0) return [];

        const string referenceName = "des_o_actor_capa1";
        int? testedMask = EffectiveInteger(owner, "des_i_capa_test1");
        int? requiredState = EffectiveInteger(owner, "des_i_capa_etat1");
        CinematicScriptObjectReference? actorReference = owner.EffectiveObjectReference(referenceName);
        if (testedMask == null || requiredState == null || actorReference == null)
            return ["Capability start gate is enabled, but its target/mask/expected state is incomplete; "
                + "the gate result is unknown."];

        if (actorReference.TargetKey is 0 or uint.MaxValue or 1 or 2)
            return ["Capability start gate uses an unresolved player-actor slot or null target; "
                + "the live actor capabilities are unknown."];

        SceneObject[] matchingActors = worldObjects?
            .Where(sceneObject => sceneObject.GaoKey == actorReference.TargetKey)
            .Take(2)
            .ToArray() ?? [];
        if (matchingActors.Length != 1)
            return [$"Capability start gate target {actorReference.TargetKey:X8} is "
                + (matchingActors.Length == 0 ? "not loaded in this world" : "ambiguous in this world")
                + "; the gate result is unknown."];

        SceneObject actor = matchingActors[0];
        uint snapshotCapabilities = actor.InitialCapabilities;
        int matchedBits = unchecked((int)snapshotCapabilities) & testedMask.Value;
        int expectedBits = unchecked(requiredState.Value * testedMask.Value);
        bool snapshotPasses = matchedBits == expectedBits;
        string result = snapshotPasses ? "passes" : "fails";
        return [$"Iris capability gate snapshot {result}: {actor.Name} ({actor.GaoKey:X8}) "
            + $"has initial capabilities 0x{snapshotCapabilities:X4}; (0x{snapshotCapabilities:X8} "
            + $"& mask 0x{testedMask.Value:X8}) = 0x{matchedBits:X8}, expected "
            + $"0x{expectedBits:X8}. Earlier gameplay or event commands can change capabilities; "
            + "this archive snapshot does not prove the runtime callback result."];
    }
}
