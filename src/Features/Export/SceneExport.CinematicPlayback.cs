namespace JadeView;

public sealed partial class SceneExporter
{
    private CinematicPlayback? CinematicPlaybackFor(AnimationClip clip, WorldData world)
    {
        if (options.CinematicPlayback is { } activePlayback
            && activePlayback.Key == clip.Key && activePlayback.WorldKey == world.Key)
            return activePlayback;
        if (options.CinematicDocument is not { } document) return null;

        var key = (world, clip.Key);
        if (cinematicPlaybackCache.TryGetValue(key, out CinematicPlayback? cached)) return cached;
        return cinematicPlaybackCache[key] = CinematicPlayback.DiscoverClip(clip, document,
            world, resolver, options.DisabledCinematicCallbackOwners,
            options.PreviewedCinematicSceneEventOwners,
            options.PreviewedCinematicSceneEventStartTimes);
    }
}
