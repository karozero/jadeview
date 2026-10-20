using System.Text;

namespace JadeView;

internal static class PcObjectVariableReader
{
    internal static uint? ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance, string name)
    {
        var defaults = Read(model, name);
        return Read(instance, name) ?? defaults;
    }

    private static uint? Read(ReadOnlySpan<byte> bytes, string name)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            var encodedName = bytes.Slice(layout.NamesStart + index * PcVariableFormat.NameSize, PcVariableFormat.NameSize);
            int end = encodedName.IndexOf((byte)0);
            if (Encoding.Latin1.GetString(end < 0 ? encodedName : encodedName[..end]) != name) continue;
            int descriptor = 4 + index * PcVariableFormat.DescriptorSize;
            uint offset = BitConverter.ToUInt32(bytes[descriptor..]);
            bool supported = BitConverter.ToUInt32(bytes[(descriptor + 4)..]) == 1
                && BitConverter.ToUInt16(bytes[(descriptor + 8)..]) == 40
                && offset + 4UL <= (ulong)layout.StorageLength;
            if (!supported) throw new InvalidDataException($"Unsupported object variable {name}.");
            return BitConverter.ToUInt32(bytes[(layout.StorageStart + (int)offset)..]);
        }
        return null;
    }
}
