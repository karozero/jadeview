using System.Reflection;
using System.Text;
using JadeView;

internal static class PcScalarVariableChecks
{
    private delegate int ReadOverlay(ReadOnlySpan<byte> model, ReadOnlySpan<byte> instance, string name, ushort type);

    internal static void Verify()
    {
        var readerType = typeof(SceneActor).Assembly.GetType("JadeView.PcScalarVariableReader")!;
        var read = readerType.GetMethod("ReadOverlay", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<ReadOverlay>();
        byte[] model = Payload([("other", 4, 33), ("des_i_friend", 0, 33)], [7, 99]);
        byte[] empty = Payload([], []);
        Check(read(model, empty, "des_i_friend", 33) == 7, "OVA scalar used descriptor order instead of storage offset");
        byte[] instance = Payload([("DES_I_FRIEND", 4, 33), ("des_i_friend", 0, 33)], [5, 1]);
        Check(read(model, instance, "des_i_friend", 33) == 1, "First case-insensitive scalar override did not win");
        Reject(read, model, Payload([("des_i_friend", 0, 40)], [1]));
        Reject(read, model, instance[..^1]);
        byte[] unsupported = (byte[])instance.Clone();
        BitConverter.GetBytes(2).CopyTo(unsupported, 8);
        Reject(read, model, unsupported);
        Console.WriteLine("PASS: bounded OVA scalar offsets, first-match override, incompatible layout and truncated storage rejection");
    }

    private static byte[] Payload((string Name, int Offset, ushort Type)[] fields, int[] storage)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(fields.Length * 12);
        foreach (var field in fields)
        {
            writer.Write(field.Offset); writer.Write(1); writer.Write(field.Type); writer.Write((ushort)0);
        }
        writer.Write(fields.Length * 30);
        foreach (var field in fields)
        {
            byte[] name = new byte[30];
            Encoding.Latin1.GetBytes(field.Name).CopyTo(name, 0);
            writer.Write(name);
        }
        writer.Write(storage.Length * 4);
        foreach (int value in storage) writer.Write(value);
        return stream.ToArray();
    }

    private static void Reject(ReadOverlay read, byte[] model, byte[] instance)
    {
        try { read(model, instance, "des_i_friend", 33); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Unsupported controller scalar was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
