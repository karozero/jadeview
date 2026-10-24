namespace JadeView;

internal sealed record PcMorphCinematicMouthVariant(int Channel, byte? ForcedModuleSelector,
    IReadOnlyList<int> FormSequence);
