namespace JadeView;

internal sealed record CinematicEmbeddedEventTrackList(
    uint OwnerKey,
    string OwnerName,
    uint WorldKey,
    string LinkName,
    uint EventKey,
    CinematicEmbeddedEventTrack[] Tracks);

internal sealed record CinematicEmbeddedEventTrack(
    int Index,
    uint TargetKey,
    int Bone,
    int ChannelFlags,
    int KeyCount,
    float Duration)
{
    internal AnimationTrack? SourceTrack { get; init; }
    internal (float Time, CinematicLiteralCall Call)[] Calls { get; init; } = [];
    internal (float Time, CinematicTimelineMessage Message)[] Messages { get; init; } = [];
}

internal static class CinematicEventTrackFlags
{
    internal const int RunningInit = 0x0001;
    internal const int AutoLoop = 0x0002;
    internal const int AutoStop = 0x0004;
}
