using System.Buffers.Binary;
using JadeView;

internal static class PcWorldListHeaderChecks
{
    internal static void Verify()
    {
        byte[] payload = BuildWorldList();
        if (!PcWorldListHeaderReader.TryRead(payload, 0xAABBCCDD,
                out GameWorldListDescriptor? descriptor, out int consumedBytes, out string error)
            || descriptor == null)
            throw new InvalidDataException($"A valid PC WOL header was rejected: {error}");

        if (descriptor.OuterListKey != 0xAABBCCDD
            || !descriptor.OrderedWorldRootKeys.SequenceEqual([0x11223344u, 0u, 0xFFFFFFFFu])
            || consumedBytes != 28)
            throw new InvalidDataException("PC WOL parsing changed root order, placeholders, identity, or consumed size.");

        byte[] truncated = payload[..27];
        if (PcWorldListHeaderReader.TryRead(truncated, null, out _, out _, out _))
            throw new InvalidDataException("A truncated PC WOL root list must be rejected.");

        byte[] invalidTag = BuildWorldList();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidTag.AsSpan(8), 0x12345678);
        if (PcWorldListHeaderReader.TryRead(invalidTag, null, out _, out _, out _))
            throw new InvalidDataException("A nonzero WOL root with a non-.wow tag must be rejected.");

        byte[] invalidLength = BuildWorldList();
        BinaryPrimitives.WriteInt32LittleEndian(invalidLength, 15);
        if (PcWorldListHeaderReader.TryRead(invalidLength, null, out _, out _, out _))
            throw new InvalidDataException("A misaligned PC WOL header length must be rejected.");

        Console.WriteLine("PASS: PC WOL header bounds, ordered roots, placeholders and archive identity");
    }

    private static byte[] BuildWorldList()
    {
        byte[] payload = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 24);
        WriteEntry(payload, 4, 0x11223344, 0x776F772E);
        WriteEntry(payload, 12, 0, 0x12345678);
        WriteEntry(payload, 20, uint.MaxValue, 0x776F772E);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(28), 0xDEADBEEF);
        return payload;
    }

    private static void WriteEntry(byte[] payload, int offset, uint key, uint tag)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), key);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + sizeof(uint)), tag);
    }
}
