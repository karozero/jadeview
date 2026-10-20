using System.Numerics;
using System.Text;

namespace JadeView;

internal static class CinematicScreenControllerReader
{
    // Native Cine_FigeleMonde entry points, not scene/actor identities.
    private const uint FreezeFunction = 0x3500b8fe, ReleaseFunction = 0x3500b902;
    private const uint GlobalReflexFunction = 0x7100bd05;

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = PcScriptModelResolver.CreateResourceMap(records);
        foreach (var root in resources.Values.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(root, resources, origins, out var model, out uint instanceKey)) continue;
            var keys = model.References.Select(reference => reference.Key).ToHashSet();
            if (keys.Contains(GlobalReflexFunction)) document.CinematicFadeReceivers.Add(root.Key);
            if (!keys.Contains(FreezeFunction) || !keys.Contains(ReleaseFunction)) continue;
            var defaultsKeys = model.References.Where(reference => reference.Kind == 't').ToArray();
            if (defaultsKeys.Length != 1 || !resources.TryGetValue(defaultsKeys[0].Key, out var defaults)
                || !resources.TryGetValue(instanceKey, out var instance)) continue;
            try
            {
                var modelBytes = origins[defaults].AsSpan(defaults.Offset, defaults.Size);
                var instanceBytes = origins[instance].AsSpan(instance.Offset, instance.Size);
                float close = PcFloatArrayVariableReader.ReadOverlay(modelBytes, instanceBytes, "des_f_temps_iris_1", 0);
                float hold = PcFloatArrayVariableReader.ReadOverlay(modelBytes, instanceBytes, "des_f_temps_obturation", 0);
                float open = PcFloatArrayVariableReader.ReadOverlay(modelBytes, instanceBytes, "des_f_temps_iris_2", 0);
                var center = ReadCenter(instanceBytes) ?? ReadCenter(modelBytes);
                bool valid = center != null && float.IsFinite(close + hold + open) && close >= 0 && hold >= 0
                    && open >= 0 && close + hold + open > 0;
                if (valid) document.CinematicShutterControllers[root.Key] = new(close, hold, open, center!.Value, root.InitialCapabilities);
            }
            catch (InvalidDataException error) { document.Diagnostics.Add($"Shutter controller {root.Key:X8}: {error.Message}"); }
        }
    }

    private static Vector2? ReadCenter(ReadOnlySpan<byte> bytes)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            var name = bytes.Slice(layout.NamesStart + index * PcVariableFormat.NameSize, PcVariableFormat.NameSize);
            int end = name.IndexOf((byte)0);
            if (Encoding.Latin1.GetString(end < 0 ? name : name[..end]) != "des_v_centre_iris") continue;
            int descriptor = 4 + index * PcVariableFormat.DescriptorSize;
            uint offset = BitConverter.ToUInt32(bytes[descriptor..]);
            if (BitConverter.ToUInt32(bytes[(descriptor + 4)..]) != 1
                || BitConverter.ToUInt16(bytes[(descriptor + 8)..]) != 37 || offset + 12UL > (ulong)layout.StorageLength)
                throw new InvalidDataException("Unsupported shutter center variable.");
            int data = layout.StorageStart + (int)offset;
            var value = new Vector2(BitConverter.ToSingle(bytes[data..]), BitConverter.ToSingle(bytes[(data + 4)..]));
            if (!float.IsFinite(value.LengthSquared())) throw new InvalidDataException("Nonfinite shutter center.");
            return value;
        }
        return null;
    }
}
