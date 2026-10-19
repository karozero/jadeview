namespace JadeView;

// Explicit snapshot of one qualified controller slot, not a live actor binding.
public sealed record PcMorphTransitionState(FaceMorphChannel Channel,
    float TargetFactor, float CurrentFactor, float Progression, float Speed,
    int TargetGroup, PcMorphTransitionPhase Phase);
