namespace JadeView;

internal static class CinematicOwnerContext
{
    internal static uint[] ForTrack(AnimationClip clip, AnimationTrack track, uint fallbackOwnerKey = 0)
    {
        if (track.Target != 0) return [track.Target];
        if (clip.CinematicOwnerKeys.Length > 0) return clip.CinematicOwnerKeys;
        uint ownerKey = clip.CinematicOwnerKey is 0 or uint.MaxValue
            ? fallbackOwnerKey : clip.CinematicOwnerKey;
        return [ownerKey];
    }
}
