namespace JadeView;

internal enum CinematicStartGateStatus
{
    Satisfied,
    Rejected,
    Unknown
}

internal enum CinematicStartGateEvidenceKind
{
    PreviewTimeline,
    PreviewObjectState,
    RuntimeInputUnavailable
}

internal sealed record CinematicStartGateResult(
    int Mask,
    string Name,
    CinematicStartGateStatus Status,
    CinematicStartGateEvidenceKind EvidenceKind,
    string Evidence,
    string[] RequiredInputs)
{
    internal GameScriptInitialActivationForecast? InitialActivationForecast { get; init; }
}

internal sealed record CinematicStartGateEvaluation(
    int EnabledMask,
    CinematicStartGateStatus Status,
    CinematicStartGateResult[] Gates,
    string Scope);
