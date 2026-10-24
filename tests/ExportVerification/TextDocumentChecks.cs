using System.Text;
using JadeView;

internal static class TextDocumentChecks
{
    internal static void Verify()
    {
        byte[] fixture = BuildFixture();
        TextDocument document = TextDocument.Read(fixture);
        TextDocument.TextGroup group = document.Batches.Single().Single();

        Require(group.StringOffsets.SequenceEqual([0, 3]),
            "PC TXI string offsets should be retained in ordinal order.");
        Require(group.Strings.SequenceEqual(["Hi", "Bye"]),
            "TXI offsets should address the corresponding TXS strings.");
        Require(document.Write().SequenceEqual(fixture),
            "Read/write should preserve a valid TXI/TXS text fixture.");

        Console.WriteLine("PASS: PC text documents retain TXI string-pool offsets and round-trip localized strings");
    }

    private static byte[] BuildFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(8);
        writer.Write(0x3500A1C9u);
        writer.Write(0x6778742Eu);

        writer.Write(16);
        writer.Write(0xC10093D4u);
        writer.Write(Encoding.ASCII.GetBytes(".txi"));
        writer.Write(0x35008DC1u);
        writer.Write(Encoding.ASCII.GetBytes(".txs"));
        writer.Write(0u);

        writer.Write(8);
        writer.Write(0);
        writer.Write(3);
        byte[] pool = Encoding.ASCII.GetBytes("Hi\0Bye\0");
        writer.Write(pool.Length);
        writer.Write(pool);
        return stream.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
