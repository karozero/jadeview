namespace JadeView;

internal sealed record PcAiFunctionCallPath(
    uint[] FunctionKeys,
    string[] FunctionNames,
    int[] CallNodeIndexes,
    string[] TransferKinds,
    string Status);
