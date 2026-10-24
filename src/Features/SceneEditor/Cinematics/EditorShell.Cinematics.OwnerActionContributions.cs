namespace JadeView;

public sealed partial class EditorShell
{
    private readonly Dictionary<(uint ClipKey, CinematicScriptOwnerId OwnerId), string[]>
        _cinematicOwnerActionContributionCache = [];

    private string[] GetCinematicOwnerActionContributions(CinematicPlayback playback,
        CinematicScriptOwnerId ownerId)
    {
        var cacheKey = (playback.Key, ownerId);
        if (_cinematicOwnerActionContributionCache.TryGetValue(cacheKey, out string[]? cached))
            return cached;

        if (Document == null || _cinematicWorld == null)
            return _cinematicOwnerActionContributionCache[cacheKey] = [];

        string[] contributions = CinematicOwnerActionContributionResolver.FindResolvedRequests(
            playback, Document, _cinematicWorld, ownerId,
            _disabledCinematicCallbackOwners, _previewedCinematicSceneEventOwners,
            _previewedCinematicSceneEventStartTimes);
        _cinematicOwnerActionContributionCache[cacheKey] = contributions;
        return contributions;
    }
}
