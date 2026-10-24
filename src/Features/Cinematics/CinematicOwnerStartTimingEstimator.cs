using System.Globalization;

namespace JadeView;

internal static class CinematicOwnerStartTimingEstimator
{
    private const string CineStartCallback = "Cine_Events_cine_tests_depart";

    internal static CinematicOwnerStartTimingEstimate Estimate(CinematicScriptOwner owner,
        float serializedGateSignalTime)
    {
        if (!float.IsFinite(serializedGateSignalTime) || serializedGateSignalTime < 0)
            return new(0, false, "The serialized gate-signal time is invalid; no timing estimate was applied.");

        if (!IsCineEventsOwner(owner))
            return new(serializedGateSignalTime, false,
                "This owner is not identified as a Cine_Events controller; its serialized signal time is used directly.");

        float? firstDelay = ReadFiniteSeconds(owner, "f_first_delay");
        float? departureDelay = ReadFiniteSeconds(owner, "des_f_tempo");
        float? runtimeTimecode = ReadFiniteSeconds(owner, "f_timecode");
        if (runtimeTimecode != 0)
            return new(serializedGateSignalTime, false,
                runtimeTimecode is { } timecode
                    ? $"Serialized f_timecode={Format(timecode)}s is nonzero; the owner may already be in its runtime playback path."
                    : "Effective f_timecode is unavailable; the owner playback state is unknown.");
        if (firstDelay is not { } configuredFirstDelay || departureDelay is not { } configuredDepartureDelay)
            return new(serializedGateSignalTime, false,
                "Effective f_first_delay or des_f_tempo is missing or invalid; the serialized signal time is used directly.");

        float estimatedStart = Math.Max(serializedGateSignalTime, configuredFirstDelay) + configuredDepartureDelay;
        if (!float.IsFinite(estimatedStart))
            return new(serializedGateSignalTime, false,
                "The configured delay sum is non-finite; the serialized signal time is used directly.");

        return new(estimatedStart, true,
            $"Explicit preview estimate: max(signal {Format(serializedGateSignalTime)}s, f_first_delay {Format(configuredFirstDelay)}s) + des_f_tempo {Format(configuredDepartureDelay)}s. Runtime callback dispatch and other gates are not executed.");
    }

    private static bool IsCineEventsOwner(CinematicScriptOwner owner) =>
        owner.CallbackNames.Contains(CineStartCallback, StringComparer.Ordinal)
        || owner.ModelFunctionNames.Contains(CineStartCallback, StringComparer.Ordinal);

    private static float? ReadFiniteSeconds(CinematicScriptOwner owner, string variableName)
    {
        CinematicScriptVariable? variable = owner.EffectiveVariable(variableName);
        if (variable?.Type != PcVariableFormat.FloatType
            || !float.TryParse(variable.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
            || !float.IsFinite(seconds) || seconds < 0)
            return null;
        return seconds;
    }

    private static string Format(float seconds) => seconds.ToString("F3", CultureInfo.InvariantCulture);
}

internal sealed record CinematicOwnerStartTimingEstimate(float PreviewStartTime,
    bool UsedConfiguredTiming, string Evidence);
