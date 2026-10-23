namespace JadeView;

internal static class CinematicAudioPlaybackStatus
{
    internal static string Describe(CinematicAudioCue[] cues, int decodedCueCount, float time,
        bool enabled, bool playing)
    {
        if (!enabled) return $"Source audio disabled · {decodedCueCount} cue assets decoded";

        int activeTimelineCueCount = 0;
        float nextCueTime = float.PositiveInfinity;
        foreach (var cue in cues)
        {
            if (time >= cue.Start && time < cue.End) activeTimelineCueCount++;
            if (cue.Start > time && cue.Start < nextCueTime) nextCueTime = cue.Start;
        }

        if (!playing)
            return DescribePaused(decodedCueCount, nextCueTime, activeTimelineCueCount);
        if (activeTimelineCueCount > 0)
            return $"Playing · {activeTimelineCueCount} timeline cue(s) active · {decodedCueCount} cue assets decoded";

        return float.IsFinite(nextCueTime)
            ? $"Playing silence until next cue in {nextCueTime - time:F2}s · {decodedCueCount} cue assets decoded"
            : $"No timeline cue at or after this playhead · {decodedCueCount} cue assets decoded";
    }

    private static string DescribePaused(int decodedCueCount, float nextCueTime,
        int activeTimelineCueCount)
    {
        if (activeTimelineCueCount > 0)
            return $"Paused · {activeTimelineCueCount} timeline cue(s) active at this playhead · {decodedCueCount} cue assets decoded";

        return float.IsFinite(nextCueTime)
            ? $"Paused · next cue at {nextCueTime:F2}s · {decodedCueCount} cue assets decoded"
            : $"Paused · no later timeline cue · {decodedCueCount} cue assets decoded";
    }
}
