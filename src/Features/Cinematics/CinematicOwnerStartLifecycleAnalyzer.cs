using System.Globalization;

namespace JadeView;

/// <summary>
/// Presents the source-derived owner start path without executing callbacks or duplicating
/// the detailed Cine_Events predicate evaluator.
/// </summary>
internal static class CinematicOwnerStartLifecycleAnalyzer
{
    private const uint EventTableFlag = 0x02000000;
    private const uint PendingInactiveRuntimeFlag = 0x00400000;
    private const uint DistanceCullExtraFlag = 0x4;
    private const string CineStartCallback = "Cine_Events_cine_tests_depart";

    internal static CinematicOwnerStartLifecycle Analyze(CinematicScriptOwner owner,
        IReadOnlyList<CinematicScriptEventTrack>? decodedEventTracks)
    {
        CinematicScriptCallbackSlot[] configuredCallbacks = owner.CallbackSlots?
            .Where(slot => HasResourceKey(slot.FunctionKey)).ToArray() ?? [];
        bool callbackSlotsDecoded = owner.CallbackSlots != null;
        bool hasStartCallback = owner.CallbackNames.Contains(CineStartCallback, StringComparer.Ordinal);
        bool modelReferencesStartCallback = owner.ModelFunctionNames.Contains(CineStartCallback, StringComparer.Ordinal);
        bool hasSceneEventKey = HasResourceKey(owner.SceneEventsKey);
        bool hasDecodedSceneEventTracks = decodedEventTracks is { Count: > 0 };
        bool eventTableRequested = (owner.SerializedGaoFlags & EventTableFlag) != 0;
        bool cineEventsModel = hasStartCallback || modelReferencesStartCallback;
        GameScriptEotMembership membership = GameScriptEotMembershipResolver
            .Evaluate(owner.SerializedGaoFlags);
        GameScriptInitialActivationForecast activationForecast = GameScriptInitialActivationForecaster
            .Evaluate(owner.SerializedGaoFlags, owner.SerializedStateFlags);

        return new(
        [
            new("Archive owner links", HasResourceKey(owner.BrainKey) && HasResourceKey(owner.ScriptModelKey)
                    ? CinematicOwnerLifecycleEvidence.ArchiveResolved : CinematicOwnerLifecycleEvidence.PartiallyResolved,
                $"World {owner.WorldKey:X8} resolves owner {owner.OwnerKey:X8} to brain {FormatKey(owner.BrainKey)} and AI model {FormatKey(owner.ScriptModelKey)}.",
                ["These are serialized resource links; resolving them does not imply that the game instantiated or activated the object."]),
            BuildCallbackPhase(owner, configuredCallbacks, callbackSlotsDecoded,
                hasStartCallback, modelReferencesStartCallback),
            new("World and EOT activation", CinematicOwnerLifecycleEvidence.RuntimeUnknown,
                eventTableRequested
                    ? $"Serialized GAO flag 0x{EventTableFlag:X8} requests event-table participation when world activation builds the EOT tables."
                    : $"Serialized GAO flag 0x{EventTableFlag:X8} is clear; runtime code could still change it before EOT construction.",
                [$"Fresh-load projection: state flags 0x{activationForecast.SerializedStateFlags:X8} → initial flags2 0x{activationForecast.InitialRuntimeFlags2:X8}.",
                 .. activationForecast.Paths.Select(DescribeActivationPath),
                 $"If activated, GAO identity flags 0x{membership.IdentityFlags:X8} select: {DescribeMembership(membership)}.",
                 "World activation and eventGameObjectsTablePF membership are runtime outcomes.",
                 $"EVE_MainCall also checks runtime flags2 0x{PendingInactiveRuntimeFlag:X8} and, when extraFlag 0x{DistanceCullExtraFlag:X}, a nonzero owner distance."]),
            BuildCineStartPhase(hasStartCallback, modelReferencesStartCallback),
            BuildConfiguredTimingPhase(owner, cineEventsModel),
            BuildEventPhase(owner, decodedEventTracks, hasSceneEventKey, hasDecodedSceneEventTracks)
        ]);
    }

    private static CinematicOwnerLifecyclePhase BuildConfiguredTimingPhase(
        CinematicScriptOwner owner, bool cineEventsModel)
    {
        if (!cineEventsModel)
            return new("Configured start timing", CinematicOwnerLifecycleEvidence.RuntimeUnknown,
                "This owner is not identified as the shared Cine_Events controller.",
                ["No Cine_Events timing interpretation is applied to unrelated script models."]);

        float? firstDelay = ReadFiniteSeconds(owner, "f_first_delay");
        float? departureDelay = ReadFiniteSeconds(owner, "des_f_tempo");
        float? serializedTimecode = ReadFiniteSeconds(owner, "f_timecode");
        float totalConfiguredWait = firstDelay.GetValueOrDefault() + departureDelay.GetValueOrDefault();
        bool valuesResolved = firstDelay.HasValue && departureDelay.HasValue && float.IsFinite(totalConfiguredWait);
        bool runtimeTimecodeActive = serializedTimecode is > 0;
        string delaySummary = valuesResolved
            ? $"Configured waits total {FormatSeconds(totalConfiguredWait)}s: f_first_delay={FormatSeconds(firstDelay!.Value)}s before Cine_Events initialization, then des_f_tempo={FormatSeconds(departureDelay!.Value)}s after its departure predicates pass."
            : "The configured first-init and post-predicate delays are incomplete or invalid; no wait total is computed.";
        string timecodeSummary = serializedTimecode is { } timecode
            ? $" Serialized f_timecode={FormatSeconds(timecode)}s{(runtimeTimecodeActive ? " also holds the departure path" : " is zero in this archive snapshot")}."
            : " Serialized f_timecode is unavailable.";

        return new("Configured start timing",
            valuesResolved ? CinematicOwnerLifecycleEvidence.ArchiveResolved
                : CinematicOwnerLifecycleEvidence.PartiallyResolved,
            delaySummary + timecodeSummary,
            ["These are serialized waits, not an absolute clip start time. Actor/collision/save-state/readiness/priority gates can delay or prevent GoToEvents.",
             "The preview does not advance Cine_Events timers or infer that TRAPlayAll ran; do not shift EVE tracks from these values alone."]);
    }

    private static float? ReadFiniteSeconds(CinematicScriptOwner owner, string variableName)
    {
        CinematicScriptVariable? variable = owner.EffectiveVariable(variableName);
        if (variable?.Type != PcVariableFormat.FloatType || !float.TryParse(variable.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float seconds)
            || !float.IsFinite(seconds) || seconds < 0)
            return null;
        return seconds;
    }

    private static string FormatSeconds(float seconds) =>
        seconds.ToString("F3", CultureInfo.InvariantCulture);

    private static string DescribeMembership(GameScriptEotMembership membership) =>
        membership.TableNames.Length == 0
            ? "no EOT update table"
            : string.Join(", ", membership.TableNames);

    private static string DescribeActivationPath(GameScriptActivationPathForecast path)
    {
        string eligibility = path.Eligibility == GameScriptActivationEligibility.Eligible
            ? "eligible" : "blocked";
        string membership = path.EotMembership is { } eot
            ? $"; EOT tables: {DescribeMembership(eot)}"
            : "; no EOT membership forecast because initial activation is blocked";
        return $"{path.WorldMode}: {eligibility} — {path.Reason}{membership}.";
    }

    private static CinematicOwnerLifecyclePhase BuildCallbackPhase(CinematicScriptOwner owner,
        CinematicScriptCallbackSlot[] configuredCallbacks, bool callbackSlotsDecoded,
        bool hasStartCallback, bool modelReferencesStartCallback)
    {
        string assignmentSummary = callbackSlotsDecoded
            ? $"{configuredCallbacks.Length} non-empty callback slot(s) are serialized on the effective owner model."
            : "Effective callback-slot assignment was not decoded for this owner.";
        var details = new List<string>
        {
            "The engine dispatches callback slots from render, animation, and world-frame paths; slot assignment alone does not prove a dispatch."
        };
        if (hasStartCallback)
            details.Add($"{CineStartCallback} is present in the effective callback names.");
        else if (modelReferencesStartCallback)
            details.Add($"The model references {CineStartCallback}, but no effective owner callback slot confirms its assignment.");
        else
            details.Add("No Cine_Events start callback is identified in the available owner/model callback data.");

        if (owner.CallbackSlots is { } slots)
            details.AddRange(slots.Where(slot => HasResourceKey(slot.FunctionKey))
                .Select(slot => $"Slot {slot.Index:D2}: {slot.FunctionName ?? $"unmapped {slot.FunctionKey:X8}"} ({slot.FunctionKey:X8})."));

        return new("AI callback assignment",
            callbackSlotsDecoded ? CinematicOwnerLifecycleEvidence.ArchiveResolved
                : CinematicOwnerLifecycleEvidence.PartiallyResolved,
            assignmentSummary, [.. details]);
    }

    private static CinematicOwnerLifecyclePhase BuildCineStartPhase(bool hasStartCallback,
        bool modelReferencesStartCallback)
    {
        string summary = hasStartCallback
            ? $"The owner has the {CineStartCallback} callback needed to enter the Cine_Events departure test."
            : modelReferencesStartCallback
                ? $"The model contains {CineStartCallback}, but effective owner assignment is unresolved."
                : "No Cine_Events departure callback is linked by the available callback data.";
        return new("Cine_Events start predicate", hasStartCallback
                ? CinematicOwnerLifecycleEvidence.ArchiveResolved
                : modelReferencesStartCallback
                    ? CinematicOwnerLifecycleEvidence.PartiallyResolved
                    : CinematicOwnerLifecycleEvidence.RuntimeUnknown,
            summary,
            ["Configured start gates are reported in the separate Activation and start gates section.",
             "Saved-game flags, active main actors, collision/readiness, interaction state, priority, and timers can still decide whether the callback starts playback."]);
    }

    private static CinematicOwnerLifecyclePhase BuildEventPhase(CinematicScriptOwner owner,
        IReadOnlyList<CinematicScriptEventTrack>? decodedEventTracks,
        bool hasSceneEventKey, bool hasDecodedSceneEventTracks)
    {
        string trackState = hasDecodedSceneEventTracks
            ? $"{decodedEventTracks!.Count} linked SceneEvents track(s) were decoded."
            : hasSceneEventKey
                ? "The SceneEvents key is serialized, but no linked SceneEvents tracks were decoded for this owner."
                : "No valid SceneEvents EVE key is serialized on this owner.";
        return new("EVE scheduling", CinematicOwnerLifecycleEvidence.RuntimeUnknown,
            hasSceneEventKey
                ? $"SceneEvents EVE {owner.SceneEventsKey:X8} is linked. Actor Events key: {FormatKey(owner.EventsKey)}. {trackState}"
                : $"No SceneEvents EVE list is linked. Actor Events key: {FormatKey(owner.EventsKey)}. {trackState}",
            ["A linked/decoded EVE list is not proof that its owner entered the event table or its tracks are running.",
             "TRAPlayAll marks tracks running after the cinematic start path; EVE then advances eligible tracks. AI-function events may execute arbitrary script side effects."]);
    }

    private static bool HasResourceKey(uint key) => key is > 2 and < uint.MaxValue;
    private static string FormatKey(uint key) => HasResourceKey(key) ? $"{key:X8}" : "none";
}
