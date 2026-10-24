namespace JadeView;

internal sealed record PcAiScalarConstantAssignment(
    int TargetNodeIndex,
    string TargetDescription,
    int ValueNodeIndex,
    int Value,
    int AssignmentNodeIndex);
