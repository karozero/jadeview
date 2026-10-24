namespace JadeView;

internal static class PcCollisionResourceCatalogBuilder
{
    // PC scene resource tags; see docs/collision-cob-wire-audit.md.
    private const char InstanceResourceKind = 'd';
    private const char SetResourceKind = 'e';
    private const char MapResourceKind = 'f';

    internal static PcCollisionResourceCatalog Build(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var recordList = records.Distinct().ToArray();
        var index = new PcSceneResourceIndex(recordList);
        var objects = new Dictionary<SceneObject, PcCollisionObjectResources>();
        foreach (WorldData world in document.Worlds)
        foreach (SceneObject sceneObject in world.Objects)
        {
            bool hasInstance = sceneObject.CollisionInstanceKey is not (0 or uint.MaxValue);
            bool hasMap = sceneObject.CollisionMapKey is not (0 or uint.MaxValue);
            if (!hasInstance && !hasMap)
                continue;

            var owner = index.ResolveDetailed(sceneObject.GaoKey, world.Key);
            uint ownerWorldKey = owner.Record?.LoadWorldKey ?? world.Key;
            var instance = ReadResource(sceneObject.CollisionInstanceKey, ownerWorldKey,
                InstanceResourceKind, index, origins);
            PcCollisionResourceInfo? set = null;
            if (instance?.Instance is { } decodedInstance)
                set = ReadResource(decodedInstance.ColSetKey,
                    instance.SourceWorldKey, SetResourceKind, index, origins);
            var map = ReadResource(sceneObject.CollisionMapKey, ownerWorldKey,
                MapResourceKind, index, origins);
            objects[sceneObject] = new(ownerWorldKey, sceneObject.GaoKey,
                sceneObject.CollisionInstanceKey, sceneObject.CollisionMapKey,
                instance, set, map, owner.Failure ?? "");
        }
        return new PcCollisionResourceCatalog(objects);
    }

    private static PcCollisionResourceInfo? ReadResource(uint key, uint preferredWorldKey, char expectedKind,
        PcSceneResourceIndex index, IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        if (key is 0 or uint.MaxValue) return null;
        PcSceneResourceResolution resolution = index.ResolveDetailed(key, preferredWorldKey);
        if (resolution.Record is not { } record)
        {
            var status = resolution.Failure?.Contains("multiple", StringComparison.OrdinalIgnoreCase) == true
                ? PcCollisionResourceStatus.Ambiguous : PcCollisionResourceStatus.Missing;
            return new(key, preferredWorldKey, expectedKind, status,
                resolution.Failure ?? "Resource could not be resolved.", []);
        }
        if (record.Kind != expectedKind)
            return new(key, record.LoadWorldKey, record.Kind, PcCollisionResourceStatus.Unsupported,
                $"Expected '{expectedKind}' collision resource, found '{record.Kind}'.", []);
        if (!origins.TryGetValue(record, out byte[]? bytes)
            || record.Offset < 0 || record.Size < 0 || record.Offset > bytes.Length - record.Size)
            return new(key, record.LoadWorldKey, record.Kind, PcCollisionResourceStatus.Malformed,
                "Source payload range is unavailable or outside its archive bytes.", []);

        ReadOnlySpan<byte> payload = bytes.AsSpan(record.Offset, record.Size);
        return expectedKind switch
        {
            InstanceResourceKind => FromDecode(PcCollisionResourceReader.ReadInstance(payload,
                PcCollisionWireDialect.JadedPc, PcCollisionReadMode.BinaryCompact), key, record),
            SetResourceKind => FromDecode(PcCollisionResourceReader.ReadSet(payload,
                PcCollisionWireDialect.JadedPc, PcCollisionReadMode.BinaryCompact), key, record),
            MapResourceKind => FromDecode(PcCollisionResourceReader.ReadMap(payload,
                PcCollisionWireDialect.JadedPc, PcCollisionReadMode.BinaryCompact), key, record),
            _ => new(key, record.LoadWorldKey, record.Kind, PcCollisionResourceStatus.Unsupported,
                $"Resource kind '{record.Kind}' is not a collision resource.", [])
        };
    }

    private static PcCollisionResourceInfo FromDecode<T>(PcCollisionDecodeResult<T> result,
        uint key, PcSceneLoader.Record record) where T : class
    {
        PcCollisionResourceStatus status = result.Status switch
        {
            PcCollisionDecodeStatus.Success => PcCollisionResourceStatus.Resolved,
            PcCollisionDecodeStatus.UnsupportedFeature or PcCollisionDecodeStatus.UnsupportedVersion => PcCollisionResourceStatus.Unsupported,
            _ => PcCollisionResourceStatus.Malformed
        };
        return new(key, record.LoadWorldKey, record.Kind, status, result.Diagnostic,
            result.Warnings, result.Value as PcCollisionInstance,
            result.Value as PcCollisionSet, result.Value as PcCollisionMap);
    }
}
