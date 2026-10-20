using System.Numerics;
using JadeView;

internal static class AnimationPackedRotationChecks
{
    internal static void Verify()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((short)0);
        writer.Write(0u); writer.Write(0u); writer.Write(4u);
        Header(writer, 0x80);
        writer.Write((short)1000); writer.Write((short)2000); writer.Write((short)3000);
        Delta(writer, 0x100, 300, -12, 8);
        Delta(writer, 0x200, -450, 10, -20);
        Delta(writer, 0x400, 550, -8, 6);
        byte[] bytes = stream.ToArray();
        var clip = AnimationReader.Read(bytes, 0, bytes.Length);
        Vector3[] expected = [new(1000, 2000, 3000), new(1300, 1988, 3008),
            new(1310, 1538, 2988), new(1302, 1544, 3538)];
        for (int index = 0; index < expected.Length; index++)
        {
            var rotation = clip.Tracks[0].Keys[index].Rotation
                ?? throw new InvalidDataException("Packed bone rotation was skipped.");
            var actual = new Vector3(rotation.X, rotation.Y, rotation.Z) * 32767;
            if (Vector3.Distance(actual, expected[index]) > .01f || Math.Abs(rotation.Length() - 1) > 1e-6f)
                throw new InvalidDataException($"Packed rotation {index}: {actual}, expected {expected[index]}.");
        }
        if (clip.Unsupported != 0 || clip.Tracks[0].Keys.Skip(1).Any(key => key.OpaquePayloads.Count != 1))
            throw new InvalidDataException("Decoded packed rotations must retain their source bytes.");
        try { AnimationReader.Read(bytes[..^1], 0, bytes.Length - 1); }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: chained compressed bone rotations, all wide axes, signed deltas, source bytes and truncation rejection");
            return;
        }
        throw new InvalidDataException("Truncated packed rotation was accepted.");
    }

    private static void Header(BinaryWriter writer, ushort format)
    {
        writer.Write((ushort)1); writer.Write((ushort)0x80);
        writer.Write((ushort)20); writer.Write(format);
    }

    private static void Delta(BinaryWriter writer, ushort format, short wide, sbyte first, sbyte second)
    {
        Header(writer, format);
        writer.Write(wide); writer.Write(first); writer.Write(second);
    }
}
