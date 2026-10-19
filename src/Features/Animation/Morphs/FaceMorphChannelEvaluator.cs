namespace JadeView;

// PC callback 00B31900, recovered from a saved runtime image. This evaluates
// explicit channel state only; it does not invent blink triggers or speech keys.
public static class FaceMorphChannelEvaluator
{
    // Exclusive bound: converting this float or larger to Int32 is invalid.
    private const float MaximumProgressionExclusive = 2147483648f;
    public static float[] Evaluate(int groupCount, IEnumerable<FaceMorphChannel> channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCount);
        var weights = new float[groupCount];
        foreach (var channel in channels)
            Accumulate(weights, channel);
        return weights;
    }

    private static void Accumulate(float[] weights, FaceMorphChannel channel)
    {
        float weight = channel.Weight;
        float progression = channel.Progression;
        bool invalidState = !float.IsFinite(weight) || !float.IsFinite(progression)
            || progression < 0 || progression >= MaximumProgressionExclusive;
        if (invalidState)
            throw new ArgumentException("Morph channel requires finite weight and nonnegative, bounded progression.");
        var references = channel.GroupReferences;
        if (references.Length == 0 || weight == 0 || progression == 0) return;

        int lower = (int)progression;
        int upper = lower;
        while (upper < references.Length && references[upper] >= weights.Length) upper++;
        float blend;
        if (upper < references.Length)
        {
            lower--;
            while (lower >= 0 && references[lower] >= weights.Length) lower--;
            blend = (progression - lower - 1) / (upper - lower);
        }
        else
        {
            upper = references.Length - 1;
            while (upper >= 0 && references[upper] >= weights.Length) upper--;
            if (upper < 0) return;
            lower = -1;
            blend = 1;
        }

        if (blend > 0) Add(weights, references[upper], blend * weight);
        if (lower >= 0 && 1 - blend > 0) Add(weights, references[lower], (1 - blend) * weight);
    }

    private static void Add(float[] weights, uint group, float contribution)
    {
        float combined = weights[group] + contribution;
        if (!float.IsFinite(combined)) throw new ArgumentException("Morph channel weights overflowed.");
        weights[group] = combined;
    }
}
