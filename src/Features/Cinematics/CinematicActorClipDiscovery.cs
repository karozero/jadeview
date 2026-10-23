namespace JadeView;

internal static class CinematicActorClipDiscovery
{
    internal static PcSceneLoader.Record[] FindActorClips(
        IEnumerable<PcSceneLoader.Record> records,
        IReadOnlySet<uint> cinematicWorldKeys,
        IReadOnlySet<uint> actorMemberKeys)
    {
        var sourceRecords = records.ToArray();
        var actorEventClipKeys = sourceRecords
            .Where(record => record.Kind == 'l'
                && actorMemberKeys.Contains(record.Key)
                && IsValidKey(record.Events))
            .Select(record => (record.LoadWorldKey, ClipKey: record.Events))
            .ToHashSet();
        actorEventClipKeys.UnionWith(CinematicTimelineClipDiscovery.FindSceneTracks(
            sourceRecords, cinematicWorldKeys));

        return sourceRecords
            .Where(record => record.Kind == 'i'
                && actorEventClipKeys.Contains((record.LoadWorldKey, record.Key)))
            .ToArray();
    }

    private static bool IsValidKey(uint key) => key is > 2 and < uint.MaxValue;
}
