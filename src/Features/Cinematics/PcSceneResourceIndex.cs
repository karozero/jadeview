namespace JadeView;

internal sealed class PcSceneResourceIndex
{
    private readonly Dictionary<(uint WorldKey, uint ResourceKey), PcSceneLoader.Record[]> recordsByWorld;
    private readonly Dictionary<uint, PcSceneLoader.Record[]> recordsByKey;

    internal PcSceneResourceIndex(IEnumerable<PcSceneLoader.Record> records)
    {
        PcSceneLoader.Record[] recordArray = records.Distinct().ToArray();
        recordsByWorld = recordArray
            .GroupBy(record => (record.LoadWorldKey, record.Key))
            .ToDictionary(group => group.Key, group => group.ToArray());
        recordsByKey = recordArray
            .GroupBy(record => record.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    internal PcSceneLoader.Record? Resolve(uint resourceKey, uint preferredWorldKey) =>
        ResolveDetailed(resourceKey, preferredWorldKey).Record;

    internal PcSceneResourceResolution ResolveDetailed(uint resourceKey, uint preferredWorldKey)
    {
        if (recordsByWorld.TryGetValue((preferredWorldKey, resourceKey), out var localRecords))
            return SelectSingle(localRecords, "multiple records in the preferred world");

        if (preferredWorldKey != 0
            && recordsByWorld.TryGetValue((0, resourceKey), out var sharedRecords))
            return SelectSingle(sharedRecords, "multiple shared records");

        if (!recordsByKey.TryGetValue(resourceKey, out var candidates))
            return new(null, "resource was not found");

        return SelectSingle(candidates, "multiple records across the loaded archive scope");
    }

    private static PcSceneResourceResolution SelectSingle(PcSceneLoader.Record[] candidates, string ambiguity)
    {
        return candidates.Length switch
        {
            1 => new(candidates[0], null),
            _ => new(null, ambiguity)
        };
    }
}

internal readonly record struct PcSceneResourceResolution(PcSceneLoader.Record? Record, string? Failure);
