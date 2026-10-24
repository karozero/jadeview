using System.Buffers.Binary;

namespace JadeView;

internal static class PcWorldLightingReader
{
    // PC /BIN WOW v3 omits the editor-only word before the 68-byte matrix.
    // iristool BFWOWChunk: ambient 1 before name; ambient 2 after matrix/FOV/clear color.
    internal static WorldLighting? Read(ReadOnlySpan<byte> payload, uint worldKey = 0)
    {
        if (payload.Length < 156 || !payload[..4].SequenceEqual(".wow"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]) != 3)
            return null;
        return new(BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[152..])) { SourceWorldKey = worldKey };
    }
}
