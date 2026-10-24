namespace JadeView;

internal sealed record PcAiDirectFunctionCall(
    int CallNodeIndex,
    string EngineFunction,
    int? ReferenceNodeIndex,
    uint? TargetKey,
    string? TargetName,
    string Status);
