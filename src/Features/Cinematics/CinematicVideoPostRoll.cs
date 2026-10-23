namespace JadeView;

internal static class CinematicVideoPostRoll
{
    internal static bool ShouldContinue(CinematicVideoPlaybackSample? sample,
        bool stopAtMediaEnd, bool mediaLoadFailed, bool mediaMetadataAvailable,
        bool mediaReachedEnd)
    {
        if (sample is not { Controlled: true, Ambiguous: false, HasStarted: true, IsPlaying: true })
            return false;
        if (!stopAtMediaEnd) return true;
        if (mediaLoadFailed) return false;
        return !mediaMetadataAvailable || !mediaReachedEnd;
    }
}
