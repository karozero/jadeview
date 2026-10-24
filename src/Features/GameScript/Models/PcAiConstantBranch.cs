namespace JadeView;

internal sealed record PcAiConstantBranch(
    int CallNodeIndex,
    string Operation,
    int JumpNodeIndex,
    string ConditionDescription,
    bool ConditionIsTrue,
    int BranchTargetNodeIndex,
    int FallthroughNodeIndex,
    int ChosenNodeIndex,
    string ChosenEdge,
    string Evidence);
