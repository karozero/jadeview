namespace JadeView;

internal static class CinematicShockwaveReader
{
    private const uint InitializeFunction = 0x3500C01F;
    private const string RadiusVariable = "des_f_rayon_max";

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = PcScriptModelResolver.CreateResourceMap(records);
        foreach (var owner in resources.Values.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(owner, resources, origins, out var model, out uint instanceKey)) continue;
            if (!model.References.Any(reference => reference.Key == InitializeFunction)) continue;

            var defaults = model.References.Where(reference => reference.Kind == 't')
                .Select(reference => resources.GetValueOrDefault(reference.Key))
                .Where(record => record?.Kind == 't').Cast<PcSceneLoader.Record>().Distinct().ToArray();
            if (defaults.Length != 1 || !resources.TryGetValue(instanceKey, out var instance) || instance.Kind != 't')
            {
                document.Diagnostics.Add($"Cinematic shockwave {owner.Key:X8}: defaults or instance data are unresolved.");
                continue;
            }

            try
            {
                float radius = ReadFloat(origins[defaults[0]], defaults[0], origins[instance], instance, RadiusVariable);
                if (!float.IsFinite(radius) || radius <= 0)
                    throw new InvalidDataException("Radius must be finite and positive.");
                document.CinematicShockwaveControllers[owner.Key] = new(owner.Key, radius);
            }
            catch (InvalidDataException error)
            {
                document.Diagnostics.Add($"Cinematic shockwave {owner.Key:X8}: {error.Message}");
            }
        }
    }

    private static float ReadFloat(byte[] defaultsBytes, PcSceneLoader.Record defaults,
        byte[] instanceBytes, PcSceneLoader.Record instance, string name) =>
        BitConverter.Int32BitsToSingle(PcScalarVariableReader.ReadOverlay(
            defaultsBytes.AsSpan(defaults.Offset, defaults.Size),
            instanceBytes.AsSpan(instance.Offset, instance.Size), name, PcVariableFormat.FloatType));
}
