namespace JadeView;

internal static class FaceMorphMouthPlaybackFactory
{
    internal static void Validate(float factor, float speed)
    {
        bool validFactor = float.IsFinite(factor) && factor >= 0 && factor <= 1;
        bool validSpeed = float.IsFinite(speed) && speed > 0 && speed <= FaceMorphMouthCycleSettings.MaximumSpeed;
        if (!validFactor || !validSpeed)
            throw new ArgumentException("Mouth preview requires factor 0..1 and speed 0..30 (exclusive zero).");
    }

    internal static FaceMorphMouthPlayback Create(PcMorphMouthCycleDefinition definition, FaceMorphChannel channel,
        float[] baseline, FaceMorphChannelState[]? channels, float factor, float speed)
    {
        var transition = new PcMorphTransitionState(channel, factor, 0, 2, speed, 0, PcMorphTransitionPhase.Settled);
        var state = new PcMorphMouthCycleState(transition, definition.InitialFormIndex, true);
        int[] sequence = definition.FormSequence.ToArray();
        var sourceGroups = channel.GroupReferences.Where(reference => reference < baseline.Length)
            .Select(reference => (int)reference);
        int[] affectedGroups = sequence.Concat(sourceGroups).Distinct().ToArray();
        return new(baseline, channels, state, definition, sequence, affectedGroups);
    }
}
