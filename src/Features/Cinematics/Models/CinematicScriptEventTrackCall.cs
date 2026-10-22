namespace JadeView;

internal sealed record CinematicScriptEventTrackCall(
    CinematicScriptReachableEngineCall SourceCall,
    string Operation,
    int? TrackIndex,
    CinematicEmbeddedEventTrackList EventList,
    CinematicEmbeddedEventTrack[] Tracks,
    string TargetBinding,
    string? IndexBinding = null);

internal sealed record CinematicScriptUnresolvedEventTrackCall(
    CinematicScriptReachableEngineCall SourceCall,
    string Reason);

internal sealed record CinematicScriptEventTrackCallAnalysis(
    CinematicScriptEventTrackCall[] Resolved,
    CinematicScriptUnresolvedEventTrackCall[] Unresolved);
