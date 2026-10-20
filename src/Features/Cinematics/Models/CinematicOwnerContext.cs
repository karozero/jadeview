namespace JadeView;

internal static class CinematicOwnerContext
{
    internal static uint[] ForTrack(AnimationClip clip, AnimationTrack track)
    {
        if (track.Target != 0) return [track.Target];
        if (clip.CinematicOwnerKeys.Length > 0) return clip.CinematicOwnerKeys;
        return [clip.CinematicOwnerKey];
    }
}
