using System.Buffers.Binary;
using JadeView;

internal static class PcScriptCallbackSlotResolverChecks
{
    internal static void Verify()
    {
        const uint modelWorld = 0x100;
        var model = new PcSceneLoader.Record(1, 's', 0, 0)
        {
            LoadWorldKey = modelWorld,
            References = [new(2, 't'), new(3, 't'), new(2, 't')]
        };
        byte[] callbacks = Payload(10, 0, 30, 0, 50);
        var first = Record(2, 't', modelWorld, callbacks);
        var second = Record(3, 't', modelWorld, callbacks);
        var shadow = Record(2, 't', 0x200, Payload(99, 0, 0, 0, 0));
        var origins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [first] = callbacks, [second] = callbacks, [shadow] = Payload(99, 0, 0, 0, 0)
        };
        var names = new Dictionary<uint, string> { [10] = "init" };
        CinematicScriptCallbackSlot[]? Resolve(params PcSceneLoader.Record[] records) =>
            PcScriptCallbackSlotResolver.Resolve(model, new PcSceneResourceIndex(records), origins, names);

        var agreed = Resolve(first, second, shadow);
        Require(agreed is { Length: 5 } && agreed[0].FunctionName == "init"
            && agreed.Select(slot => slot.FunctionKey).SequenceEqual(new uint[] { 10, 0, 30, 0, 50 }),
            "Agreeing model-scoped callback tables lost order, gaps, names or repeated-reference deduplication.");

        origins[second] = Payload(0, 0, 0, 0, 0);
        Require(Resolve(first, second) == null,
            "A nonzero callback table was silently preferred over a conflicting zero table.");
        origins[first] = Payload(0, 0, 0, 0, 0);
        Require(Resolve(first, second)?.All(slot => slot.FunctionKey == 0) == true,
            "Two agreeing empty callback tables should remain known empty slots.");
        origins[first] = callbacks;
        origins[second] = Payload(0, 10, 30, 0, 50);
        Require(Resolve(first, second) == null, "Callback table position conflicts were ignored.");
        Require(Resolve(first) == null, "A missing referenced OVA was ignored.");
        var wrongKind = Record(3, 'r', modelWorld, callbacks);
        origins[wrongKind] = callbacks;
        Require(Resolve(first, wrongKind) == null, "A non-OVA resource was parsed as callbacks.");

        var ambiguousA = Record(3, 't', 0x200, callbacks);
        var ambiguousB = Record(3, 't', 0x300, callbacks);
        origins[ambiguousA] = callbacks;
        origins[ambiguousB] = callbacks;
        Require(Resolve(first, ambiguousA, ambiguousB) == null, "Ambiguous OVA resolution was accepted.");

        foreach (var invalid in new[]
        {
            new PcSceneLoader.Record(3, 't', -1, callbacks.Length),
            new PcSceneLoader.Record(3, 't', 0, -1),
            new PcSceneLoader.Record(3, 't', 1, callbacks.Length),
            new PcSceneLoader.Record(3, 't', int.MaxValue, 1)
        })
        {
            invalid.LoadWorldKey = modelWorld;
            origins[invalid] = callbacks;
            Require(Resolve(first, invalid) == null, "An out-of-bounds OVA payload was accepted.");
        }
        origins[second] = new byte[callbacks.Length];
        origins[second][0] = 1;
        Require(Resolve(first, second) == null, "A malformed OVA was ignored.");
        origins.Remove(second);
        Require(Resolve(first, second) == null, "An unavailable OVA origin was ignored.");
        model.References = [];
        Require(Resolve(first) == null, "A model without explicit OVA references acquired callback slots.");
        Console.WriteLine("PASS: callback slot resolution rejects conflicting or incomplete model-scoped evidence");
    }

    private static PcSceneLoader.Record Record(uint key, char kind, uint world, byte[] bytes) =>
        new(key, kind, 0, bytes.Length) { LoadWorldKey = world };

    private static byte[] Payload(params uint[] keys)
    {
        byte[] bytes = new byte[12 + 5 * sizeof(uint)];
        for (int index = 0; index < keys.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12 + index * sizeof(uint)), keys[index]);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
