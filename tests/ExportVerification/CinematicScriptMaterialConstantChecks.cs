using JadeView;

internal static class CinematicScriptMaterialConstantChecks
{
    internal static void Verify()
    {
        const uint ownerKey = 0x10004AE8;
        const uint worldKey = 0xFF0045A2;
        const uint materialKey = 0x43000A8B;
        var document = new SceneDocument();
        document.CinematicScriptOwners[new(worldKey, ownerKey)] = new(ownerKey, 0x3D002D78,
            0x3D002D5B, worldKey, 0, 0, "shield controller",
            ["sfx_bouclier_phare_sbp_init"], [], [])
        {
            ModelFunctionNames = ["sfx_bouclier_phare_sbp_init"]
        };
        var world = new WorldData { Key = worldKey };
        world.Objects.Add(new SceneObject { GaoKey = ownerKey, MaterialKey = materialKey });
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [materialKey] = new() { Id = new(materialKey), Type = 5 }
        };

        var playback = CinematicScriptMaterialConstantPlayback.Create(document, world,
            materials, []);
        if (playback == null)
            throw new InvalidDataException("A reachable static MATSetConstant callback was not discovered.");

        IReadOnlyDictionary<uint, uint> unresolvedConstants = playback.Evaluate(0, null);
        if (unresolvedConstants.ContainsKey(materialKey))
            throw new InvalidDataException("Unknown saved flag 2452 must not guess whether the callback writes its material constant.");

        // The source setter is under `!AI_EvalFunc_ARRBitTest_C(..., 2452)`.
        // Model the known first-run state explicitly; an unknown persistent flag
        // must stay unresolved rather than being guessed by the preview.
        var savedFlags = new Dictionary<int, bool?> { [2452] = false };
        IReadOnlyDictionary<uint, uint> constants = playback.Evaluate(0, savedFlags);
        if (!constants.TryGetValue(materialKey, out uint packedColor) || packedColor != 0x444444)
            throw new InvalidDataException($"Expected source-backed callback material color 0x444444, got 0x{packedColor:X8}; diagnostics={string.Join(" | ", playback.Diagnostics)}; callbacks={string.Join(",", document.CinematicScriptOwners[new(worldKey, ownerKey)].CallbackNames)}");

        savedFlags[2452] = true;
        if (playback.Evaluate(1, savedFlags).ContainsKey(materialKey))
            throw new InvalidDataException("Changing a saved flag in the same dictionary must invalidate a cached material callback window.");
        savedFlags[2452] = false;
        if (!playback.Evaluate(0, savedFlags).ContainsKey(materialKey)
            || playback.Evaluate(0, null).ContainsKey(materialKey))
            throw new InvalidDataException("Reverse seek and returning to unknown flags must preserve callback gating.");

        Console.WriteLine("PASS: source-reachable MATSetConstant callback applies a static material color, not alpha");
    }
}
