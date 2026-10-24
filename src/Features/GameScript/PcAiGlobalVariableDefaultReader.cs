using System.Buffers.Binary;
namespace JadeView;

/// <summary>Reads typed default values from the model OVA block after its editor metadata.</summary>
internal static class PcAiGlobalVariableDefaultReader
{
    private const int InitializationCallbackCount = 5;
    private const int MetadataEntrySize = 16;
    private const int MaximumMetadataEntries = 65_536;
    private const int MaximumIntegerValues = 65_536;

    internal static PcAiGlobalVariableDefault[] Read(ReadOnlySpan<byte> payload,
        PcAiGlobalVariableDescriptor[] descriptors)
    {
        int defaultsStart = FindDefaultValuesStart(payload);
        int defaultsSize = ReadInt32(payload, defaultsStart - sizeof(int));
        ReadOnlySpan<byte> defaults = payload.Slice(defaultsStart, defaultsSize);

        // OVA descriptors describe runtime slots. An absent defaults buffer means only that
        // this payload does not serialize their initial bytes; do not infer their live values.
        if (defaults.IsEmpty)
            return descriptors.Select(descriptor => new PcAiGlobalVariableDefault(
                descriptor.Name, descriptor.Offset, descriptor.Type, defaultsStart, 0, null,
                "NoSerializedDefaults")).ToArray();

        var results = new PcAiGlobalVariableDefault[descriptors.Length];
        for (int index = 0; index < descriptors.Length; index++)
            results[index] = ReadDefault(defaultsStart, defaults, descriptors[index]);
        return results;
    }

    internal static uint[] ReadInitializationCallbackKeys(ReadOnlySpan<byte> payload)
    {
        int defaultsStart = FindDefaultValuesStart(payload);
        int defaultsSize = ReadInt32(payload, defaultsStart - sizeof(int));
        int callbackStart = checked(defaultsStart + defaultsSize);
        EnsureRange(payload, callbackStart, InitializationCallbackCount * sizeof(uint),
            "Global OVA callback-key table is truncated.");

        var callbackKeys = new uint[InitializationCallbackCount];
        for (int callbackSlot = 0; callbackSlot < callbackKeys.Length; callbackSlot++)
        {
            callbackKeys[callbackSlot] = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.Slice(callbackStart + callbackSlot * sizeof(uint), sizeof(uint)));
        }
        return callbackKeys;
    }

    private static PcAiGlobalVariableDefault ReadDefault(int defaultsStart, ReadOnlySpan<byte> defaults,
        PcAiGlobalVariableDescriptor descriptor)
    {
        if (descriptor.Type != PcVariableFormat.IntegerType)
            return new(descriptor.Name, descriptor.Offset, descriptor.Type, defaultsStart, defaults.Length, null, "UnsupportedType");
        if (descriptor.Offset < 0)
            return new(descriptor.Name, descriptor.Offset, descriptor.Type, defaultsStart, defaults.Length, null, "InvalidOffset");

        uint packedCount = descriptor.PackedArrayCount;
        int dimensions = (int)(packedCount >> 30);
        uint valueCount = packedCount & 0x3FFFFFFF;
        ulong byteCount = (ulong)dimensions * sizeof(int) + (ulong)valueCount * sizeof(int);
        if (valueCount > MaximumIntegerValues
            || (ulong)descriptor.Offset + byteCount > (ulong)defaults.Length)
            return new(descriptor.Name, descriptor.Offset, descriptor.Type, defaultsStart, defaults.Length, null,
                $"OutOfBounds (needs {byteCount} bytes at {descriptor.Offset})");

        int valuesStart = checked(descriptor.Offset + dimensions * sizeof(int));
        var values = new int[checked((int)valueCount)];
        for (int index = 0; index < values.Length; index++)
            values[index] = BinaryPrimitives.ReadInt32LittleEndian(
                defaults.Slice(valuesStart + index * sizeof(int), sizeof(int)));

        return new(descriptor.Name, descriptor.Offset, descriptor.Type, defaultsStart, defaults.Length, values, "Parsed");
    }

    private static int FindDefaultValuesStart(ReadOnlySpan<byte> payload)
    {
        int descriptorBytes = ReadInt32(payload, 0);
        if (descriptorBytes < 0 || descriptorBytes % PcVariableFormat.DescriptorSize != 0
            || descriptorBytes > payload.Length - sizeof(int))
            throw new InvalidDataException("Global OVA descriptor block is invalid.");

        int namesLengthOffset = checked(sizeof(int) + descriptorBytes);
        int namesLength = ReadInt32(payload, namesLengthOffset);
        int descriptorCount = descriptorBytes / PcVariableFormat.DescriptorSize;
        if (namesLength != checked(descriptorCount * PcVariableFormat.NameSize))
            throw new InvalidDataException("Global OVA name table is invalid.");

        int metadataHeader = checked(namesLengthOffset + sizeof(int) + namesLength);
        int metadataBytes = ReadInt32(payload, metadataHeader);
        _ = ReadInt32(payload, checked(metadataHeader + sizeof(int)));
        if (metadataBytes < 0 || metadataBytes % MetadataEntrySize != 0
            || metadataBytes / MetadataEntrySize > MaximumMetadataEntries)
            throw new InvalidDataException("Global OVA editor metadata block is invalid.");

        int metadataStart = checked(metadataHeader + sizeof(int) * 2);
        EnsureRange(payload, metadataStart, metadataBytes, "Global OVA editor metadata is truncated.");
        int metadataCount = metadataBytes / MetadataEntrySize;
        int[] stringFlags = new int[metadataCount * 2];
        for (int index = 0; index < metadataCount; index++)
        {
            int entryOffset = metadataStart + index * MetadataEntrySize;
            stringFlags[index * 2] = ReadInt32(payload, entryOffset + sizeof(int));
            stringFlags[index * 2 + 1] = ReadInt32(payload, entryOffset + sizeof(int) * 2);
        }

        int cursor = checked(metadataStart + metadataBytes);
        foreach (int stringFlag in stringFlags)
        {
            if (stringFlag == 0) continue;
            if ((uint)cursor >= (uint)payload.Length)
                throw new InvalidDataException("Global OVA metadata string is missing.");
            int terminator = payload[cursor..].IndexOf((byte)0);
            if (terminator < 0)
                throw new InvalidDataException("Global OVA metadata string is unterminated.");
            cursor = checked(cursor + terminator + 1);
        }

        int defaultsSize = ReadInt32(payload, cursor);
        if (defaultsSize < 0)
            throw new InvalidDataException("Global OVA default-value block has a negative size.");
        int defaultsStart = checked(cursor + sizeof(int));
        EnsureRange(payload, defaultsStart, defaultsSize,
            "Global OVA default-value block is truncated.");
        return defaultsStart;
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, int offset)
    {
        if (offset < 0 || offset > payload.Length - sizeof(int))
            throw new InvalidDataException("Global OVA layout is truncated.");
        return BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
    }

    private static void EnsureRange(ReadOnlySpan<byte> payload, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset > payload.Length - length)
            throw new InvalidDataException(message);
    }
}
