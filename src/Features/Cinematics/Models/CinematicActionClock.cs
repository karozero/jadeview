namespace JadeView;

internal readonly record struct CinematicFrequencyChange(float Time, int Frequency);

internal static class CinematicActionClock
{
    internal static float SourceElapsed(CinematicAction action, float elapsed, int? frequency,
        float startFrame, IReadOnlyList<CinematicFrequencyChange> changes)
    {
        foreach (var item in action.Items) item.Clip.EnsureLoaded();
        double position = startFrame / 60d;
        double previous = 0;
        foreach (var change in changes)
        {
            if (change.Time > elapsed) break;
            position = Advance(action, position, change.Time - previous, ref frequency);
            previous = change.Time;
            frequency = change.Frequency;
        }
        position = Advance(action, position, elapsed - previous, ref frequency);
        return (float)Math.Max(0, position - startFrame / 60d);
    }

    private static double Advance(CinematicAction action, double position, double elapsed, ref int? frequency)
    {
        double[] durations = action.Items.Select(item => (double)item.Clip.Duration * Math.Max(1, item.Repetitions)).ToArray();
        double total = durations.Sum();
        if (total <= 0 || elapsed <= 0) return position;
        int loopStart = Math.Clamp(action.LoopItem, 0, durations.Length - 1);
        double prefix = durations.Take(loopStart).Sum();
        double cycle = total - prefix;
        for (int step = 0; elapsed > 1e-9; step++)
        {
            if (step >= 100000) throw new InvalidDataException("Action clock step budget exceeded.");
            double local = position;
            if (local >= total)
            {
                if (action.LoopItem == byte.MaxValue || cycle <= 0) return position;
                local = prefix + (local - prefix) % cycle;
            }
            int itemIndex = 0;
            while (itemIndex < durations.Length - 1 && local >= durations[itemIndex])
                local -= durations[itemIndex++];
            double rate = CinematicAction.Rate(action.Items[itemIndex], frequency);
            if (rate <= 0) return position;
            double remaining = durations[itemIndex] - local;
            if (remaining <= 1e-9)
            {
                position += remaining + 1e-9;
                continue;
            }
            double consumed = Math.Min(elapsed, remaining / rate);
            position += consumed * rate;
            elapsed -= consumed;
            if (consumed >= remaining / rate - 1e-9)
            {
                int nextItem = itemIndex + 1 < durations.Length ? itemIndex + 1 : loopStart;
                if (nextItem != itemIndex) frequency = null;
            }
        }
        return position;
    }
}
