namespace JadeView;

internal sealed record PcMorphControllerBinding(uint InitializerKey, uint UpdateKey,
    IReadOnlyList<int> Sequence, PcMorphTransitionCompletion Completion,
    byte ModuleSelector = 0, int Channel = 4, bool AutoStart = false,
    float DefaultFactor = FaceMorphMouthCycleSettings.DefaultFactor,
    float DefaultSpeed = FaceMorphMouthCycleSettings.DefaultSpeed, int InitialFormIndex = 0,
    bool MatchMorphData = false)
{
    internal PcMorphMouthCycleDefinition? Bind(FaceMorphData? data) =>
        TryBind(data, out _);

    internal PcMorphMouthCycleDefinition? TryBind(FaceMorphData? data, out string? failure)
    {
        failure = null;
        bool validChannel = data != null && data.Channels.Length > Channel
            && data.Channels[Channel].GroupReferences.Length >= 2;
        if (!validChannel)
        {
            failure = $"mouth channel {Channel} is missing or has fewer than two form slots";
            return null;
        }
        if (Sequence.Count != 32)
        {
            failure = $"source sequence contains {Sequence.Count} forms instead of 32";
            return null;
        }
        if (Sequence.Any(group => group < 0 || group >= data!.Groups.Length))
        {
            failure = "source form sequence references groups outside this mesh";
            return null;
        }
        bool supportedDefault = float.IsFinite(DefaultFactor) && DefaultFactor >= 0 && DefaultFactor <= 1
            && float.IsFinite(DefaultSpeed) && DefaultSpeed > 0 && DefaultSpeed <= FaceMorphMouthCycleSettings.MaximumSpeed;
        if ((uint)InitialFormIndex >= 32 || AutoStart && !supportedDefault)
        {
            failure = "controller initial state is outside the supported range";
            return null;
        }
        bool hasMotion = Sequence.Distinct().Any(group =>
            data!.Groups[group].AccumulatedDeltas().Values.Any(delta => delta.LengthSquared() > 0));
        if (!hasMotion)
        {
            failure = "source form sequence has no nonzero deltas in this mesh";
            return null;
        }
        return new(ModuleSelector, Channel, Sequence, Completion, AutoStart,
            DefaultFactor, DefaultSpeed, InitialFormIndex);
    }
}
