namespace JadeView;

internal sealed record GameScriptObjectFlagChange(uint TargetKey, uint SetMask,
    uint ClearMask, bool TargetsGroup);
