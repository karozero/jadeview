namespace JadeView;

internal sealed record CinematicScriptChannelTransfer(
    uint OwnerKey,
    uint TargetKey,
    string TargetName,
    string OvaField,
    int SourceChannel,
    int TargetChannel,
    string FunctionName,
    string Source,
    int GetterLine,
    int SetterLine,
    string CallbackPath,
    bool SourcePathResolved,
    CinematicScriptBranchConditionSource[] BranchConditions,
    bool ClearsInvisibleFlag,
    int? InvisibleFlagLine)
{
    internal CinematicScriptCallbackPathStep[] CallbackPathSteps { get; init; } = [];
    internal string[] ReceiverGuardAliases { get; init; } = [];
}
