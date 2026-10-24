using JadeView;

internal static class PcCollisionResourceCatalogChecks
{
    internal static void Verify()
    {
        VerifyWorldScopedSetResolution();
        VerifyDuplicateIdsAcrossWorlds();
        VerifyMissingAmbiguousAndMalformedResources();
        Console.WriteLine("PASS: read-only world-scoped PC collision resource catalog");
    }

    private static void VerifyDuplicateIdsAcrossWorlds()
    {
        const uint firstWorldKey = 0x1101;
        const uint secondWorldKey = 0x2201;
        const uint objectKey = 0x1003;
        const uint instanceKey = 0xF003;
        const uint setKey = 0xA100;
        byte[] instanceBytes = [0x00, 0xA1, 0x00, 0x00];
        byte[] setBytes = new byte[18];
        var firstObject = new SceneObject
        {
            Id = "same-object-id", GaoKey = objectKey, Name = "first world",
            CollisionInstanceKey = instanceKey, CollisionMapKey = uint.MaxValue
        };
        var secondObject = new SceneObject
        {
            Id = "same-object-id", GaoKey = objectKey, Name = "second world",
            CollisionInstanceKey = instanceKey, CollisionMapKey = uint.MaxValue
        };
        var firstWorld = new WorldData { Key = firstWorldKey, Name = "first" };
        var secondWorld = new WorldData { Key = secondWorldKey, Name = "second" };
        firstWorld.Objects.Add(firstObject);
        secondWorld.Objects.Add(secondObject);
        var document = new SceneDocument();
        document.Worlds.Add(firstWorld);
        document.Worlds.Add(secondWorld);
        var records = new[]
        {
            Record(objectKey, 'l', firstWorldKey, []),
            Record(objectKey, 'l', secondWorldKey, []),
            Record(instanceKey, 'd', firstWorldKey, instanceBytes),
            Record(instanceKey, 'd', secondWorldKey, instanceBytes),
            Record(setKey, 'e', firstWorldKey, setBytes),
            Record(setKey, 'e', secondWorldKey, setBytes)
        };
        var origins = records.ToDictionary(record => record, record => record.Size == 0
            ? Array.Empty<byte>() : record.Kind is 'd' ? instanceBytes : setBytes);

        var catalog = PcCollisionResourceCatalogBuilder.Build(records, origins, document);
        Require(catalog.TryGet(firstObject, out var first)
            && catalog.TryGet(secondObject, out var second)
            && first.SourceWorldKey == firstWorldKey
            && second.SourceWorldKey == secondWorldKey
            && first.Set?.SourceWorldKey == firstWorldKey
            && second.Set?.SourceWorldKey == secondWorldKey,
            "Objects with a reused Id/key in separate worlds must retain independent world-scoped collision resources.");
    }

    private static void VerifyWorldScopedSetResolution()
    {
        const uint worldKey = 0x1100;
        const uint objectKey = 0x1001;
        const uint instanceKey = 0xF001;
        const uint setKey = 0x6C00684C;
        const uint mapKey = 0xD001;
        var objectRecord = new PcSceneLoader.Record(objectKey, 'l', 0, 0) { LoadWorldKey = worldKey };
        byte[] instanceBytes = Convert.FromHexString(
            "4C68006C060402000101020304006402FF001100000000000000000000000000003FCDCC4C3F" +
            "61021A051100000000000000000000006666E63E6666E63EFFFF21009A5804000201");
        var instanceRecord = Record(instanceKey, 'd', worldKey, instanceBytes);
        byte[] setBytes = Convert.FromHexString(
            "0100FFFFFFFFFFFF00FFFFFFFFFFFFFFFFFF0402C6000D0000000000000000000000000000000000803F");
        var localSet = Record(setKey, 'e', worldKey, setBytes);
        var otherWorldSet = Record(setKey, 'e', 0x2200, [1, 0]);
        var mapRecord = Record(mapKey, 'f', worldKey, [0x44, 0x33, 0x22, 0x11]);
        SceneDocument document = Document(worldKey, objectKey, instanceKey, mapKey);

        var catalog = PcCollisionResourceCatalogBuilder.Build(
            [objectRecord, instanceRecord, localSet, otherWorldSet, mapRecord],
            Origins(objectRecord, Array.Empty<byte>(), instanceRecord, instanceBytes, localSet, setBytes,
                otherWorldSet, new byte[] { 1, 0 }, mapRecord, new byte[] { 0x44, 0x33, 0x22, 0x11 }),
            document);

        Require(catalog.TryGet(document.Worlds[0].Objects[0], out var entry),
            "The collision catalog must be keyed to the owning scene object.");
        Require(entry.Instance?.Status == PcCollisionResourceStatus.Resolved
            && entry.Set is { Status: PcCollisionResourceStatus.Resolved, SourceWorldKey: worldKey }
            && entry.Map?.Status == PcCollisionResourceStatus.Resolved
            && entry.Map.Map?.CobKeys.SequenceEqual([0x11223344u]) == true,
            "The catalog must decode instance/set/map records and resolve the set in the instance's source world.");
    }

    private static void VerifyMissingAmbiguousAndMalformedResources()
    {
        const uint worldKey = 0x1100;
        const uint objectKey = 0x1002;
        const uint instanceKey = 0xF002;
        const uint mapKey = 0xD002;
        var owner = new PcSceneLoader.Record(objectKey, 'l', 0, 0) { LoadWorldKey = worldKey };
        var firstInstance = Record(instanceKey, 'd', worldKey, [1, 2]);
        var secondInstance = Record(instanceKey, 'd', worldKey, [3, 4]);
        var malformedMap = Record(mapKey, 'f', worldKey, [1, 0, 0]);
        SceneDocument document = Document(worldKey, objectKey, instanceKey, mapKey);
        var catalog = PcCollisionResourceCatalogBuilder.Build([owner, firstInstance, secondInstance, malformedMap],
            Origins(firstInstance, new byte[] { 1, 2 }, secondInstance,
                new byte[] { 3, 4 }, malformedMap, new byte[] { 1, 0, 0 }), document);

        Require(catalog.TryGet(document.Worlds[0].Objects[0], out var entry)
            && entry.Instance?.Status == PcCollisionResourceStatus.Ambiguous
            && entry.Instance.Diagnostic.Contains("multiple", StringComparison.OrdinalIgnoreCase)
            && entry.Map?.Status == PcCollisionResourceStatus.Malformed,
            "Ambiguous keys and malformed payloads must remain explicit diagnostics in the catalog.");

        SceneDocument missingDocument = Document(worldKey, objectKey, 0xFFFF, mapKey);
        var missingCatalog = PcCollisionResourceCatalogBuilder.Build([owner, malformedMap],
            Origins(malformedMap, new byte[] { 1, 0, 0 }), missingDocument);
        Require(missingCatalog.TryGet(missingDocument.Worlds[0].Objects[0], out var missing)
            && missing.Instance?.Status == PcCollisionResourceStatus.Missing,
            "A missing source key must remain visible as a Missing resource, not disappear from the catalog.");
    }

    private static SceneDocument Document(uint worldKey, uint objectKey, uint instanceKey, uint mapKey)
    {
        var document = new SceneDocument();
        var world = new WorldData { Key = worldKey, Name = "fixture" };
        world.Objects.Add(new SceneObject
        {
            Id = $"{objectKey:X8}", GaoKey = objectKey, Name = "fixture object",
            CollisionInstanceKey = instanceKey, CollisionMapKey = mapKey
        });
        document.Worlds.Add(world);
        return document;
    }

    private static PcSceneLoader.Record Record(uint key, char kind, uint world, byte[] bytes) =>
        new(key, kind, 0, bytes.Length) { LoadWorldKey = world };

    private static Dictionary<PcSceneLoader.Record, byte[]> Origins(params object[] pairs)
    {
        var origins = new Dictionary<PcSceneLoader.Record, byte[]>();
        for (int index = 0; index < pairs.Length; index += 2)
            origins[(PcSceneLoader.Record)pairs[index]] = (byte[])pairs[index + 1];
        return origins;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
