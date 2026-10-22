using System.Buffers.Binary;
using System.Text;

namespace JadeView;

internal static class PcDialogueLineTableReader
{
    private const ushort IntegerArrayType = PcVariableFormat.IntegerType;
    private const ushort FloatArrayType = PcVariableFormat.FloatType;
    private const ushort TextReferenceType = 44;
    private const int ArrayDataHeaderSize = sizeof(uint);

    private static readonly IReadOnlyDictionary<string, ushort[]> ArrayTypes =
        new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["des_at_lignes_texte"] = [TextReferenceType],
            ["des_ai_son_associe"] = [IntegerArrayType],
            ["ai_son_differents"] = [IntegerArrayType],
            ["des_ai_encart"] = [IntegerArrayType, FloatArrayType],
            ["des_af_volume_associe"] = [FloatArrayType],
            ["des_af_near_associe"] = [FloatArrayType],
            ["des_af_far_associe"] = [FloatArrayType],
            ["des_f_duree_dialogues"] = [FloatArrayType],
            ["des_ai_perso_localisation"] = [PcVariableFormat.ObjectReferenceType]
        };

    internal static CinematicDialogueLine[]? ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance)
    {
        return ReadOverlayBlocks([model.ToArray()], instance.ToArray());
    }

    internal static CinematicDialogueLine[]? ReadOverlayBlocks(
        IEnumerable<ReadOnlyMemory<byte>> modelBlocks, ReadOnlyMemory<byte> instance)
    {
        var effectiveArrays = new Dictionary<string, DialogueArray>(StringComparer.OrdinalIgnoreCase);
        foreach (var modelBlock in modelBlocks)
        {
            foreach (var (name, array) in ReadArrays(modelBlock.Span))
                effectiveArrays.TryAdd(name, array);
        }
        foreach (var (name, array) in ReadArrays(instance.Span))
            effectiveArrays[name] = array;
        if (effectiveArrays.Values.All(array => array == null)) return null;

        int lineCount = effectiveArrays.Values.Max(array => array.Count);
        var lines = new CinematicDialogueLine[lineCount];
        for (int slot = 0; slot < lineCount; slot++)
        {
            var textReference = ReadTextReference(effectiveArrays.GetValueOrDefault("des_at_lignes_texte"), slot);
            lines[slot] = new(
                slot,
                textReference.Bank,
                textReference.Id,
                ReadInteger(effectiveArrays.GetValueOrDefault("des_ai_son_associe"), slot),
                ReadInteger(effectiveArrays.GetValueOrDefault("ai_son_differents"), slot),
                ReadFloat(effectiveArrays.GetValueOrDefault("des_ai_encart"), slot),
                ReadFloat(effectiveArrays.GetValueOrDefault("des_af_volume_associe"), slot),
                ReadFloat(effectiveArrays.GetValueOrDefault("des_af_near_associe"), slot),
                ReadFloat(effectiveArrays.GetValueOrDefault("des_af_far_associe"), slot),
                ReadFloat(effectiveArrays.GetValueOrDefault("des_f_duree_dialogues"), slot),
                ReadObjectReference(effectiveArrays.GetValueOrDefault("des_ai_perso_localisation"), slot));
        }
        return lines;
    }

    private static Dictionary<string, DialogueArray> ReadArrays(ReadOnlySpan<byte> bytes)
    {
        var arrays = new Dictionary<string, DialogueArray>(StringComparer.OrdinalIgnoreCase);
        if (bytes.IsEmpty) return arrays;

        var layout = PcVariableStorageReader.ReadLayout(bytes);
        for (int descriptorIndex = 0; descriptorIndex < layout.DescriptorCount; descriptorIndex++)
        {
            string name = ReadName(bytes, layout.NamesStart + descriptorIndex * PcVariableFormat.NameSize);
            if (!ArrayTypes.TryGetValue(name, out ushort[]? expectedTypes)) continue;

            int descriptorOffset = PcVariableFormat.BlockLengthSize
                + descriptorIndex * PcVariableFormat.DescriptorSize;
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[descriptorOffset..]);
            uint packedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(descriptorOffset + sizeof(uint))..]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(descriptorOffset + sizeof(uint) * 2)..]);
            uint count = packedLength & 0xffff;
            bool validFlags = (packedLength & 0xffff0000) is 0 or 0x40000000;
            int elementSize = ElementSize(type);
            // Iris AI_EvalVarArray_C/C2 address script-array items after the 4-byte array header.
            ulong byteCount = ArrayDataHeaderSize + (ulong)count * (uint)elementSize;
            if (!expectedTypes.Contains(type) || !validFlags || count > int.MaxValue
                || dataOffset > layout.StorageLength || byteCount > (ulong)layout.StorageLength - dataOffset)
                throw new InvalidDataException($"Dialogue OVA array '{name}' has an unsupported type or invalid storage range.");

            int dataStart = layout.StorageStart + checked((int)dataOffset) + ArrayDataHeaderSize;
            byte[] values = bytes.Slice(dataStart, checked((int)(count * (uint)elementSize))).ToArray();
            arrays[name] = new((int)count, type, values);
        }
        return arrays;
    }

    private static int ElementSize(ushort type) => type switch
    {
        TextReferenceType => sizeof(uint) * 2,
        IntegerArrayType or FloatArrayType or PcVariableFormat.ObjectReferenceType => sizeof(uint),
        _ => 0
    };

    private static (uint Bank, int Id) ReadTextReference(DialogueArray? array, int slot)
    {
        if (array == null || slot >= array.Count) return default;
        int offset = slot * sizeof(uint) * 2;
        return (BinaryPrimitives.ReadUInt32LittleEndian(array.Values.AsSpan(offset)),
            BinaryPrimitives.ReadInt32LittleEndian(array.Values.AsSpan(offset + sizeof(uint))));
    }

    private static int? ReadInteger(DialogueArray? array, int slot)
    {
        if (array == null || slot >= array.Count) return null;
        return BinaryPrimitives.ReadInt32LittleEndian(array.Values.AsSpan(slot * sizeof(uint)));
    }

    private static float? ReadFloat(DialogueArray? array, int slot)
    {
        if (array == null || slot >= array.Count) return null;
        int bits = BinaryPrimitives.ReadInt32LittleEndian(array.Values.AsSpan(slot * sizeof(uint)));
        if (array.Type == IntegerArrayType) return bits;
        float value = BitConverter.Int32BitsToSingle(bits);
        return float.IsFinite(value) ? value : null;
    }

    private static uint? ReadObjectReference(DialogueArray? array, int slot)
    {
        if (array == null || slot >= array.Count) return null;
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(array.Values.AsSpan(slot * sizeof(uint)));
        return key is 0 or uint.MaxValue ? null : key;
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int offset)
    {
        var nameBytes = bytes.Slice(offset, PcVariableFormat.NameSize);
        int terminator = nameBytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? nameBytes : nameBytes[..terminator]);
    }

    private sealed record DialogueArray(int Count, ushort Type, byte[] Values);
}
