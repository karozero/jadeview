namespace JadeView;

internal static class CinematicActorClipDiscovery
{
    internal static PcSceneLoader.Record[] FindActorEventClips(
        IEnumerable<PcSceneLoader.Record> records,
        IReadOnlySet<uint> cinematicWorldKeys,
        IReadOnlySet<uint> actorMemberKeys)
    {
        var eventClipKeys = records
            .Where(record => record.Kind == 'l'
                && cinematicWorldKeys.Contains(record.LoadWorldKey)
                && actorMemberKeys.Contains(record.Key)
                && IsValidKey(record.Events))
            .Select(record => (record.LoadWorldKey, ClipKey: record.Events))
            .ToHashSet();

        return records
            .Where(record => record.Kind == 'i'
                && eventClipKeys.Contains((record.LoadWorldKey, record.Key)))
            .ToArray();
    }

    private static bool IsValidKey(uint key) => key is > 2 and < uint.MaxValue;
}
