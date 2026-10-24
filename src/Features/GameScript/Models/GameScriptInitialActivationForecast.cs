namespace JadeView;

internal enum GameScriptActivationEligibility
{
    Eligible,
    NotEligible
}

internal sealed record GameScriptActivationPathForecast(
    string WorldMode,
    GameScriptActivationEligibility Eligibility,
    string Reason,
    GameScriptEotMembership? EotMembership = null);

internal sealed record GameScriptInitialActivationForecast(
    uint SerializedStateFlags,
    uint InitialRuntimeFlags2,
    GameScriptActivationPathForecast[] Paths,
    string Scope);
