namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private FaceMorphChannelState[] channelStates = [];
    public bool ChannelsActive { get; private set; }
    public int ChannelCount => channelStates.Length;

    public FaceMorphChannelState GetChannel(int index) => channelStates[index];
    public FaceMorphChannelState[]? CopyChannelState() => ChannelsActive ? (FaceMorphChannelState[])channelStates.Clone() : null;

    public void SetChannel(int index, float weight, float progression)
    {
        var incoming = (FaceMorphChannelState[])channelStates.Clone();
        incoming[index] = new(weight, progression);
        RestoreChannelState(incoming);
    }

    public void RestoreChannelState(FaceMorphChannelState[] states)
    {
        bool valid = Data != null && states.Length == Data.Channels.Length
            && states.All(state => state is { IsValid: true });
        if (!valid) throw new ArgumentException("Morph channel state does not match the source modifier.", nameof(states));
        var channels = states.Select((state, index) => new FaceMorphChannel(
            BitConverter.SingleToUInt32Bits(state.Weight), BitConverter.SingleToUInt32Bits(state.Progression),
            Data!.Channels[index].GroupReferences));
        var evaluated = FaceMorphChannelEvaluator.Evaluate(weights.Length, channels);
        StopBlink();
        channelStates = (FaceMorphChannelState[])states.Clone();
        evaluated.CopyTo(weights, 0);
        ChannelsActive = true;
        Timeline.Playing = false;
    }

    private void ResetChannelState()
    {
        channelStates = Data?.Channels.Select(channel =>
            new FaceMorphChannelState(float.IsFinite(channel.Weight) ? channel.Weight : 0, 0)).ToArray() ?? [];
        ChannelsActive = false;
    }
}
