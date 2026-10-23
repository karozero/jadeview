namespace JadeView;

internal static class CinematicActionLibraryReader
{
    private const int ActionHeaderSize = 2;
    private const int SerializedItemSize = 19;

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        PcSceneLoader.Record[] recordArray = records.ToArray();
        var resources = new PcSceneResourceIndex(recordArray);
        var recordsByKey = recordArray.GroupBy(record => record.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var clips = new Dictionary<PcSceneLoader.Record, AnimationClip>();
        foreach (var owner in recordArray.Where(record => record.Skeleton is not 0 and not uint.MaxValue)
                     .GroupBy(record => (record.LoadWorldKey, record.Key))
                     .Select(group => group.Last()))
        {
            var table = resources.Resolve(owner.Skeleton, owner.LoadWorldKey);
            if (table?.Kind != 'b' || !origins.TryGetValue(table, out byte[]? tableBytes)) continue;
            var standardEntries = ReadEntries(table, tableBytes, PcActionKitFormat.EntrySize,
                owner.LoadWorldKey, resources, recordsByKey, origins);
            var speedModeEntries = ReadEntries(table, tableBytes, PcActionKitFormat.ActionKeySize,
                owner.LoadWorldKey, resources, recordsByKey, origins);
            var selectedEntries = speedModeEntries.Length > standardEntries.Length
                ? speedModeEntries : standardEntries;
            var actions = selectedEntries.ToDictionary(entry => entry.Slot, entry =>
                ReadAction(entry.Record, owner.LoadWorldKey, resources, recordsByKey, origins, clips));
            document.CinematicActionKitsByOwner[(owner.LoadWorldKey, owner.Key)] = actions;
            document.CinematicActionKits[owner.Key] = actions;
        }
    }

    private static (int Slot, PcSceneLoader.Record Record)[] ReadEntries(PcSceneLoader.Record table,
        byte[] tableBytes, int entrySize, uint ownerWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<uint, PcSceneLoader.Record[]> recordsByKey,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        var entries = new List<(int Slot, PcSceneLoader.Record Record)>();
        int tableEnd = table.Offset + table.Size;
        int slot = 0;
        for (int offset = table.Offset;
             offset <= tableEnd - entrySize;
             offset += entrySize, slot++)
        {
            uint key = Preview.U32(tableBytes, offset);
            if (key == 1) break;
            if (ResolveIdenticalResource(key, 'a', ownerWorldKey, resources, recordsByKey,
                    origins) is { } action)
                entries.Add((slot, action));
        }
        return entries.ToArray();
    }

    private static CinematicAction ReadAction(PcSceneLoader.Record action,
        uint preferredWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<uint, PcSceneLoader.Record[]> recordsByKey,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        Dictionary<PcSceneLoader.Record, AnimationClip> clips)
    {
        byte[] bytes = origins[action];
        if (action.Size < ActionHeaderSize) return new(byte.MaxValue, []);
        int count = bytes[action.Offset];
        if (ActionHeaderSize + count * SerializedItemSize > action.Size)
            throw new InvalidDataException("Truncated cinematic action items.");
        var transitionTables = new bool[count];
        for (int index = 0; index < count; index++)
            transitionTables[index] = Preview.U32(bytes, action.Offset + ActionHeaderSize + index * SerializedItemSize + 4) != 0;
        int transitionOffset = ActionHeaderSize + count * SerializedItemSize;
        var transitions = CinematicActionTransitions.Read(
            bytes.AsSpan(action.Offset + transitionOffset, action.Size - transitionOffset), transitionTables);
        var items = new List<CinematicActionItem>();
        for (int index = 0; index < count; index++)
        {
            int offset = action.Offset + ActionHeaderSize + index * SerializedItemSize;
            uint clipKey = Preview.U32(bytes, offset);
            if (ResolveIdenticalResource(clipKey, 'i', preferredWorldKey, resources,
                    recordsByKey, origins) is not { } source
                || !origins.TryGetValue(source, out byte[]? clipBytes)) continue;
            if (!clips.TryGetValue(source, out var clip))
            {
                clip = new AnimationClip
                {
                    Key = clipKey,
                    Name = $"{clipKey:X8}",
                    Read = () => AnimationReader.Read(clipBytes, source.Offset, source.Size)
                };
                clips.Add(source, clip);
            }
            items.Add(new(clip, Preview.U32(bytes, offset + 8), bytes[offset + 14], bytes[offset + 12])
            {
                BlendFrames = bytes[offset + 13],
                CustomBits = bytes[offset + 15],
                DesignFlags = (ushort)Preview.U16(bytes, offset + 16),
                Flags = bytes[offset + 18],
                Transitions = transitions[index]
            });
        }
        return new(bytes[action.Offset + 1], items.ToArray());
    }

    private static PcSceneLoader.Record? ResolveIdenticalResource(uint key, char expectedKind,
        uint preferredWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<uint, PcSceneLoader.Record[]>? recordsByKey,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        var preferred = resources.Resolve(key, preferredWorldKey);
        if (preferred?.Kind == expectedKind && origins.ContainsKey(preferred))
            return preferred;
        if (recordsByKey == null || !recordsByKey.TryGetValue(key, out var candidates))
            return null;

        PcSceneLoader.Record[] matching = candidates.Where(candidate => candidate.Kind == expectedKind
            && origins.ContainsKey(candidate)).ToArray();
        if (matching.Length == 0 || matching.Skip(1).Any(candidate =>
                !HaveIdenticalPayload(matching[0], candidate, origins)))
            return null;
        return matching.OrderBy(candidate => candidate.LoadWorldKey).First();
    }

    private static bool HaveIdenticalPayload(PcSceneLoader.Record left,
        PcSceneLoader.Record right, IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        byte[] leftBytes = origins[left];
        byte[] rightBytes = origins[right];
        if (left.Size != right.Size || left.Offset < 0 || right.Offset < 0
            || left.Offset > leftBytes.Length - left.Size
            || right.Offset > rightBytes.Length - right.Size)
            return false;
        return leftBytes.AsSpan(left.Offset, left.Size)
            .SequenceEqual(rightBytes.AsSpan(right.Offset, right.Size));
    }
}
