namespace JadeView;

internal static class CinematicEnvironmentReader
{
    private const uint CycleFunction = 0x5e000004, InitializeFunction = 0x5e000040;
    private static readonly string[] ObjectFields = ["des_o_env", "des_o_sky", "des_o_fog", "des_o_sun",
        "des_o_moon", "des_o_moon_light", "des_o_moon_rayon", "des_o_desactivation",
        "des_standard_world_cycle"];
    private static readonly string[] NormalPaletteNames = ["des_ac_nuit", "des_ac_aurore", "des_ac_coucher", "des_ac_jour"];
    private static readonly string[] AttackPaletteNames = ["des_ac_nuit_attaque", "des_ac_aurore_attaque",
        "des_ac_coucher_attaque", "des_ac_jour_attaque"];

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = PcScriptModelResolver.CreateResourceMap(records);
        foreach (var root in resources.Values.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(root, resources, origins, out var model, out uint instanceKey)) continue;
            bool nativeRole = model.References.Any(reference => reference.Key is CycleFunction or InitializeFunction);
            if (!nativeRole) continue;
            var defaultsReferences = model.References.Where(reference => reference.Kind == 't').ToArray();
            if (defaultsReferences.Length != 1 || !resources.TryGetValue(defaultsReferences[0].Key, out var defaults)
                || !resources.TryGetValue(instanceKey, out var instance)) continue;
            try
            {
                var keys = new HashSet<uint>();
                var objectKeys = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (string field in ObjectFields)
                {
                    uint? key = PcObjectVariableReader.ReadOverlay(origins[defaults].AsSpan(defaults.Offset, defaults.Size),
                        origins[instance].AsSpan(instance.Offset, instance.Size), field);
                    if (key is > 2 and < uint.MaxValue)
                    {
                        keys.Add(key.Value);
                        objectKeys[field] = key.Value;
                    }
                }
                var normalPalettes = NormalPaletteNames.Select(field =>
                    PcPackedColorArrayVariableReader.ReadOverlay(
                        origins[defaults].AsSpan(defaults.Offset, defaults.Size),
                        origins[instance].AsSpan(instance.Offset, instance.Size), field)).ToArray();
                var attackPalettes = AttackPaletteNames.Select(field =>
                    PcPackedColorArrayVariableReader.ReadOverlay(
                        origins[defaults].AsSpan(defaults.Offset, defaults.Size),
                        origins[instance].AsSpan(instance.Offset, instance.Size), field)).ToArray();
                float ReadHeight(string name) => BitConverter.Int32BitsToSingle(
                    PcScalarVariableReader.ReadOverlay(
                        origins[defaults].AsSpan(defaults.Offset, defaults.Size),
                        origins[instance].AsSpan(instance.Offset, instance.Size), name, PcVariableFormat.FloatType));
                var controller = new CinematicEnvironmentController(root.Key, root.LoadWorldKey, keys.ToArray())
                {
                    EnvironmentKey = objectKeys.GetValueOrDefault("des_o_env"),
                    SkyKey = objectKeys.GetValueOrDefault("des_o_sky"),
                    FogKey = objectKeys.GetValueOrDefault("des_o_fog"),
                    SunKey = objectKeys.GetValueOrDefault("des_o_sun"),
                    MoonKey = objectKeys.GetValueOrDefault("des_o_moon"),
                    MoonLightKey = objectKeys.GetValueOrDefault("des_o_moon_light"),
                    MoonRayKey = objectKeys.GetValueOrDefault("des_o_moon_rayon"),
                    DeactivationObjectKey = objectKeys.GetValueOrDefault("des_o_desactivation"),
                    StandardControllerKey = objectKeys.GetValueOrDefault("des_standard_world_cycle"),
                    NightHeight = ReadHeight("des_f_Z_nuit"),
                    DawnHeight = ReadHeight("des_f_Z_aurore"),
                    SunriseHeight = ReadHeight("des_f_Z_lever"),
                    DayHeight = ReadHeight("des_f_Z_jour"),
                    NightRliFactor = ReadHeight("des_f_RLI_nuit"),
                    DawnRliFactor = ReadHeight("des_f_RLI_aurore"),
                    SunsetRliFactor = ReadHeight("des_f_RLI_coucher"),
                    DayRliFactor = ReadHeight("des_f_RLI_jour"),
                    FogNear = ReadHeight("des_f_fog_near"),
                    FogFar = ReadHeight("des_f_fog_far"),
                    AttackFogNear = ReadHeight("des_f_fog_near_attaque"),
                    AttackFogFar = ReadHeight("des_f_fog_far_attaque"),
                    AttackTransitionSpeed = ReadHeight("f_vitesse_transition_vers_att"),
                    AttackReturnSpeed = ReadHeight("f_vitesse_transition_retour_v"),
                    NormalPalettes = normalPalettes,
                    AttackPalettes = attackPalettes
                };
                document.CinematicEnvironmentControllers[new(root.LoadWorldKey, root.Key)] = controller;

            }
            catch (InvalidDataException error) { document.Diagnostics.Add($"Environment controller {root.Key:X8}: {error.Message}"); }
        }
    }
}
