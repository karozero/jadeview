using JadeView;

internal static class MorphReaderChecks
{
    internal static void Verify()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4804u); writer.Write(0u); writer.Write(3u);
            writer.Write(2); writer.Write(1);
            writer.Write(0); // Empty target slot must survive.
            writer.Write(1); writer.Write(2);
            writer.Write(.125f); writer.Write(-.25f); writer.Write(.5f);
            writer.Write(3); writer.Write(0x80000000u); writer.Write(0x3F000000u);
            writer.Write(1u); writer.Write(uint.MaxValue); writer.Write(1u);
        }
        var bytes = stream.ToArray();
        var morph = FaceMorphReader.Read(bytes, 3);
        Check(morph.Groups.Length == 2 && morph.Groups[0].Deltas.Length == 0, "Empty morph group was lost");
        Check(morph.Groups[1].Deltas[0].Y == -.25f, "Sparse morph delta was decoded incorrectly");
        Check(morph.Channels[0].WeightBits == 0x80000000u && morph.Channels[0].GroupReferences.SequenceEqual(new uint[] { 1, uint.MaxValue, 1 }),
            "Channel words, holes or duplicate references were lost");
        Reject(() => FaceMorphReader.Read(bytes, 4));
        for (int length = 0; length < bytes.Length; length++)
            Reject(() => FaceMorphReader.Read(bytes[..length], 3));
        var invalidVertex = (byte[])bytes.Clone();
        BitConverter.GetBytes(3).CopyTo(invalidVertex, 28);
        Reject(() => FaceMorphReader.Read(invalidVertex, 3));
        var invalidDelta = (byte[])bytes.Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(invalidDelta, 32);
        Reject(() => FaceMorphReader.Read(invalidDelta, 3));
        var unrelated = new FaceMorphData([], Enumerable.Range(0, 38).Select(index => new FaceMorphGroup(index, [], [])).ToArray(), []);
        Check(FaceMorphDiagnostics.SourceIssues(new Geometry { Key = 0x6200318D }, unrelated).Length == 0,
            "Target index alone was incorrectly treated as a known source issue");
        Console.WriteLine("PASS: PC morph decoding, empty slots, channel words/holes, mesh mismatch and malformed payload rejection");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Malformed or mismatched morph data was accepted");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
