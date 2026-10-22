namespace JadeView;

internal static class AnimationExportSampling
{
    private const int SamplesPerSecond = 60;
    private const int MaximumSamples = 200000;

    internal static float[] Times(AnimationClip clip)
    {
        if (!float.IsFinite(clip.Duration) || clip.Duration < 0)
            throw new InvalidDataException($"Animation '{clip.Name}' has an invalid duration.");
        if (clip.Duration == 0)
            return [];
        double sampleCount = Math.Ceiling(clip.Duration * (double)SamplesPerSecond);
        if (sampleCount > MaximumSamples)
            throw new InvalidDataException($"Animation '{clip.Name}' exceeds the export sample budget.");
        var times = new SortedSet<float> { 0, clip.Duration };
        for (int sample = 1; sample < (int)sampleCount; sample++)
            times.Add(sample / (float)SamplesPerSecond);
        if (clip.CinematicBody != null)
            foreach (float boundary in clip.CinematicBody.PoseBoundaries())
            {
                AddBoundary(times, boundary, clip.Duration);
                if (boundary > 0 && boundary <= clip.Duration) times.Add(MathF.BitDecrement(boundary));
            }
        foreach (var track in clip.Tracks)
        {
            foreach (var key in track.Keys)
            {
                if (!float.IsFinite(key.Duration) || key.Duration < 0)
                    throw new InvalidDataException("Invalid source animation key duration.");
                AddBoundary(times, key.Time, clip.Duration);
                if (key.TimeCurve != null)
                {
                    foreach (var point in key.TimeCurve.Points)
                    {
                        if (float.IsFinite(point.Time))
                            AddBoundary(times, key.Time + point.Time, clip.Duration);
                    }
                }
                float end = key.Time + key.Duration;
                AddBoundary(times, end, clip.Duration);
                // Mixed STEP/LINEAR source keys cannot be expressed by one glTF sampler.
                // Keep the pre-jump value until the last representable float before its end.
                if ((key.Flags & 2048) != 0 && end > 0 && end <= clip.Duration)
                    times.Add(MathF.BitDecrement(end));
            }
            var transformKeys = track.Keys.Where(key => key.Position != null || key.Rotation != null || key.Scale != null).ToArray();
            for (int index = 1; index < transformKeys.Length; index++)
            {
                float transition = transformKeys[index].Time;
                if ((transformKeys[index - 1].Flags & 2048) != 0 && transition > 0 && transition <= clip.Duration)
                    times.Add(MathF.BitDecrement(transition));
            }
        }
        if (times.Count > MaximumSamples)
            throw new InvalidDataException($"Animation '{clip.Name}' exceeds the export sample budget.");
        return times.ToArray();
    }

    private static void AddBoundary(SortedSet<float> times, float time, float duration)
    {
        if (!float.IsFinite(time) || time < 0)
            throw new InvalidDataException("Invalid source animation key time.");
        if (time <= duration)
            times.Add(time);
    }
}
