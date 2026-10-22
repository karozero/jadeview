namespace JadeView;

internal sealed record CinematicEventTargetCommand(
    CinematicEmbeddedEventTrackList EventList,
    CinematicEmbeddedEventTrack Track,
    float Time,
    CinematicLiteralCall Call);

internal static class CinematicEventTargetCommandResolver
{
    internal static CinematicEventTargetCommand[] Resolve(
        IEnumerable<CinematicEmbeddedEventTrackList> eventLists, uint worldKey, uint targetKey)
    {
        if (targetKey is <= 2 or uint.MaxValue) return [];

        return eventLists
            .Where(list => list.WorldKey == worldKey)
            .Where(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase))
            .SelectMany(list => list.Tracks.SelectMany(track => track.Calls
                .Where(entry => entry.Call.Target == targetKey)
                .Select(entry => new CinematicEventTargetCommand(list, track, entry.Time, entry.Call))))
            .OrderBy(command => command.Time)
            .ThenBy(command => command.EventList.OwnerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.Track.Index)
            .ToArray();
    }
}
