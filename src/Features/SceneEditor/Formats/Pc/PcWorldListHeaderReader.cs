using System.Buffers.Binary;

namespace JadeView;

/// <summary>Reads the ordered root-key header of a PC WOL resource without loading its worlds.</summary>
internal static class PcWorldListHeaderReader
{
    private const int HeaderLengthSize = sizeof(int);
    private const int EntrySize = sizeof(uint) * 2;
    private const uint WowTag = 0x776F772E;

    internal static bool TryRead(ReadOnlySpan<byte> payload, uint? outerListKey,
        out GameWorldListDescriptor? descriptor, out int consumedBytes, out string error)
    {
        descriptor = null;
        consumedBytes = 0;
        error = string.Empty;
        if (payload.Length < HeaderLengthSize)
            return Fail("Not a PC WOL stream: missing header length.", out error);

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(payload);
        bool validHeaderSize = headerSize >= EntrySize
            && headerSize % EntrySize == 0
            && headerSize <= payload.Length - HeaderLengthSize;
        if (!validHeaderSize)
            return Fail("Not a PC WOL stream: invalid or truncated root list.", out error);

        int rootCount = headerSize / EntrySize;
        var roots = new uint[rootCount];
        for (int rootIndex = 0; rootIndex < rootCount; rootIndex++)
        {
            int entryOffset = HeaderLengthSize + rootIndex * EntrySize;
            uint rootKey = BinaryPrimitives.ReadUInt32LittleEndian(payload[entryOffset..]);
            uint entryTag = BinaryPrimitives.ReadUInt32LittleEndian(payload[(entryOffset + sizeof(uint))..]);
            bool validWorldEntry = entryTag == WowTag || rootKey == 0;
            if (!validWorldEntry)
                return Fail($"Not a PC WOL list: root entry {rootIndex} has tag {entryTag:X8}.", out error);
            roots[rootIndex] = rootKey;
        }

        descriptor = new(outerListKey, roots);
        consumedBytes = HeaderLengthSize + headerSize;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
