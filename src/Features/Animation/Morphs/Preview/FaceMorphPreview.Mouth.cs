namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private FaceMorphMouthPlayback? mouth;
    public bool MouthCyclePlaying => mouth != null;

    public void StartMouthCycle(float factor = FaceMorphMouthCycleSettings.DefaultFactor,
        float speed = FaceMorphMouthCycleSettings.DefaultSpeed)
    {
        var definition = MouthCycleDefinition ?? throw new InvalidOperationException("No qualified mouth sequence for this source layout.");
        FaceMorphMouthPlaybackFactory.Validate(factor, speed);
        StopMouthCycle();
        StopBlink();
        var channelState = channelStates[definition.Channel];
        var channel = new FaceMorphChannel(BitConverter.SingleToUInt32Bits(channelState.Weight),
            BitConverter.SingleToUInt32Bits(channelState.Progression),
            (uint[])channelGroupReferences[definition.Channel].Clone());
        mouth = FaceMorphMouthPlaybackFactory.Create(definition, channel,
            CopyWeights(), CopyChannelState(), factor, speed);
        ChannelsActive = false;
        Timeline.Playing = false;
    }

    public void StopMouthCycle()
    {
        if (mouth == null) return;
        var captured = mouth;
        mouth = null;
        captured.Baseline.CopyTo(weights, 0);
        ChannelsActive = captured.Channels != null;
        if (captured.Channels != null) channelStates = captured.Channels;
    }

    internal float[] CopyAuthoringWeights() => (float[])(mouth?.Baseline ?? weights).Clone();
    internal FaceMorphChannelState[]? CopyAuthoringChannels()
    {
        if (mouth == null) return CopyChannelState();
        return mouth.Channels is { } captured ? (FaceMorphChannelState[])captured.Clone() : null;
    }

    private void AdvanceMouthCycle(float delta)
    {
        // Evaluate(0) is also used while sampling export poses; leave transient
        // state untouched then. Preview deliberately caps elapsed steps at 0.1s.
        if (mouth == null || delta <= 0) return;
        mouth = FaceMorphMouthCycleSampler.Advance(mouth, weights,
            Math.Min(delta, FaceMorphMouthCycleSettings.MaximumPreviewStep));
    }
}
