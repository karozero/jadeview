namespace JadeView;

internal sealed record CinematicScriptActionRequest(
    CinematicScriptReachableEngineCall SourceCall,
    int ActionIndex,
    CinematicAction Action,
    string IndexBinding,
    int? ControlValue,
    string? ControlBinding);

internal sealed record CinematicScriptUnresolvedActionRequest(
    CinematicScriptReachableEngineCall SourceCall,
    string Reason);

internal sealed record CinematicScriptActionRequestAnalysis(
    CinematicScriptActionRequest[] Resolved,
    CinematicScriptUnresolvedActionRequest[] Unresolved);
