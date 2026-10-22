namespace JadeView;

internal sealed record CinematicScriptMessageTrack(
    uint WorldKey,
    uint OwnerKey,
    string LinkName,
    uint EventKey,
    string Name,
    (float Time, CinematicTimelineMessage Message)[] Messages,
    int? ChannelIndex = null)
{
    internal (float Time, CinematicLiteralCall Call)[] EngineCalls { get; init; } = [];

    internal static IEnumerable<CinematicScriptMessageTrack> FromScriptEventTracks(
        IEnumerable<CinematicScriptEventTrack> tracks) => tracks.Select(track => new CinematicScriptMessageTrack(
            track.WorldKey, track.OwnerKey, track.LinkName, track.EventKey, track.Name, track.Messages)
        {
            EngineCalls = track.Calls
        });

    internal static IEnumerable<CinematicScriptMessageTrack> FromCinematicClips(
        IEnumerable<AnimationClip> clips) => clips.Select(clip => new CinematicScriptMessageTrack(
            clip.CinematicWorldKey, clip.CinematicOwnerKey, "Cinematic", clip.Key, clip.Name,
            CinematicTimelineMessage.ReadTimeline(clip))
        {
            EngineCalls = CinematicLiteralCall.ReadTimeline(clip)
        });

    internal static IEnumerable<CinematicScriptMessageTrack> FromEmbeddedEventTracks(
        IEnumerable<CinematicEmbeddedEventTrackList> eventLists) => eventLists
        .SelectMany(eventList => eventList.Tracks.Select(track => new CinematicScriptMessageTrack(
            eventList.WorldKey, eventList.OwnerKey, eventList.LinkName, eventList.EventKey,
            eventList.OwnerName, track.Messages, track.Index)
        {
            EngineCalls = track.Calls
        }));

    internal static IEnumerable<CinematicScriptMessageTrack> FromActorClips(IEnumerable<WorldData> worlds)
    {
        foreach (var world in worlds)
        foreach (var actor in world.Actors)
        foreach (var clip in actor.Clips)
        {
            clip.EnsureLoaded();
            var messages = CinematicTimelineMessage.ReadTimeline(clip, actor.Root.GaoKey);
            var engineCalls = CinematicLiteralCall.ReadTimeline(clip, actor.Root.GaoKey);
            if (messages.Length == 0 && engineCalls.Length == 0) continue;

            yield return new(world.Key, actor.Root.GaoKey, "ActorAction", clip.Key,
                actor.Root.Name, messages)
            {
                EngineCalls = engineCalls
            };
        }
    }
}
