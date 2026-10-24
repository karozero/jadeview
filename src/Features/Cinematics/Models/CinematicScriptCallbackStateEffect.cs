namespace JadeView;

internal sealed record CinematicScriptCallbackStateEffect(
    uint OwnerKey,
    string StateEffect,
    string Api,
    string Variant,
    string[] Arguments,
    CinematicScriptBranchConditionSource[] BranchConditions,
    string FunctionName,
    string Source,
    int Line,
    string CallbackPath,
    string ExecutionStatus)
{
    internal uint? ReceiverKey { get; init; }
    internal string ReceiverEvidence { get; init; } = "Receiver is unresolved from available callback source.";
}
