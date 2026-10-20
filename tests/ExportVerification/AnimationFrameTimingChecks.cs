using JadeView;

internal static class AnimationFrameTimingChecks
{
    internal static void Verify()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((short)-1);
        writer.Write(0u); writer.Write(7u); writer.Write(1000u);
        for (int index = 0; index < 1000; index++)
        {
            writer.Write((ushort)6); writer.Write((ushort)0);
        }
        byte[] bytes = stream.ToArray();
        var clip = AnimationReader.Read(bytes, 0, bytes.Length);
        for (int index = 0; index < 1000; index++)
            if (clip.Tracks[0].Keys[index].Time != index * 6 / 60f)
                throw new InvalidDataException("Cumulative float drift moved an event across its source frame.");
        if (clip.Duration != 100) throw new InvalidDataException("Clip duration drifted from its source frame count.");
        Console.WriteLine("PASS: source frame boundaries and clip duration stay aligned across long event tracks");
    }
}
