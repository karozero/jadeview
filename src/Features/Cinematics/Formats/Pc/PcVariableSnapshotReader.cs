using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace JadeView;

internal static class PcVariableSnapshotReader
{
    private const int MaximumDisplayedArrayValues = 8;
    private const int MaximumSemanticIntegerArrayValues = 64;

    internal static CinematicScriptVariable[] Read(ReadOnlySpan<byte> bytes, string source)
    {
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        var variables = new List<CinematicScriptVariable>(layout.DescriptorCount);
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            int descriptorOffset = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
            uint valueOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[descriptorOffset..]);
            uint packedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(descriptorOffset + sizeof(uint))..]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(descriptorOffset + sizeof(uint) * 2)..]);
            string name = ReadName(bytes, layout.NamesStart + index * PcVariableFormat.NameSize);
            string value = DescribeValue(bytes, layout, valueOffset, packedLength, type);
            int count = (int)(packedLength & 0x3fffffff);
            int dimensions = (int)(packedLength >> 30);
            bool isArray = dimensions > 0 || count != 1;
            int[]? integerArrayValues = isArray && type == PcVariableFormat.IntegerType
                ? ReadIntegerArray(bytes, layout, valueOffset, packedLength)
                : null;
            variables.Add(new(name, type, source, value, integerArrayValues,
                isArray ? count : null));
        }
        return variables.ToArray();
    }

    private static string DescribeValue(ReadOnlySpan<byte> bytes, PcVariableStorageLayout layout,
        uint valueOffset, uint packedLength, ushort type)
    {
        uint count = packedLength & 0x3fffffff;
        int dimensions = (int)(packedLength >> 30);
        int elementSize = ElementSize(type);
        ulong headerSize = (ulong)dimensions * sizeof(uint);
        ulong valuesSize = (ulong)count * (uint)elementSize;
        bool hasValidStorage = elementSize > 0 && valueOffset <= layout.StorageLength
            && headerSize + valuesSize <= (ulong)layout.StorageLength - valueOffset;
        if (!hasValidStorage)
            return count == 1 && dimensions == 0
                ? $"type {type}, raw value unavailable"
                : $"array ({count} entries), raw values unavailable";

        int absoluteOffset = checked(layout.StorageStart + (int)valueOffset + (int)headerSize);
        var valueBytes = bytes.Slice(absoluteOffset, checked((int)valuesSize));
        if (dimensions == 0 && count == 1)
            return FormatValue(valueBytes, type);

        if (elementSize != sizeof(uint)) return $"array ({count} entries), type {type}";
        int displayedCount = (int)Math.Min(count, MaximumDisplayedArrayValues);
        var displayedValues = new string[displayedCount];
        for (int index = 0; index < displayedCount; index++)
        {
            displayedValues[index] = FormatValue(valueBytes.Slice(index * elementSize, elementSize), type);
        }
        string remainder = count > displayedCount ? $", … +{count - displayedCount}" : "";
        return $"[{string.Join(", ", displayedValues)}{remainder}] ({count} entries)";
    }

    private static int ElementSize(ushort type) => PcVariableFormat.IsSupportedWordType(type)
        ? sizeof(uint) : type switch
    {
        37 => 12,
        41 => 52,
        44 => 8,
        _ => 0
    };

    private static int[]? ReadIntegerArray(ReadOnlySpan<byte> bytes, PcVariableStorageLayout layout,
        uint valueOffset, uint packedLength)
    {
        uint count = packedLength & 0x3fffffff;
        int dimensions = (int)(packedLength >> 30);
        if (count > MaximumSemanticIntegerArrayValues) return null;
        ulong headerSize = (ulong)dimensions * sizeof(uint);
        ulong valuesSize = (ulong)count * sizeof(uint);
        if (valueOffset > layout.StorageLength
            || headerSize + valuesSize > (ulong)layout.StorageLength - valueOffset)
            return null;

        int absoluteOffset = checked(layout.StorageStart + (int)valueOffset + (int)headerSize);
        var values = new int[checked((int)count)];
        for (int index = 0; index < values.Length; index++)
            values[index] = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(absoluteOffset + index * sizeof(uint), sizeof(uint)));
        return values;
    }

    private static string FormatValue(ReadOnlySpan<byte> valueBytes, ushort type)
    {
        return type switch
        {
            PcVariableFormat.BooleanType => FormatBoolean(valueBytes),
            PcVariableFormat.IntegerType => BinaryPrimitives.ReadInt32LittleEndian(valueBytes)
                .ToString(CultureInfo.InvariantCulture),
            PcVariableFormat.FloatType => FormatFloat(valueBytes),
            PcVariableFormat.ObjectReferenceType => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(valueBytes):X8}",
            PcVariableFormat.KeyType => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(valueBytes):X8}",
            PcVariableFormat.PackedColorType => $"0x{BinaryPrimitives.ReadUInt32LittleEndian(valueBytes):X8}",
            _ => Convert.ToHexString(valueBytes)
        };
    }

    private static string FormatBoolean(ReadOnlySpan<byte> bytes)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return $"{(value == 0 ? "false" : "true")} (0x{value:X8})";
    }

    private static string FormatFloat(ReadOnlySpan<byte> bytes)
    {
        float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        return float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "non-finite";
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int offset)
    {
        var name = bytes.Slice(offset, PcVariableFormat.NameSize);
        int terminator = name.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? name : name[..terminator]);
    }
}
