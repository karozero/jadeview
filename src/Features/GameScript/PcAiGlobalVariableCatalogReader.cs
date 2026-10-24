using System.Buffers.Binary;
using System.Text;

namespace JadeView;

/// <summary>Reads global-AI variable names and offsets using Iris's serialized AIVarElem layout.</summary>
internal static class PcAiGlobalVariableCatalogReader
{
    private const int DescriptorSize = 12;
    private const int NameSize = 30;
    private const int MaximumDescriptorBytes = 16 * 1024 * 1024;

    internal static bool TryRead(ReadOnlySpan<byte> payload,
        out PcAiGlobalVariableDescriptor[] variables, out string error)
    {
        variables = [];
        error = string.Empty;
        if (!TryReadInt32(payload, 0, out int descriptorBytes)
            || descriptorBytes < 0 || descriptorBytes > MaximumDescriptorBytes
            || descriptorBytes % DescriptorSize != 0)
            return Fail("Global AI variable descriptor block has an invalid length.", out error);

        int descriptorCount = descriptorBytes / DescriptorSize;
        int namesHeaderOffset = sizeof(int) + descriptorBytes;
        if (!TryReadInt32(payload, namesHeaderOffset, out int namesBytes)
            || namesBytes != (long)descriptorCount * NameSize
            || namesHeaderOffset > payload.Length - sizeof(int) - namesBytes)
            return Fail("Global AI variable-name block is invalid or truncated.", out error);

        int namesOffset = namesHeaderOffset + sizeof(int);
        variables = new PcAiGlobalVariableDescriptor[descriptorCount];
        for (int index = 0; index < descriptorCount; index++)
        {
            int descriptorOffset = sizeof(int) + index * DescriptorSize;
            int offset = BinaryPrimitives.ReadInt32LittleEndian(payload[descriptorOffset..]);
            uint packedArrayCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[(descriptorOffset + sizeof(int))..]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(payload[(descriptorOffset + sizeof(int) * 2)..]);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(payload[(descriptorOffset + sizeof(int) * 2 + sizeof(ushort))..]);
            ReadOnlySpan<byte> encodedName = payload.Slice(namesOffset + index * NameSize, NameSize);
            int terminator = encodedName.IndexOf((byte)0);
            if (terminator >= 0) encodedName = encodedName[..terminator];
            variables[index] = new(offset, packedArrayCount, type, flags,
                Encoding.Latin1.GetString(encodedName).Trim());
        }

        // Defaults and optional variable-metadata blocks follow these source-backed tables.
        // Keep them opaque here: names/offsets are sufficient to annotate OFC global accesses.
        return true;
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> bytes, int offset, out int value)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(int))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
