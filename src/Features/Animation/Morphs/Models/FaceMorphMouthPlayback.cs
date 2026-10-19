namespace JadeView;

internal sealed record FaceMorphMouthPlayback(float[] Baseline, FaceMorphChannelState[]? Channels,
    PcMorphMouthCycleState State, PcMorphMouthCycleDefinition Definition, int[] Sequence, int[] AffectedGroups);
