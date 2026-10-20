using System.Text;

namespace JadeView;

internal static class PcFloatArrayVariableReader
{
    private const ushort FloatType = 34;

    internal static float ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance,
        string name, int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        var source = Find(model, name) ?? throw new InvalidDataException("Required controller array is missing.");
        var replacement = Find(instance, name);
        var selected = replacement ?? source;
        if (source.Type != FloatType || selected.Type != source.Type || index >= selected.Length)
            throw new InvalidDataException($"Controller float array '{name}' has type={selected.Type}, length={selected.Length}, requested index={index}.");
        return selected.Values[index];
    }

    private static FloatArray? Find(ReadOnlySpan<byte> bytes, string name)
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
            uint elementCount = packedLength & 0xffff;
            ushort type = BitConverter.ToUInt16(bytes[(descriptorOffset + sizeof(uint) * 2)..]);
            bool dataFits = dataOffset + (ulong)elementCount * sizeof(float) <= (ulong)layout.StorageLength;
            bool supportedLengthFlags = (packedLength & 0xffff0000) is 0 or 0x40000000;
            if (!supportedLengthFlags || elementCount > int.MaxValue || !dataFits)
                throw new InvalidDataException($"Controller float array '{name}' has offset={dataOffset}, length={elementCount}, storage={layout.StorageLength}.");

            var values = new float[(int)elementCount];
            int arrayOffset = layout.StorageStart + (int)dataOffset;
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                values[valueIndex] = BitConverter.ToSingle(bytes[(arrayOffset + valueIndex * sizeof(float))..]);
            return new(type, values.Length, values);
        }
        return null;
    }

    private sealed record FloatArray(ushort Type, int Length, float[] Values);
}
