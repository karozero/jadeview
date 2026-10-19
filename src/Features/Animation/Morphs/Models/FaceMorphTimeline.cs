namespace JadeView;

// User-authored preview keys; independent of native PC controller channels.
public sealed class FaceMorphTimeline(int targetCount)
{
    private const float MaximumAdvanceDelta = .1f;
    private readonly List<FaceMorphKeyframe> keys = [];
    public IReadOnlyList<FaceMorphKeyframe> Keys => keys;
    public float Time { get; private set; }
    public float Duration => keys.Count == 0 ? 0 : keys[^1].Time;
    public bool Playing { get; set; }
    public bool Loop { get; set; } = true;

    public void Record(float time, float[] weights)
    {
        ValidateTime(time);
        if (weights.Length != targetCount || weights.Any(weight => !float.IsFinite(weight)))
            throw new ArgumentException("Invalid morph key weights.", nameof(weights));
        Playing = false;
        var key = new FaceMorphKeyframe(time, (float[])weights.Clone());
        int existing = keys.FindIndex(candidate => candidate.Time == time);
        if (existing >= 0) keys[existing] = key;
        else
        {
            int next = keys.FindIndex(candidate => candidate.Time > time);
            keys.Insert(next >= 0 ? next : keys.Count, key);
        }
    }

    public bool RemoveCurrentKey()
    {
        Playing = false;
        return keys.RemoveAll(key => key.Time == Time) > 0;
    }
    public void Clear()
    {
        keys.Clear();
        Playing = false;
        Time = 0;
    }

    public float[]? Seek(float time)
    {
        ValidateTime(time);
        Time = time;
        return Sample(time);
    }

    public float[]? Advance(float delta)
    {
        if (!Playing || delta <= 0 || Duration <= 0) return null;
        Time += Math.Clamp(delta, 0, MaximumAdvanceDelta);
        if (Time >= Duration)
        {
            if (Loop) Time %= Duration;
            else
            {
                Time = Duration;
                Playing = false;
            }
        }
        return Sample(Time);
    }

    public float[]? Sample(float time)
    {
        ValidateTime(time);
        if (keys.Count == 0) return null;
        if (time <= keys[0].Time) return (float[])keys[0].Weights.Clone();
        int next = keys.FindIndex(key => key.Time > time);
        if (next < 0) return (float[])keys[^1].Weights.Clone();
        var first = keys[next - 1];
        var second = keys[next];
        float blend = (time - first.Time) / (second.Time - first.Time);
        var weights = new float[targetCount];
        for (int target = 0; target < weights.Length; target++)
            weights[target] = first.Weights[target] * (1 - blend) + second.Weights[target] * blend;
        return weights;
    }

    public FaceMorphKeyframe[] CopyKeys() => keys.Select(key => new FaceMorphKeyframe(key.Time, (float[])key.Weights.Clone())).ToArray();
    private static void ValidateTime(float time)
    {
        if (!float.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
    }
}
