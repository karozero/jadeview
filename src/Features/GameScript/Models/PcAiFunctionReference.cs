namespace JadeView;

internal sealed record PcAiFunctionReference(
    int NodeIndex,
    string Operation,
    int? ReferenceNodeIndex,
    uint? TargetKey,
    string? TargetName,
    string Status);
