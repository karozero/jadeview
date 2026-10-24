namespace JadeView;

internal sealed record CinematicScheduledMessage(
    uint WorldKey,
    uint EventListOwnerKey,
    uint EventKey,
    int ChannelIndex,
    CinematicOwnerExecutionRoute PreviewRoute,
    float ChannelSourceTime,
    float TimelineTime,
    CinematicTimelineMessage Message,
    CinematicTrackPlaybackSnapshot? ChannelState,
    uint? IrisQueueOwnerCandidateKey,
    CinematicMessageQueueOwnerEvidence QueueOwnerEvidence,
    string Provenance,
    string DeliveryLimit);

internal enum CinematicMessageQueueOwnerEvidence
{
    Unresolved,
    IrisEveTrackContext
}
