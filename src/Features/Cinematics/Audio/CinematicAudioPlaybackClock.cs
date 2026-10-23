namespace JadeView;

internal static class CinematicAudioPlaybackClock
{
    private const double MaximumCorrectionSeconds = .002;
    private const double MaximumCorrectionPerFrame = .05;
    private const double ClockCorrectionRate = .08;

    internal static float Delta(double playheadTime, double audioClockTime,
        bool audioClockAvailable, float frameDelta)
    {
        float safeFrameDelta = float.IsFinite(frameDelta) ? Math.Max(0, frameDelta) : 0;
        if (!audioClockAvailable || !double.IsFinite(playheadTime)
            || !double.IsFinite(audioClockTime) || safeFrameDelta == 0)
            return safeFrameDelta;

        double clockError = audioClockTime - playheadTime;
        double maximumCorrection = Math.Min(MaximumCorrectionSeconds,
            safeFrameDelta * MaximumCorrectionPerFrame);
        double correction = Math.Clamp(clockError * ClockCorrectionRate,
            -maximumCorrection, maximumCorrection);
        return (float)Math.Max(0, safeFrameDelta + correction);
    }
}
