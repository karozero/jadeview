namespace JadeView;

internal static class CinematicTimelineClipDiscovery
{
    internal static void Load(SceneDocument document, PcSceneLoader.Result replay,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> cinematicWorlds,
        Dictionary<PcSceneLoader.Record, AnimationClip?> decodedClips)
    {
        var timelineKeys = FindSceneTracks(replay.Records, cinematicWorlds.Keys.ToHashSet());
        var ownersByClip = replay.Records
            .Where(record => record.Kind == 'l' && cinematicWorlds.ContainsKey(record.LoadWorldKey))
            .SelectMany(record => new[] { record.SceneEvents, record.Events }
                .Where(IsValidKey)
                .Select(key => (record.LoadWorldKey, ClipKey: key, OwnerKey: record.Key)))
            .GroupBy(reference => (reference.LoadWorldKey, reference.ClipKey))
            .ToDictionary(group => group.Key, group => group.Select(item => item.OwnerKey).Distinct().ToArray());

        foreach (var record in replay.Records.Where(record => record.Kind == 'i'
            && timelineKeys.Contains((record.LoadWorldKey, record.Key))))
        {
            AnimationClip? clip = ReadClip(document, record, origins, decodedClips);
            if (clip == null) continue;

            clip.Key = record.Key;
            clip.Name = $"Cinematic · {cinematicWorlds[record.LoadWorldKey]} · {record.Key:X8}";
            clip.SceneMotion = true;
            clip.Cinematic = true;
            clip.CinematicOwnerKeys = ownersByClip.GetValueOrDefault((record.LoadWorldKey, record.Key)) ?? [];
            clip.CinematicOwnerKey = clip.CinematicOwnerKeys.FirstOrDefault();
            clip.CinematicWorldKey = record.LoadWorldKey;
            document.CinematicClips.TryAdd(record.Key, clip);
        }
    }

    internal static HashSet<(uint WorldKey, uint ClipKey)> FindSceneTracks(
        IEnumerable<PcSceneLoader.Record> records, IReadOnlySet<uint> cinematicWorldKeys) => records
        .Where(record => record.Kind == 'l'
            && cinematicWorldKeys.Contains(record.LoadWorldKey)
            && IsValidKey(record.SceneEvents))
        .Select(record => (record.LoadWorldKey, record.SceneEvents))
        .ToHashSet();

    private static AnimationClip? ReadClip(SceneDocument document, PcSceneLoader.Record record,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        Dictionary<PcSceneLoader.Record, AnimationClip?> decodedClips)
    {
        if (decodedClips.TryGetValue(record, out AnimationClip? cached)) return cached;
        try
        {
            var clip = AnimationReader.Read(origins[record], record.Offset, record.Size);
            decodedClips[record] = clip;
            return clip;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException
            or OverflowException or IndexOutOfRangeException)
        {
            document.Diagnostics.Add($"Cinematic clip {record.Key:X8} could not be decoded: {exception.Message}");
            decodedClips[record] = null;
            return null;
        }
    }

    private static bool IsValidKey(uint key) => key is > 2 and < uint.MaxValue;
}
