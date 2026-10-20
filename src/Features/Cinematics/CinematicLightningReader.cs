namespace JadeView;

internal static class CinematicLightningReader
{
    private const uint Behavior = 0x72001350;
    private const uint Initialize = 0x72001351;

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = PcScriptModelResolver.CreateResourceMap(records);
        foreach (var owner in resources.Values.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(owner, resources, origins, out var model, out uint instanceKey)
                || !model.References.Any(reference => reference.Key == Behavior || reference.Key == Initialize)) continue;
            var defaultRecords = model.References.Where(reference => reference.Kind == 't')
                .Select(reference => resources.GetValueOrDefault(reference.Key))
                .Where(record => record?.Kind == 't').Cast<PcSceneLoader.Record>().Distinct().ToArray();
            if (defaultRecords.Length != 1 || !resources.TryGetValue(defaultRecords[0].Key, out var defaults)
                || !resources.TryGetValue(instanceKey, out var instance) || instance.Kind != 't') continue;
            try
            {
                var defaultBytes = origins[defaults].AsSpan(defaults.Offset, defaults.Size);
                var instanceBytes = origins[instance].AsSpan(instance.Offset, instance.Size);
                var controller = new CinematicLightningController(owner.Key,
                    PcObjectVariableReader.ReadOverlay(defaultBytes, instanceBytes, "des_o_actor_capa") ?? owner.Key,
                    PcObjectVariableReader.ReadOverlay(defaultBytes, instanceBytes, "des_o_wp_dest") ?? 0,
                    owner.InitialCapabilities, ReadInteger(defaultBytes, instanceBytes, "des_mode"),
                    ReadInteger(defaultBytes, instanceBytes, "des_i_capa_test"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_ON_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_ON_max"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_OFF_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_OFF_max"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_fade_in"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_fade_out"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_epaisseur"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_rayon"),
                    ReadInteger(defaultBytes, instanceBytes, "des_i_decoupage"),
                    ReadInteger(defaultBytes, instanceBytes, "des_lightning_mode"),
                    ReadColor(defaultBytes, instanceBytes, "des_c_color_OFF"),
                    ReadColor(defaultBytes, instanceBytes, "des_c_color_ON_1"),
                    ReadColor(defaultBytes, instanceBytes, "des_c_color_ON_2"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_freq"));
                if (IsValid(controller)) document.CinematicLightningControllers[owner.Key] = controller;
                else document.Diagnostics.Add($"Cinematic lightning {owner.Key:X8}: script parameters are outside supported ranges.");
            }
            catch (InvalidDataException error)
            {
                document.Diagnostics.Add($"Cinematic lightning {owner.Key:X8}: {error.Message}");
            }
        }
    }

    private static int ReadScalar(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance,
        string name, ushort expectedType)
    {
        var initial = PcScalarVariableReader.Find(defaults, name)
            ?? throw new InvalidDataException($"Required controller field '{name}' is missing.");
        var replacement = PcScalarVariableReader.Find(instance, name);
        if (initial.Type != expectedType || replacement != null && replacement.Type != initial.Type)
            throw new InvalidDataException($"Controller field '{name}' has an incompatible type.");
        return (replacement ?? initial).Value;
    }

    private static int ReadInteger(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name) =>
        ReadScalar(defaults, instance, name, PcVariableFormat.IntegerType);

    private static float ReadFloat(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name) =>
        BitConverter.Int32BitsToSingle(ReadScalar(defaults, instance, name, PcVariableFormat.FloatType));

    private static uint ReadColor(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name) =>
        unchecked((uint)ReadScalar(defaults, instance, name, PcVariableFormat.PackedColorType));

    private static bool IsValid(CinematicLightningController controller)
    {
        bool hasValidTiming = Finite(controller.OnDelayMin, controller.OnDelayMax,
                controller.OffDelayMin, controller.OffDelayMax, controller.FadeIn,
                controller.FadeOut, controller.Frequency)
            && controller.OnDelayMin >= 0 && controller.OnDelayMax >= controller.OnDelayMin
            && controller.OffDelayMin >= 0 && controller.OffDelayMax >= controller.OffDelayMin
            && controller.FadeIn >= 0 && controller.FadeOut >= 0;
        bool hasValidShape = Finite(controller.Width, controller.Radius)
            && controller.Width >= 0 && controller.Radius >= 0
            && controller.SegmentCount is > 0 and <= 128;

        return controller.Mode is >= 0 and <= 2 && hasValidTiming && hasValidShape;
    }

    private static bool Finite(params float[] values) => values.All(float.IsFinite);
}
