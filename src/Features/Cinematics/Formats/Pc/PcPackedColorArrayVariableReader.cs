using System.Text;

namespace JadeView;

internal static class PcPackedColorArrayVariableReader
{
    private const ushort PackedColorType = 46;

    internal static uint[] ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance, string name)
    {
        var defaults = Find(model, name) ?? throw new InvalidDataException($"Required controller color array '{name}' is missing.");
        var replacement = Find(instance, name);
        var selected = replacement ?? defaults;
        if (defaults.Type != PackedColorType || selected.Type != PackedColorType || selected.Values.Length < defaults.Values.Length)
            throw new InvalidDataException($"Controller color array '{name}' has an incompatible type or length.");
        return selected.Values;
    }

    private static PackedColorArray? Find(ReadOnlySpan<byte> bytes, string name)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            int nameOffset = layout.NamesStart + index * PcVariableFormat.NameSize;
            var nameBytes = bytes.Slice(nameOffset, PcVariableFormat.NameSize);
            int terminator = nameBytes.IndexOf((byte)0);
            string variableName = Encoding.Latin1.GetString(terminator < 0 ? nameBytes : nameBytes[..terminator]);
            if (!variableName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

            int descriptorOffset = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
            uint dataOffset = BitConverter.ToUInt32(bytes[descriptorOffset..]);
            uint packedLength = BitConverter.ToUInt32(bytes[(descriptorOffset + sizeof(uint))..]);
            ushort type = BitConverter.ToUInt16(bytes[(descriptorOffset + sizeof(uint) * 2)..]);
            uint elementCount = packedLength & 0xffff;
            bool validLengthFlags = (packedLength & 0xffff0000) is 0 or 0x40000000;
            bool dataFits = dataOffset + ((ulong)elementCount + 1) * sizeof(uint) <= (ulong)layout.StorageLength;
            if (!validLengthFlags || elementCount > int.MaxValue || !dataFits)
                throw new InvalidDataException($"Controller color array '{name}' has an invalid packed length or storage range.");

            int arrayOffset = layout.StorageStart + (int)dataOffset;
            uint declaredCount = BitConverter.ToUInt32(bytes[arrayOffset..]);
            if (declaredCount != elementCount)
                throw new InvalidDataException($"Controller color array '{name}' has a mismatched stored dimension.");
            var values = new uint[(int)elementCount];
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                values[valueIndex] = BitConverter.ToUInt32(bytes[(arrayOffset + sizeof(uint) + valueIndex * sizeof(uint))..]);
            return new(type, values);
        }
        return null;
    }

    private sealed record PackedColorArray(ushort Type, uint[] Values);
}
