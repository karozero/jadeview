using System.Buffers.Binary;
using JadeView;

internal static class ScriptOvaFixtureSummary
{
    internal static Summary Read(ReadOnlySpan<byte> payload)
    {
        try
        {
            PcVariableStorageLayout layout = PcVariableStorageReader.ReadLayout(payload);
            CinematicScriptVariable[] snapshots = PcVariableSnapshotReader.Read(payload, "fixture");
            var descriptors = new Descriptor[layout.DescriptorCount];
            for (int index = 0; index < descriptors.Length; index++)
            {
                int offset = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
                uint valueOffset = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
                uint packedCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + sizeof(uint))..]);
                descriptors[index] = new(index, snapshots[index].Name, snapshots[index].Type, valueOffset,
                    packedCount.ToString("X8"), packedCount & 0x3FFFFFFF, (int)(packedCount >> 30),
                    BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + PcVariableFormat.DescriptorSize - sizeof(ushort))..]).ToString("X4"),
                    snapshots[index].Value, snapshots[index].IntegerArrayValues);
            }
            ReadOnlySpan<byte> storage = payload.Slice(layout.StorageStart, layout.StorageLength);
            var words = new string[storage.Length / sizeof(uint)];
            for (int index = 0; index < words.Length; index++)
                words[index] = BinaryPrimitives.ReadUInt32LittleEndian(storage[(index * sizeof(uint))..]).ToString("X8");
            return new("DecodedStorage", null, layout.DescriptorCount, layout.StorageStart, layout.StorageLength,
                descriptors, words, Convert.ToHexString(storage[(words.Length * sizeof(uint))..]));
        }
        catch (InvalidDataException exception)
        {
            return new("InvalidLayout", exception.Message, null, null, null, [], [], null);
        }
    }

    internal sealed record Summary(string Status, string? Diagnostic, int? DescriptorCount,
        int? StorageStart, int? StorageLength, Descriptor[] Descriptors,
        string[] UntypedStorageWordsLittleEndian, string? TrailingStorageBytesHex);
    internal sealed record Descriptor(int Index, string Name, ushort Type, uint StorageOffset,
        string PackedArrayCount, uint ElementCount, int ArrayDimensions, string DescriptorTailHex,
        string ValuePreview, int[]? IntegerArrayValues);
}
