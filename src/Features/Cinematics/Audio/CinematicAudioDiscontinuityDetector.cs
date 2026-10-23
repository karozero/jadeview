namespace JadeView;

internal readonly record struct CinematicAudioSampleJump(int Frame, int Channel, int Delta,
    short Previous, short Current);

internal static class CinematicAudioDiscontinuityDetector
{
    private const int MinimumJump = 12_000;
    private const int MaximumReports = 5;
    private const double MinimumReportSpacingSeconds = .05;

    internal static string[] DescribePossibleClicks(short[] samples, int channels, int sampleRate)
    {
        if (channels is < 1 or > 2 || sampleRate <= 0 || samples.Length < channels * 2)
            return [];

        int frameCount = samples.Length / channels;
        int minimumSpacingFrames = Math.Max(1, (int)(sampleRate * MinimumReportSpacingSeconds));
        var largestJumps = new List<CinematicAudioSampleJump>(MaximumReports);
        for (int frame = 1; frame < frameCount; frame++)
        for (int channel = 0; channel < channels; channel++)
        {
            int sampleIndex = frame * channels + channel;
            short previous = samples[sampleIndex - channels];
            short current = samples[sampleIndex];
            int delta = Math.Abs((int)current - previous);
            if (delta < MinimumJump) continue;

            int nearbyIndex = largestJumps.FindIndex(jump =>
                Math.Abs(jump.Frame - frame) < minimumSpacingFrames);
            if (nearbyIndex >= 0 && largestJumps[nearbyIndex].Delta >= delta) continue;
            if (nearbyIndex >= 0) largestJumps.RemoveAt(nearbyIndex);

            largestJumps.Add(new(frame, channel, delta, previous, current));
            largestJumps.Sort((left, right) => right.Delta.CompareTo(left.Delta));
            if (largestJumps.Count > MaximumReports)
                largestJumps.RemoveAt(largestJumps.Count - 1);
        }

        return largestJumps.OrderBy(jump => jump.Frame)
            .Select(jump =>
            {
                string timestamp = (jump.Frame / (double)sampleRate)
                    .ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                string delta = jump.Delta.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                string channel = jump.Channel == 0 ? "left" : "right";
                return $"Possible click-like PCM jump at {timestamp}s: {delta} sample units "
                    + $"on {channel} channel ({jump.Previous} → {jump.Current}); source audio is left unchanged.";
            })
            .ToArray();
    }
}
