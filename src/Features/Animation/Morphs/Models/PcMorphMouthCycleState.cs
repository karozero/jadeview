namespace JadeView;

// Explicit controller snapshot; sequence/profile selection belongs to its caller.
public sealed record PcMorphMouthCycleState(PcMorphTransitionState Transition,
    int NextFormIndex, bool Enabled);
