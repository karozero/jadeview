using System.Text;
using System.Numerics;

namespace JadeView;

internal static class PcVector3VariableReader
{
    private const ushort Vector3Type = 37;

    internal static Vector3 ReadOverlay(ReadOnlySpan<byte> defaults, ReadOnlySpan<byte> instance, string name)
    {
        var initial = Find(defaults, name) ?? throw new InvalidDataException($"Required controller vector '{name}' is missing.");
        var replacement = Find(instance, name);
        var selected = replacement ?? initial;
        if (initial.Type != Vector3Type || selected.Type != Vector3Type
            || !float.IsFinite(selected.Value.LengthSquared()))
            throw new InvalidDataException($"Controller vector '{name}' has an incompatible type or value.");
        return selected.Value;
    }

    private static VectorValue? Find(ReadOnlySpan<byte> bytes, string name)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            var encodedName = bytes.Slice(layout.NamesStart + index * PcVariableFormat.NameSize, PcVariableFormat.NameSize);
            int end = encodedName.IndexOf((byte)0);
            if (!Encoding.Latin1.GetString(end < 0 ? encodedName : encodedName[..end])
                .Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            int descriptor = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
            uint offset = BitConverter.ToUInt32(bytes[descriptor..]);
            uint packedLength = BitConverter.ToUInt32(bytes[(descriptor + sizeof(uint))..]);
            ushort type = BitConverter.ToUInt16(bytes[(descriptor + sizeof(uint) * 2)..]);
            if (packedLength != PcVariableFormat.ScalarLength || type != Vector3Type
                || offset + 3UL * sizeof(float) > (ulong)layout.StorageLength)
                throw new InvalidDataException($"Controller vector '{name}' has an unsupported storage layout.");
            int data = layout.StorageStart + (int)offset;
            return new(type, new(BitConverter.ToSingle(bytes[data..]), BitConverter.ToSingle(bytes[(data + 4)..]),
                BitConverter.ToSingle(bytes[(data + 8)..])));
        }
        return null;
    }

    private sealed record VectorValue(ushort Type, Vector3 Value);
}
