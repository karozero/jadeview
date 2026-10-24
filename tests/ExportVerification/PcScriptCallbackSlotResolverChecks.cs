using System.Buffers.Binary;
using JadeView;

internal static class PcScriptCallbackSlotResolverChecks
{
    internal static void Verify()
    {
        VerifyEffectiveVariableResources();
        const uint modelWorld = 0x100;
        var model = new PcSceneLoader.Record(1, 's', 0, 0)
        {
            LoadWorldKey = modelWorld,
            References = [new(2, 't'), new(3, 't')]
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
            "Model-scoped callback tables lost order, gaps or names.");

        origins[second] = Payload(0, 0, 0, 0, 0);
        Require(Resolve(first, second)?.All(slot => slot.FunctionKey == 0) == true,
            "A final empty callback table failed to replace the earlier nonzero table.");
        origins[first] = Payload(0, 0, 0, 0, 0);
        Require(Resolve(first, second)?.All(slot => slot.FunctionKey == 0) == true,
            "Two agreeing empty callback tables should remain known empty slots.");
        origins[first] = callbacks;
        origins[second] = Payload(0, 10, 30, 0, 50);
        Require(Resolve(first, second)?.Select(slot => slot.FunctionKey)
                .SequenceEqual(new uint[] { 0, 10, 30, 0, 50 }) == true,
            "The final source-order OVA assignment was not selected.");
        model.References = [new(3, 't'), new(2, 't')];
        Require(Resolve(first, second)?.Select(slot => slot.FunctionKey)
                .SequenceEqual(new uint[] { 10, 0, 30, 0, 50 }) == true,
            "Reversing OVA source order failed to reverse the effective assignment.");
        model.References = [new(2, 't'), new(3, 't'), new(2, 't')];
        Require(Resolve(first, second)?.Select(slot => slot.FunctionKey)
                .SequenceEqual(new uint[] { 10, 0, 30, 0, 50 }) == true,
            "A repeated final OVA reference was deduplicated out of source order.");
        model.References = [new(2, 't'), new(3, 't')];
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
        Console.WriteLine("PASS: callback slots follow ordered model OVA replacement and reject incomplete evidence");
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

    private static void VerifyEffectiveVariableResources()
    {
        const uint modelWorld = 0x100;
        const uint ownerWorld = 0x200;
        byte[] oldBytes = ScalarObject("removed_model_field", 11);
        byte[] finalBytes = ScalarObject("current_field", 22);
        byte[] instanceBytes = ScalarObject("current_field", 33);
        var model = new PcSceneLoader.Record(1, 's', 0, 0)
        {
            LoadWorldKey = modelWorld, References = [new(2, 't'), new(3, 't')]
        };
        var oldModel = Record(2, 't', modelWorld, oldBytes);
        var finalModel = Record(3, 't', modelWorld, finalBytes);
        var instance = Record(3, 't', ownerWorld, instanceBytes);
        var origins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [oldModel] = oldBytes, [finalModel] = finalBytes, [instance] = instanceBytes
        };
        var resources = new PcSceneResourceIndex([oldModel, finalModel, instance]);
        ReadOnlyMemory<byte> resolvedModel = PcScriptModelVariableResourceResolver
            .ResolveModel(model, resources, origins)!.Value;
        ReadOnlyMemory<byte> resolvedInstance = PcScriptModelVariableResourceResolver
            .ResolveInstance(3, ownerWorld, resources, origins)!.Value;
        var modelVariables = PcVariableSnapshotReader.Read(resolvedModel.Span, "model");
        var instanceVariables = PcVariableSnapshotReader.Read(resolvedInstance.Span, "instance");
        Require(modelVariables.Length == 1 && modelVariables[0].Name == "current_field"
            && !modelVariables.Any(variable => variable.Name == "removed_model_field")
            && modelVariables[0].Value != instanceVariables[0].Value,
            "Replaced model fields survived or model/instance world scopes were conflated.");
        var references = PcObjectReferenceListReader.ReadOverlayBlocks([resolvedModel], resolvedInstance.Span);
        Require(references.Length == 1 && references[0].VariableName == "current_field"
            && references[0].TargetKey == 33 && references[0].Source == "instance",
            "Effective model replacement changed the existing model/instance reference overlay.");

        byte[] truncatedTail = oldBytes[..^sizeof(uint)];
        var truncatedModel = Record(2, 't', modelWorld, truncatedTail);
        origins[truncatedModel] = truncatedTail;
        var truncatedResources = new PcSceneResourceIndex([truncatedModel, finalModel, instance]);
        Require(PcScriptModelVariableResourceResolver.ResolveModel(model, truncatedResources, origins) == null,
            "A later valid OVA erased an earlier model OVA's truncated required callback table.");
        Require(PcScriptModelVariableResourceResolver.ResolveInstance(2, modelWorld, truncatedResources, origins) != null,
            "Model-only callback-tail validation changed instance variable-storage semantics.");
        origins[finalModel] = [0];
        Require(PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins) == null
            && PcScriptModelVariableResourceResolver.ResolveInstance(3, ownerWorld, resources, origins) != null,
            "An invalid final model OVA resurrected earlier defaults or erased valid instance evidence.");
    }

    private static byte[] ScalarObject(string name, uint key)
    {
        byte[] bytes = new byte[12 + 12 + 30 + sizeof(uint) + 5 * sizeof(uint)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), PcVariableFormat.ObjectReferenceType);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 30);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 20);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(50), sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(54), key);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
