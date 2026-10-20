namespace JadeView;

internal static class CinematicActionLibraryReader
{
    private const int ActionHeaderSize = 2;
    private const int SerializedItemSize = 19;

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var resources = records.GroupBy(record => record.Key).ToDictionary(group => group.Key, group => group.Last());
        var clips = new Dictionary<uint, AnimationClip>();
        foreach (var owner in resources.Values.Where(record => record.Kind == 'l'))
        {
            if (!resources.TryGetValue(owner.Skeleton, out var table) || table.Kind != 'b') continue;
            var actions = new Dictionary<int, CinematicAction>();
            byte[] bytes = origins[table];
            int slot = 0;
            for (int offset = table.Offset; offset <= table.Offset + table.Size - 4; offset += 4, slot++)
            {
                uint key = Preview.U32(bytes, offset);
                if (key == 1) { offset += 4; continue; }
                if (!resources.TryGetValue(key, out var action) || action.Kind != 'a') continue;
                actions[slot] = ReadAction(action, resources, origins, clips);
            }
            document.CinematicActionKits[owner.Key] = actions;
        }
    }

    private static CinematicAction ReadAction(PcSceneLoader.Record action,
        IReadOnlyDictionary<uint, PcSceneLoader.Record> resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, Dictionary<uint, AnimationClip> clips)
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
            if (!resources.TryGetValue(clipKey, out var source) || source.Kind != 'i') continue;
            if (!clips.TryGetValue(clipKey, out var clip))
            {
                clip = new AnimationClip
                {
                    Key = clipKey,
                    Name = $"{clipKey:X8}",
                    Read = () => AnimationReader.Read(origins[source], source.Offset, source.Size)
                };
                clips.Add(clipKey, clip);
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
}
