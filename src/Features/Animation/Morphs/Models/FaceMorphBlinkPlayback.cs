namespace JadeView;

internal sealed class FaceMorphBlinkPlayback(float[] baseline, FaceMorphChannelState[]? channels)
{
    internal float[] Baseline { get; } = baseline;
    internal FaceMorphChannelState[]? Channels { get; } = channels;
    internal float Phase { get; private set; }

    internal bool Advance(float delta)
    {
        if (float.IsFinite(delta) && delta > 0)
            Phase = Math.Min(FaceMorphBlinkCurve.PhaseLimit, Phase + delta * FaceMorphBlinkCurve.PhaseSpeed);
        return Phase < FaceMorphBlinkCurve.PhaseLimit;
    }
}
