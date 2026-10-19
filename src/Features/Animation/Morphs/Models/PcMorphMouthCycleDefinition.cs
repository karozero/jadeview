namespace JadeView;

public sealed record PcMorphMouthCycleDefinition(byte ModuleSelector, int Channel,
    IReadOnlyList<int> FormSequence, PcMorphTransitionCompletion Completion);
