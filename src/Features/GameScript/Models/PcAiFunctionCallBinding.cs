namespace JadeView;

internal sealed record PcAiFunctionCallBinding(
    int CallNodeIndex,
    string Operation,
    int ArgumentNodeIndex,
    string ArgumentDescription,
    int? ResultTargetNodeIndex,
    string? ResultTargetDescription,
    string Status,
    string Evidence,
    int? ResolvedIntegerValue = null,
    int? ResultAssignmentNodeIndex = null,
    int? ResolvedArgumentValue = null,
    int? ArgumentValueAssignmentNodeIndex = null);
