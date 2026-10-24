namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private FaceMorphChannelState[] channelStates = [];
    private uint[][] channelGroupReferences = [];
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
        => ApplyChannelState(states, stopOtherPlayback: true);

    internal void ApplyCinematicChannelState(int index, float weight, float progression)
    {
        if ((uint)index >= (uint)channelStates.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var states = (FaceMorphChannelState[])channelStates.Clone();
        states[index] = new(weight, progression);
        ApplyChannelState(states, stopOtherPlayback: false);
    }

    private void ApplyChannelState(FaceMorphChannelState[] states, bool stopOtherPlayback)
    {
        bool valid = Data != null && states.Length == Data.Channels.Length
            && states.All(state => state is { IsValid: true });
        if (!valid) throw new ArgumentException("Morph channel state does not match the source modifier.", nameof(states));
        var channels = states.Select((state, index) => new FaceMorphChannel(
            BitConverter.SingleToUInt32Bits(state.Weight), BitConverter.SingleToUInt32Bits(state.Progression),
            channelGroupReferences[index]));
        var evaluated = FaceMorphChannelEvaluator.Evaluate(weights.Length, channels);
        if (stopOtherPlayback)
        {
            StopMouthCycle();
            StopBlink();
        }
        channelStates = (FaceMorphChannelState[])states.Clone();
        ReplaceWeights(evaluated);
        ChannelsActive = true;
        Timeline.Playing = false;
    }

    private void ResetChannelState()
    {
        channelGroupReferences = Data?.Channels
            .Select(channel => (uint[])channel.GroupReferences.Clone()).ToArray() ?? [];
        channelStates = Data?.Channels.Select(channel =>
            new FaceMorphChannelState(float.IsFinite(channel.Weight) ? channel.Weight : 0, 0)).ToArray() ?? [];
        ChannelsActive = false;
    }

    public bool SetChannelGroupReference(int channel, int slot, int group)
        => SetChannelGroupReference(channel, slot, group, stopOtherPlayback: true);

    internal bool SetCinematicChannelGroupReference(int channel, int slot, int group)
        => SetChannelGroupReference(channel, slot, group, stopOtherPlayback: false);

    private bool SetChannelGroupReference(int channel, int slot, int group, bool stopOtherPlayback)
    {
        if (Data == null || (uint)channel >= (uint)channelGroupReferences.Length
            || group < 0 || group >= Data.Groups.Length
            || (uint)slot >= (uint)channelGroupReferences[channel].Length) return false;
        channelGroupReferences[channel][slot] = (uint)group;
        if (ChannelsActive) ApplyChannelState(channelStates, stopOtherPlayback);
        if (mouth is { } playback)
        {
            var transition = playback.State.Transition;
            var updatedChannel = transition.Channel with
            {
                GroupReferences = (uint[])channelGroupReferences[channel].Clone()
            };
            var updatedTransition = transition with { Channel = updatedChannel };
            mouth = playback with { State = playback.State with { Transition = updatedTransition } };
        }
        return true;
    }

    public PcMorphTransitionState? BeginChannelTransition(int channel, int group, float factor, float speed)
    {
        if (Data == null || (uint)channel >= (uint)channelStates.Length
            || group < 0 || group >= Data.Groups.Length
            || !float.IsFinite(factor) || factor < 0 || factor > 1
            || !float.IsFinite(speed) || speed <= 0 || speed > 30
            || channelGroupReferences[channel].Length < 2) return null;
        var current = channelStates[channel];
        var channelData = new FaceMorphChannel(BitConverter.SingleToUInt32Bits(current.Weight),
            BitConverter.SingleToUInt32Bits(current.Progression),
            (uint[])channelGroupReferences[channel].Clone());
        return new(channelData, factor, current.Weight, current.Progression, speed, group,
            PcMorphTransitionPhase.Pending);
    }

    public PcMorphTransitionState AdvanceChannelTransition(int channel, PcMorphTransitionState transition, float delta)
    {
        var advanced = PcMorphTransitionEvaluator.Advance(transition, Data?.Groups.Length ?? 0, delta);
        channelGroupReferences[channel] = (uint[])advanced.Channel.GroupReferences.Clone();
        var updated = (FaceMorphChannelState[])channelStates.Clone();
        updated[channel] = new(advanced.CurrentFactor, advanced.Progression);
        RestoreChannelState(updated);
        return advanced;
    }
}
