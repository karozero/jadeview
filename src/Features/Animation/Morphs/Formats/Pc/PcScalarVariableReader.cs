using System.Text;

namespace JadeView;

// Bounded scalar view of the source OVA storage; no runtime reference fixups.
internal static class PcScalarVariableReader
{
    internal static PcScalarVariable? Find(ReadOnlySpan<byte> bytes, string name)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            string sourceName = ReadName(bytes, layout.NamesStart + index * PcVariableFormat.NameSize);
            if (sourceName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return ReadScalar(bytes, layout, index);
        }
        return null;
    }

    internal static int ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance, string name, ushort type)
    {
        var initial = Find(model, name) ?? throw new InvalidDataException("Required controller scalar is missing.");
        var replacement = Find(instance, name);
        bool incompatibleOverride = replacement != null && replacement.Type != initial.Type;
        if (initial.Type != type || incompatibleOverride)
            throw new InvalidDataException("Controller scalar override has an incompatible type.");
        return (replacement ?? initial).Value;
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int offset)
    {
        var nameBytes = bytes.Slice(offset, PcVariableFormat.NameSize);
        int terminator = nameBytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? nameBytes : nameBytes[..terminator]);
    }

    private static PcScalarVariable ReadScalar(ReadOnlySpan<byte> bytes, PcVariableStorageLayout layout, int index)
    {
        int descriptor = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
        uint offset = BitConverter.ToUInt32(bytes[descriptor..]);
        uint packed = BitConverter.ToUInt32(bytes[(descriptor + sizeof(uint))..]);
        ushort type = BitConverter.ToUInt16(bytes[(descriptor + sizeof(uint) * 2)..]);
        bool supportedType = type is PcVariableFormat.IntegerType or PcVariableFormat.ObjectReferenceType;
        bool withinStorage = offset + (ulong)sizeof(int) <= (ulong)layout.StorageLength;
        if (packed != PcVariableFormat.ScalarLength || !supportedType || !withinStorage)
            throw new InvalidDataException("Unsupported OVA scalar layout.");
        return new(type, packed, BitConverter.ToInt32(bytes[(layout.StorageStart + (int)offset)..]));
    }
}
