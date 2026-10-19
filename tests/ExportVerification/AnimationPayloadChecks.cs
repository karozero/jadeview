using JadeView;

internal static class AnimationPayloadChecks
{
    internal static void Verify()
    {
        byte[] source = CreateFixture();
        var clip = AnimationReader.Read(source, 7, source.Length - 7);
        var track = clip.Tracks.Single();
        var key = track.Keys.Single();
        var payload = key.OpaquePayloads.Single();
        Require(track.HeaderWord == 0x128 && track.ChannelFlags == 0x4013,
            "Original event channel header must survive decoding");
        Require(payload.Kind == "word-pair-list" && payload.SourceOffset == 24,
            "Payload offset must be relative to the clip, not the source array");
        Require(payload.Data.SequenceEqual(source.AsSpan(31).ToArray()),
            "Event count and every raw operand/tag word must survive decoding");
        Require(key.Time == 0 && key.Duration == .5f && clip.Duration == .5f,
            "Retaining events must preserve the animation clock");
        Require(clip.Unsupported == 1, "Opaque events must remain explicitly unsupported");
        byte savedByte = payload.Data[0];
        source[31] = 0;
        Require(payload.Data[0] == savedByte, "Retained bytes must own their storage");

        Reject(CreateFixture()[..^1]);
        byte[] invalidCount = CreateFixture();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(invalidCount, 31);
        Reject(invalidCount);
        Console.WriteLine("PASS: opaque animation event bytes, headers, offsets, timing and malformed payload rejection");
    }

    private static byte[] CreateFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[7]);
        writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)0x4013); writer.Write((short)-1);
        writer.Write(0x128u); writer.Write(0u); writer.Write(1u);
        writer.Write((ushort)30); writer.Write((ushort)0x41);
        writer.Write(2u);
        writer.Write(0x35u); writer.Write(0x01000021u);
        writer.Write(0x3F800000u); writer.Write(0x01000022u);
        return stream.ToArray();
    }

    private static void Reject(byte[] source)
    {
        try { AnimationReader.Read(source, 7, source.Length - 7); }
        catch (Exception error) when (error is InvalidDataException or OverflowException) { return; }
        throw new InvalidOperationException("Malformed animation event was accepted");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
