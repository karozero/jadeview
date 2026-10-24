using System.Buffers.Binary;
using JadeView;

internal static class ScriptOwnerResourceFixtureChecks
{
    internal static void Verify()
    {
        var owner = new PcSceneLoader.Record(1, 'l', 0, 4) { LoadWorldKey = 10, BrainFieldOffset = 0 };
        var instance = new PcSceneLoader.Record(2, 'q', 0, 8) { LoadWorldKey = 10 };
        var model = new PcSceneLoader.Record(3, 's', 0, 24)
        {
            LoadWorldKey = 10, References = [new(4, 't'), new(5, 'r'), new(4, 't')]
        };
        var variables = new PcSceneLoader.Record(4, 't', 0, 32) { LoadWorldKey = 10 };
        var function = new PcSceneLoader.Record(5, 'r', 0, 12) { LoadWorldKey = 10 };
        byte[] modelBytes = new byte[24];
        WriteEntry(0, 4, ".ova");
        WriteEntry(8, 5, ".ofc");
        WriteEntry(16, 4, ".ova");
        byte[] variableBytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(variableBytes.AsSpan(12), 5);
        var origins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [owner] = BitConverter.GetBytes(2u), [instance] = [3, 0, 0, 0, 4, 0, 0, 0],
            [model] = modelBytes, [variables] = variableBytes, [function] = new byte[12]
        };
        var records = new[] { owner, instance, model, variables, function };
        var fixture = ScriptOwnerResourceFixture.Build(records, origins, 1);
        Require(fixture.Diagnostics.Count == 0 && fixture.ExpectedCallbackKeys?[0] == "00000005"
            && fixture.Links.Where(link => link.EntryIndex != null).Select(link => link.Key)
                .SequenceEqual(new[] { "00000004", "00000005", "00000004" })
            && fixture.Resources.All(resource => resource.Status == "Resolved" && resource.Sha256?.Length == 64),
            "Owner resource fixture lost ordered repeated entries, callback slots, scoped data or hashes.");

        function.Offset = int.MaxValue;
        Require(ScriptOwnerResourceFixture.Build(records, origins, 1).Resources
                .Any(resource => resource.Key == "00000005" && resource.Status == "InvalidPayloadRange"
                    && resource.Payload == null),
            "Out-of-bounds function bytes were exported.");
        function.Offset = 0;
        var duplicate = new PcSceneLoader.Record(5, 'r', 0, 12) { LoadWorldKey = 10 };
        origins[duplicate] = new byte[12];
        Require(ScriptOwnerResourceFixture.Build([.. records, duplicate], origins, 1).Resources
                .Any(resource => resource.Key == "00000005" && resource.Status == "Unresolved"),
            "Ambiguous same-world functions were silently selected.");
        owner.BrainFieldOffset = 4;
        Require(ScriptOwnerResourceFixture.Build(records, origins, 1).Diagnostics.Count == 1,
            "A brain key outside the owner record was read from adjacent bytes.");
        Console.WriteLine("PASS: script-owner evidence fixtures preserve source order and reject ambiguous or unbounded slices");

        void WriteEntry(int offset, uint key, string tag)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(modelBytes.AsSpan(offset), key);
            System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(modelBytes, offset + 4);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
