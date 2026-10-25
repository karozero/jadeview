namespace JadeView;

internal sealed record CinematicScriptCallbackStateEffect(
    uint OwnerKey,
    string StateEffect,
    string Api,
    string Variant,
    string[] Arguments,
    string? ResultVariable,
    CinematicScriptBranchConditionSource[] BranchConditions,
    string FunctionName,
    string Source,
    int Line,
    string CallbackPath,
    string ExecutionStatus)
{
    internal uint? ReceiverKey { get; init; }
    internal string ReceiverEvidence { get; init; } = "Receiver is unresolved from available callback source.";
    internal string[] ReceiverGuardAliases { get; init; } = [];
    internal string[] SerializedBranchFindings { get; init; } = [];
}
