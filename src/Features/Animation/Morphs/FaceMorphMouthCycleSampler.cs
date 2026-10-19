namespace JadeView;

internal static class FaceMorphMouthCycleSampler
{
    internal const int FramesPerSecond = 60;
    internal const float MaximumDuration = 10;

    internal static FaceMorphMouthPlayback Advance(FaceMorphMouthPlayback playback, float[] weights, float delta)
    {
        var state = PcMorphMouthCycleEvaluator.Advance(playback.State, playback.Sequence,
            weights.Length, delta, playback.Definition.Completion);
        float[] evaluated = FaceMorphChannelEvaluator.Evaluate(weights.Length, [state.Transition.Channel]);
        foreach (int group in playback.AffectedGroups) weights[group] = evaluated[group];
        return playback with { State = state };
    }

    internal static FaceMorphKeyframe[] Sample(FaceMorphMouthPlayback playback, float[] pose, float duration)
    {
        if (!float.IsFinite(duration) || duration <= 0 || duration > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), "Mouth sample duration must be positive and at most 10 seconds.");
        int intervals = (int)Math.Ceiling((double)duration * FramesPerSecond);
        var samples = new List<FaceMorphKeyframe>(intervals + 1);
        float[] weights = (float[])pose.Clone();
        samples.Add(new(0, (float[])weights.Clone()));
        for (int frame = 1; frame <= intervals; frame++)
        {
            float time = Math.Min(frame / (float)FramesPerSecond, duration);
            float delta = time - samples[^1].Time;
            if (delta <= 0) continue; // Float durations can round onto the previous grid point.
            playback = Advance(playback, weights, delta);
            samples.Add(new(time, (float[])weights.Clone()));
        }
        return samples.ToArray();
    }
}
