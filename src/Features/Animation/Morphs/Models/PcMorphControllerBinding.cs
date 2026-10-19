namespace JadeView;

internal sealed record PcMorphControllerBinding(uint InitializerKey, uint UpdateKey,
    IReadOnlyList<int> Sequence, PcMorphTransitionCompletion Completion)
{
    internal PcMorphMouthCycleDefinition? Bind(FaceMorphData? data)
    {
        const int mouthChannel = 4;
        bool validChannel = data != null && data.Channels.Length > mouthChannel
            && data.Channels[mouthChannel].GroupReferences.Length >= 2;
        if (!validChannel || Sequence.Count != 32) return null;
        if (Sequence.Any(group => group < 0 || group >= data!.Groups.Length)) return null;
        bool hasMotion = Sequence.Distinct().Any(group =>
            data!.Groups[group].AccumulatedDeltas().Values.Any(delta => delta.LengthSquared() > 0));
        return hasMotion ? new(0, mouthChannel, Sequence, Completion) : null;
    }
}
