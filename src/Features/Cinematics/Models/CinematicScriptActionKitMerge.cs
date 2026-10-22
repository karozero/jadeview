namespace JadeView;

internal sealed record CinematicScriptActionKitMerge(
    CinematicScriptReachableEngineCall SourceCall,
    string Mode,
    uint DestinationKey,
    uint DestinationWorldKey,
    uint SourceKey,
    uint SourceWorldKey,
    string DestinationBinding,
    string SourceBinding,
    int FirstAction,
    int LastAction,
    string RangeBinding,
    int SourceSlotCount,
    int DestinationConflictCount);

internal sealed record CinematicScriptUnresolvedActionKitMerge(
    CinematicScriptReachableEngineCall SourceCall,
    string Reason);

internal sealed record CinematicScriptActionKitMergeAnalysis(
    CinematicScriptActionKitMerge[] Resolved,
    CinematicScriptUnresolvedActionKitMerge[] Unresolved);
