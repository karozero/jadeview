using System.Buffers.Binary;

internal static class PcProceduralTextureChecks
{
    internal static void Verify()
    {
        byte[] container = new byte[sizeof(uint) + 96];
        int dataOffset = sizeof(uint);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(dataOffset + 6), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(dataOffset + 8), 128);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(dataOffset + 10), 128);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(dataOffset + 42), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(dataOffset + 76), 0x61006334);
        var descriptor = JadeView.PcProceduralTextureReader.Read(container,
            new("header", 0, 96, 0xBF000F07, 5));
        Check(descriptor is { Key: 0xBF000F07, Subtype: 3, Width: 128, Height: 128,
            MediaKey: 0x61006334 },
            "PC movie texture descriptors should resolve their type-5 key, dimensions, subtype and serialized Bink key");

        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(dataOffset + 42), 2);
        var nonMovieDescriptor = JadeView.PcProceduralTextureReader.Read(container,
            new("header", 0, 96, 0xBF000F08, 5));
        Check(nonMovieDescriptor is { Subtype: 2, MediaKey: null },
            "Unknown procedural subtypes should retain their descriptor metadata without guessing a media reference");
        Check(JadeView.PcProceduralTextureReader.Read(container,
                new("header", 0, 43, 0xBF000F07, 5)) == null
            && JadeView.PcProceduralTextureReader.Read(container,
                new("pixels", 0, 96, 0xBF000F07, 5)) == null,
            "Procedural descriptor parsing should ignore truncated and non-header texture reads");
        Console.WriteLine("PASS: PC procedural texture descriptors preserve video resource links without inventing unknown subtype semantics");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
