namespace JadeView;

internal sealed record PcAiTrackMutation(
    int NodeIndex,
    string Operation,
    int? TrackIndex,
    uint? TargetKey,
    string? TargetName,
    string Status);
