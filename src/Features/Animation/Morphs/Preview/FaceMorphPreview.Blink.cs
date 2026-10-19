namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private FaceMorphBlinkPlayback? blink;
    public bool BlinkPlaying => blink != null;

    public void StartBlink()
    {
        if (!SupportsPcBlink) throw new InvalidOperationException("PC blink targets are not qualified for this model.");
        StopBlink();
        blink = new(CopyWeights(), CopyChannelState());
        ChannelsActive = false;
        Timeline.Playing = false;
    }

    public void StopBlink()
    {
        if (blink == null) return;
        var captured = blink;
        blink = null;
        captured.Baseline.CopyTo(weights, 0);
        ChannelsActive = captured.Channels != null;
        if (captured.Channels != null) channelStates = captured.Channels;
    }

    public void BakeBlink()
    {
        if (!CanBakeBlink) throw new InvalidOperationException("Blink baking is not qualified for this profile.");
        var baseline = blink?.Baseline ?? CopyWeights();
        var keys = FaceMorphBlinkCurve.Bake(Timeline.Time, baseline, BlinkGroups[0], BlinkGroups[1]);
        bool overlaps = Timeline.Keys.Any(key => key.Time >= keys[0].Time && key.Time <= keys[^1].Time);
        if (overlaps) throw new InvalidOperationException("Move the morph cursor to an empty interval before baking a blink.");
        StopBlink();
        foreach (var key in keys) Timeline.Record(key.Time, key.Weights);
    }

    private void AdvanceBlink(float delta)
    {
        if (blink == null) return;
        if (!blink.Advance(delta)) { StopBlink(); return; }
        foreach (int group in BlinkGroups)
            weights[group] = FaceMorphBlinkCurve.Weight(blink.Baseline[group], blink.Phase);
    }
}
