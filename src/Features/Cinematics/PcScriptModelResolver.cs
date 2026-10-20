namespace JadeView;

internal static class PcScriptModelResolver
{
    private const int BrainRecordSize = sizeof(uint) * 2;

    internal static Dictionary<uint, PcSceneLoader.Record> CreateResourceMap(
        IEnumerable<PcSceneLoader.Record> records) =>
        records.GroupBy(record => record.Key)
            .ToDictionary(group => group.Key, group => group.Last());

    internal static bool TryRead(PcSceneLoader.Record owner,
        IReadOnlyDictionary<uint, PcSceneLoader.Record> resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        out PcSceneLoader.Record model, out uint instanceKey)
    {
        model = null!;
        instanceKey = 0;

        if (owner.BrainFieldOffset < 0) return false;

        uint brainKey = Preview.U32(origins[owner], owner.BrainFieldOffset);
        if (!resources.TryGetValue(brainKey, out var brain) || brain.Kind != 'q' || brain.Size < BrainRecordSize)
            return false;

        var brainBytes = origins[brain].AsSpan(brain.Offset, brain.Size);
        uint modelKey = BitConverter.ToUInt32(brainBytes);
        instanceKey = BitConverter.ToUInt32(brainBytes[sizeof(uint)..]);
        if (resources.TryGetValue(modelKey, out model!) && model.Kind == 's') return true;

        model = null!;
        instanceKey = 0;
        return false;
    }
}
