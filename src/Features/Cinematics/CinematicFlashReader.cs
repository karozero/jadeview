namespace JadeView;

internal static class CinematicFlashReader
{
    private const uint FlashBehavior = 0xBF002518;

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = PcScriptModelResolver.CreateResourceMap(records);
        foreach (var owner in resources.Values.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(owner, resources, origins, out var model, out uint instanceKey)
                || !model.References.Any(reference => reference.Key == FlashBehavior)) continue;
            var defaultKeys = model.References.Where(reference => reference.Kind == 't').ToArray();
            if (defaultKeys.Length != 1 || !resources.TryGetValue(defaultKeys[0].Key, out var defaults)
                || defaults.Kind != 't' || !resources.TryGetValue(instanceKey, out var instance)
                || instance.Kind != 't') continue;
            try
            {
                var defaultBytes = origins[defaults].AsSpan(defaults.Offset, defaults.Size);
                var instanceBytes = origins[instance].AsSpan(instance.Offset, instance.Size);
                var controller = new CinematicFlashController(owner.Key,
                    unchecked((uint)ReadInteger(defaultBytes, instanceBytes, "des_o_snap_actor", PcVariableFormat.ObjectReferenceType)),
                    PcVector3VariableReader.ReadOverlay(defaultBytes, instanceBytes, "des_v_offset"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_apparition"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_delai_disparition"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_extrude_dist"),
                    unchecked((uint)ReadInteger(defaultBytes, instanceBytes, "des_c_color", PcVariableFormat.PackedColorType)),
                    ReadInteger(defaultBytes, instanceBytes, "des_i_nb_rayons", PcVariableFormat.IntegerType),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_big_alpha"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_big_length_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_big_length_max"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_big_width_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_big_width_max"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_small_alpha"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_small_length_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_small_length_max"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_small_width_min"),
                    ReadFloat(defaultBytes, instanceBytes, "des_f_small_width_max"),
                    owner.InitialCapabilities);
                if (IsValid(controller)) document.CinematicFlashControllers[owner.Key] = controller;
                else document.Diagnostics.Add($"Cinematic flash {owner.Key:X8}: script parameters are outside supported ranges.");
            }
            catch (InvalidDataException error)
            {
                document.Diagnostics.Add($"Cinematic flash {owner.Key:X8}: {error.Message}");
            }
        }
    }

    private static int ReadInteger(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name, ushort type) =>
        PcScalarVariableReader.ReadOverlay(defaults, instance, name, type);

    private static float ReadFloat(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name) =>
        BitConverter.Int32BitsToSingle(ReadInteger(defaults, instance, name, PcVariableFormat.FloatType));

    private static bool IsValid(CinematicFlashController controller)
    {
        bool hasValidDelays = float.IsFinite(controller.AppearDelay + controller.DisappearDelay
            + controller.ExtrudeDistance)
            && controller.AppearDelay >= 0 && controller.DisappearDelay >= 0 && controller.ExtrudeDistance >= 0;
        bool hasValidRayCount = controller.RayCount is > 0 and <= 128;
        bool hasFiniteRayDimensions = float.IsFinite(controller.BigAlpha + controller.BigLengthMin
            + controller.BigLengthMax + controller.BigWidthMin + controller.BigWidthMax + controller.SmallAlpha
            + controller.SmallLengthMin + controller.SmallLengthMax + controller.SmallWidthMin
            + controller.SmallWidthMax);

        return controller.SnapActorKey != uint.MaxValue
            && float.IsFinite(controller.Offset.LengthSquared())
            && hasValidDelays && hasValidRayCount && hasFiniteRayDimensions;
    }
}
