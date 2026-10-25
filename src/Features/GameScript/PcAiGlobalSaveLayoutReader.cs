using System.Buffers.Binary;

namespace JadeView;

/// <summary>Calculates the ordered AI save-buffer layout described by Universe OVA flags.</summary>
internal static class PcAiGlobalSaveLayoutReader
{
    private const ushort SaveVariableFlag = 0x10;
    private const int MaximumSaveBufferBytes = 16 * 1024 * 1024;

    internal static PcAiGlobalSaveLayout Read(IEnumerable<PcAiGlobalVariableDescriptor> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var savedFields = new List<PcAiSavedFieldLayout>();
        int bufferOffset = 0;

        foreach (PcAiGlobalVariableDescriptor variable in variables)
        {
            if ((variable.Flags & SaveVariableFlag) == 0) continue;
            if (!TryGetElementSize(variable.Type, out int elementSize))
            {
                savedFields.Add(new(variable.Name, variable.Type, variable.Flags,
                    variable.PackedArrayCount, null, null, "UnsupportedType"));
                return new("UnsupportedLayout", null, savedFields.ToArray(),
                    $"Saved variable '{variable.Name}' uses unsupported Iris type 0x{variable.Type:X4}; later save-buffer offsets are unknown.");
            }

            uint elementCount = variable.ArrayLength;
            int dimensions = variable.ArrayDimensions;
            ulong byteLength = (ulong)dimensions * sizeof(uint) + (ulong)elementCount * (uint)elementSize;
            if (byteLength > MaximumSaveBufferBytes || (ulong)bufferOffset + byteLength > MaximumSaveBufferBytes)
            {
                savedFields.Add(new(variable.Name, variable.Type, variable.Flags,
                    variable.PackedArrayCount, null, null, "OutOfBounds"));
                return new("UnsupportedLayout", null, savedFields.ToArray(),
                    $"Saved variable '{variable.Name}' would exceed the {MaximumSaveBufferBytes}-byte safety bound.");
            }

            int fieldLength = checked((int)byteLength);
            savedFields.Add(new(variable.Name, variable.Type, variable.Flags,
                variable.PackedArrayCount, bufferOffset, fieldLength, "Resolved"));
            bufferOffset = checked(bufferOffset + fieldLength);
        }

        return new("Resolved", bufferOffset, savedFields.ToArray(), null);
    }

    /// <summary>Reads a zero-based flag bit from an integer-array field in the selected save buffer.</summary>
    internal static bool? ReadIntegerArrayBit(PcAiGlobalSaveLayout layout,
        ReadOnlySpan<byte> saveBuffer, string variableName, int flagIndex)
    {
        if (layout.Status != "Resolved" || layout.BufferLength is not { } requiredLength
            || saveBuffer.Length < requiredLength || flagIndex < 0)
            return null;

        PcAiSavedFieldLayout[] matches = layout.Fields
            .Where(field => field.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length != 1) return null;

        PcAiSavedFieldLayout field = matches[0];
        if (field.Status != "Resolved" || field.Type is not (0x21 or 0x22)
            || field.BufferOffset is not { } fieldOffset)
            return null;

        uint packedCount = field.PackedArrayCount;
        int dimensions = (int)(packedCount >> 30);
        uint elementCount = packedCount & 0x3FFFFFFF;
        if (dimensions != 1 || saveBuffer.Length - fieldOffset < sizeof(uint)
            || BinaryPrimitives.ReadUInt32LittleEndian(saveBuffer[fieldOffset..]) != elementCount)
            return null;

        ulong bitCapacity = (ulong)elementCount * 32;
        if ((ulong)flagIndex >= bitCapacity) return null;

        int wordIndex = flagIndex / 32;
        int bitInWord = flagIndex % 32;
        ulong wordOffset = (ulong)fieldOffset + (ulong)dimensions * sizeof(uint)
            + (ulong)wordIndex * sizeof(uint);
        if (wordOffset > (ulong)saveBuffer.Length
            || saveBuffer.Length - (int)wordOffset < sizeof(uint))
            return null;
        uint word = BinaryPrimitives.ReadUInt32LittleEndian(saveBuffer[(int)wordOffset..]);
        return (word & (1u << bitInWord)) != 0;
    }

    private static bool TryGetElementSize(ushort type, out int size)
    {
        size = type switch
        {
            0x20 or 0x21 or 0x22 or 0x26 or 0x27 or 0x28 or 0x2A or 0x2B or 0x2D or 0x2E or 0x84 => 4,
            0x25 or >= 0x7C and <= 0x83 or 0x85 => 12,
            0x29 => 52,
            0x2C => 8,
            _ => 0
        };
        return size > 0;
    }
}

internal sealed record PcAiGlobalSaveLayout(string Status, int? BufferLength,
    PcAiSavedFieldLayout[] Fields, string? Diagnostic);

internal sealed record PcAiSavedFieldLayout(string Name, ushort Type, ushort Flags,
    uint PackedArrayCount, int? BufferOffset, int? ByteLength, string Status);
