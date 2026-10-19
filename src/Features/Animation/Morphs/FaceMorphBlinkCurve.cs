namespace JadeView;

// Envelope recovered from PC 00977D30. Trigger/delay scheduling is deliberately separate.
public static class FaceMorphBlinkCurve
{
    public const float PhaseSpeed = 15;
    public static readonly float PhaseLimit = BitConverter.UInt32BitsToSingle(0x40490FDC);
    public static float Duration => PhaseLimit / PhaseSpeed;

    public static float Weight(float baseline, float phase) => baseline + (1 - baseline) * MathF.Sin(phase);

    public static FaceMorphKeyframe[] Bake(float start, float[] baseline, int upperGroup, int lowerGroup)
    {
        bool invalid = !float.IsFinite(start) || start < 0 || baseline.Any(weight => !float.IsFinite(weight))
            || upperGroup < 0 || lowerGroup < 0 || upperGroup >= baseline.Length || lowerGroup >= baseline.Length
            || upperGroup == lowerGroup;
        if (invalid) throw new ArgumentException("Invalid blink pose or start time.");
        var offsets = Enumerable.Range(0, (int)MathF.Ceiling(Duration * 120))
            .Select(index => index / 120f).Append(MathF.PI / (2 * PhaseSpeed)).Append(Duration).Order().ToArray();
        var keys = offsets.Select(offset =>
        {
            var weights = (float[])baseline.Clone();
            // Match preview cleanup: end exactly at the captured baseline.
            if (offset > 0 && offset < Duration)
            {
                float phase = Math.Min(offset * PhaseSpeed, PhaseLimit);
                weights[upperGroup] = Weight(baseline[upperGroup], phase);
                weights[lowerGroup] = Weight(baseline[lowerGroup], phase);
            }
            return new FaceMorphKeyframe(start + offset, weights);
        }).ToArray();
        bool invalidTimes = keys.Any(key => !float.IsFinite(key.Time))
            || keys.Zip(keys.Skip(1)).Any(pair => pair.First.Time >= pair.Second.Time);
        if (invalidTimes) throw new ArgumentException("Blink keys cannot be represented at this timeline time.");
        return keys;
    }
}
