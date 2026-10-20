namespace JadeView;

public sealed record PcMorphMouthCycleDefinition(byte ModuleSelector, int Channel,
    IReadOnlyList<int> FormSequence, PcMorphTransitionCompletion Completion,
    bool AutoStart = false, float DefaultFactor = .8f, float DefaultSpeed = 11,
    int InitialFormIndex = 0);
