namespace JadeView;

internal sealed record CinematicLightState(SceneObject Receiver, SceneLight Light);

internal sealed record CinematicLightCommandDiagnostic(
    float Time,
    CinematicLiteralCall Call,
    CinematicLightCommandStatus Status,
    string Detail);

internal enum CinematicLightCommandStatus
{
    Applied,
    UnresolvedReceiver,
    ReceiverHasNoLight,
    InvalidArguments,
    UnsupportedLightType,
    InvalidFlagIndex,
    UnresolvedRelatedObject,
    UnsupportedValue
}

internal sealed record CinematicLightStateEvaluation(
    IReadOnlyList<CinematicLightState> Lights,
    IReadOnlyList<CinematicLightCommandDiagnostic> Diagnostics)
{
    internal IReadOnlyList<CinematicWorldCycleLightDiagnostic> WorldCycleDiagnostics { get; init; } = [];
}

[Flags]
internal enum CinematicLightFields
{
    None = 0,
    Color = 1,
    Near = 2,
    Far = 4,
    Flags = 8
}

internal sealed record CinematicWorldCycleLightOverride(
    SceneObject Receiver,
    SceneLight Light,
    CinematicLightFields Fields,
    uint FlagMask);

internal enum CinematicWorldCycleLightDiagnosticCode
{
    MissingTarget,
    AmbiguousTarget,
    TargetHasNoLight,
    WrongLightType,
    TwilightActiveStatePreserved,
    MoonRuntimeUnresolved,
    ControllerTargetCollision,
    LiteralOrderingApproximation
}

internal sealed record CinematicWorldCycleLightDiagnostic(
    uint ControllerKey,
    uint ReceiverKey,
    string Field,
    CinematicWorldCycleLightDiagnosticCode Code,
    string Detail);

internal sealed record CinematicWorldCycleLightEvaluation(
    IReadOnlyList<CinematicWorldCycleLightOverride> Overrides,
    IReadOnlyList<CinematicWorldCycleLightDiagnostic> Diagnostics);
