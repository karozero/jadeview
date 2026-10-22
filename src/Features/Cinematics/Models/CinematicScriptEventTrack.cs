namespace JadeView;

internal sealed record CinematicScriptEventTrack(
    uint WorldKey,
    uint OwnerKey,
    string LinkName,
    uint EventKey,
    string Name,
    float Duration,
    (float Time, CinematicLiteralCall Call)[] Calls,
    (float Time, CinematicTimelineMessage Message)[] Messages)
{
    internal CinematicScriptOwnerId OwnerId => new(WorldKey, OwnerKey);
}
