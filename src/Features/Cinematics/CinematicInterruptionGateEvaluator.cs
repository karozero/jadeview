using System.Globalization;

namespace JadeView;

/// <summary>
/// Reports the source-defined Cine_Events exit predicates at the current preview time.
/// It does not predict callback dispatch or the exact frame when Iris will stop tracks.
/// </summary>
internal static class CinematicInterruptionGateEvaluator
{
    private const int ZoneExit = 0x01;
    private const int ActivityExit = 0x04;
    private const int CapabilityExit = 0x08;
    private const int SavedFlagExit = 0x80;

    internal static CinematicInterruptionGateEvaluation Evaluate(CinematicScriptOwner owner,
        IEnumerable<SceneObject>? worldObjects,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? timelineCalls,
        float playhead)
    {
        int? exitMask = ReadInteger(owner, "i_tests_a_reussir_exit");
        if (!CinematicStartCallbackChain.IsReferenced(owner) && !HasAssignedStartCallback(owner))
            return new(null, CinematicStartGateStatus.Unknown, [],
                "This owner is not linked to the Cine_Events startup chain.");
        if (exitMask is null)
            return new(null, CinematicStartGateStatus.Unknown, [],
                "i_tests_a_reussir_exit is missing or invalid.");

        SceneObject[] objects = worldObjects?.ToArray() ?? [];
        var calls = timelineCalls?.ToArray() ?? [];
        var gates = new List<CinematicStartGateResult>();
        if ((exitMask.Value & ZoneExit) != 0)
            gates.Add(EvaluateCollisionExit(owner, objects));
        if ((exitMask.Value & ActivityExit) != 0)
            gates.Add(EvaluateActivityExit(owner, objects));
        if ((exitMask.Value & CapabilityExit) != 0)
            gates.Add(EvaluateCapabilityExit(owner, objects));
        if ((exitMask.Value & SavedFlagExit) != 0)
            gates.Add(EvaluateSavedFlagExit(owner, calls, timelineCalls != null, playhead));

        CinematicStartGateStatus status = gates.Count == 0
            ? CinematicStartGateStatus.Unknown
            : gates.Any(gate => gate.Status == CinematicStartGateStatus.Rejected)
                ? CinematicStartGateStatus.Rejected
                : gates.All(gate => gate.Status == CinematicStartGateStatus.Satisfied)
                    ? CinematicStartGateStatus.Satisfied
                    : CinematicStartGateStatus.Unknown;
        string scope = gates.Count == 0
            ? "No configured exit predicates are represented by the source mask. Priority interruption may still stop the owner."
            : "The callback clears active-owner bit 0x4000 only when all configured exit predicates pass, or when its priority branch marks this owner interrupted. This report evaluates predicates only; callback polling and priority state are not simulated.";
        return new(exitMask, status, gates.ToArray(), scope);
    }

    private static CinematicStartGateResult EvaluateCollisionExit(CinematicScriptOwner owner,
        SceneObject[] objects)
    {
        CinematicScriptObjectReference? detector = owner.EffectiveObjectReference("des_o_actor_detec_exit1");
        CinematicScriptObjectReference? zone = owner.EffectiveObjectReference("des_o_actor_zone_exit1");
        int? collisionType = ReadInteger(owner, "des_i_collision_type_exit1");
        string relation = collisionType switch
        {
            0 => "Iris requires the configured detector/zone collision to be absent",
            > 0 => "Iris requires the configured detector/zone collision to be present",
            _ => "the positive/inverse collision branch is unresolved because des_i_collision_type_exit1 is missing"
        };
        return Unknown(ZoneExit, "Exit collision",
            $"Iris compares {DescribeTarget(detector, objects, "des_o_actor_detec_exit1")} with "
            + $"{DescribeTarget(zone, objects, "des_o_actor_zone_exit1")}; {relation}. JadeView does not have the live collision result.",
            ["runtime collision result", "resolved exit detector and zone"]);
    }

    private static CinematicStartGateResult EvaluateActivityExit(CinematicScriptOwner owner,
        SceneObject[] objects)
    {
        CinematicScriptObjectReference? reference = owner.EffectiveObjectReference("des_o_actor_status_exit1");
        int? expectedActivity = ReadInteger(owner, "des_i_activity_exit1");
        if (reference == null || expectedActivity == null)
            return Unknown(ActivityExit, "Exit actor activity",
                "The target reference or des_i_activity_exit1 value is unavailable.",
                ["resolved exit activity target", "des_i_activity_exit1"]);

        SceneObject[] matches = objects.Where(sceneObject => sceneObject.GaoKey == reference.TargetKey)
            .Take(2).ToArray();
        if (matches.Length != 1)
            return Unknown(ActivityExit, "Exit actor activity",
                $"Target {reference.TargetKey:X8} is {(matches.Length == 0 ? "not loaded" : "ambiguous")}; "
                + $"Iris tests (flags2 & 1) == {expectedActivity.Value}.",
                ["unique target object", "runtime flags2 bit 0"]);

        return Unknown(ActivityExit, "Exit actor activity",
            $"Iris tests {matches[0].Name} ({reference.TargetKey:X8}): (runtime flags2 & 1) == {expectedActivity.Value}. "
            + "The preview does not observe this runtime activity bit.", ["runtime flags2 bit 0"]);
    }

    private static CinematicStartGateResult EvaluateCapabilityExit(CinematicScriptOwner owner,
        SceneObject[] objects)
    {
        CinematicScriptObjectReference? reference = owner.EffectiveObjectReference("des_o_actor_capa_exit1");
        int? requiredMask = ReadInteger(owner, "des_i_capa_exit1");
        if (reference == null || requiredMask == null)
            return Unknown(CapabilityExit, "Exit actor capabilities",
                "The target reference or des_i_capa_exit1 mask is unavailable.",
                ["resolved exit capability target", "des_i_capa_exit1"]);

        SceneObject[] matches = objects.Where(sceneObject => sceneObject.GaoKey == reference.TargetKey)
            .Take(2).ToArray();
        if (matches.Length != 1)
            return Unknown(CapabilityExit, "Exit actor capabilities",
                $"Target {reference.TargetKey:X8} is {(matches.Length == 0 ? "not loaded" : "ambiguous")}.",
                ["unique target object", "preview capability state"]);

        SceneObject target = matches[0];
        if (target.PreviewCapabilities is not uint capabilities)
            return Unknown(CapabilityExit, "Exit actor capabilities",
                $"Target {target.Name} ({reference.TargetKey:X8}) has no evaluated preview capability state.",
                ["preview capability state", "gameplay capability changes before callback polling"]);

        uint mask = unchecked((uint)requiredMask.Value);
        uint matched = capabilities & mask;
        bool satisfied = matched == mask;
        return new(CapabilityExit, "Exit actor capabilities",
            satisfied ? CinematicStartGateStatus.Satisfied : CinematicStartGateStatus.Rejected,
            CinematicStartGateEvidenceKind.PreviewObjectState,
            $"{target.Name} ({reference.TargetKey:X8}): (0x{capabilities:X8} & 0x{mask:X8}) = 0x{matched:X8}; "
            + $"Iris requires all configured bits 0x{mask:X8} to be set.",
            ["gameplay may change capabilities before the interruption callback"]);
    }

    private static CinematicStartGateResult EvaluateSavedFlagExit(CinematicScriptOwner owner,
        (float Time, CinematicLiteralCall Call)[] calls, bool timelineAvailable, float playhead)
    {
        int? encodedFlag = ReadInteger(owner, "des_i_SPFlag_test_exit1");
        if (encodedFlag is null)
            return Unknown(SavedFlagExit, "Exit saved flag",
                "des_i_SPFlag_test_exit1 is unavailable.", ["des_i_SPFlag_test_exit1"]);
        if (encodedFlag <= 0)
            return new(SavedFlagExit, "Exit saved flag", CinematicStartGateStatus.Rejected,
                CinematicStartGateEvidenceKind.PreviewTimeline,
                $"Iris treats des_i_SPFlag_test_exit1={encodedFlag.Value} as a failed exit predicate.", []);
        if (!timelineAvailable || !float.IsFinite(playhead))
            return Unknown(SavedFlagExit, "Exit saved flag",
                "The preview timeline or playhead is unavailable.", ["timeline writes through current playhead"]);

        bool requiredSet = encodedFlag.Value < 10000;
        int flagId = requiredSet ? encodedFlag.Value : encodedFlag.Value - 10000;
        if (flagId <= 0)
            return Unknown(SavedFlagExit, "Exit saved flag",
                $"des_i_SPFlag_test_exit1={encodedFlag.Value} does not encode a positive flag id.",
                ["valid saved-flag index"]);

        bool? state = ReadFlagState(calls, flagId, playhead, out bool ambiguous);
        if (ambiguous)
            return Unknown(SavedFlagExit, "Exit saved flag",
                $"Conflicting writes to saved flag {flagId} occur at the same timeline time.",
                ["ordered saved-flag writes"]);
        if (state is not { } current)
            return Unknown(SavedFlagExit, "Exit saved flag",
                $"Iris requires saved flag {flagId} {(requiredSet ? "set" : "clear")}; no known write establishes its state at the playhead.",
                [$"saved-game initial state for flag {flagId}"]);

        bool satisfied = current == requiredSet;
        return new(SavedFlagExit, "Exit saved flag",
            satisfied ? CinematicStartGateStatus.Satisfied : CinematicStartGateStatus.Rejected,
            CinematicStartGateEvidenceKind.PreviewTimeline,
            $"Timeline leaves flag {flagId} {(current ? "set" : "clear")}; Iris requires it {(requiredSet ? "set" : "clear")}.",
            [$"saved-game value before the first timeline write to flag {flagId}"]);
    }

    private static bool? ReadFlagState((float Time, CinematicLiteralCall Call)[] calls,
        int flagId, float playhead, out bool ambiguous)
    {
        ambiguous = false;
        var writes = calls.Where(item => float.IsFinite(item.Time) && item.Time <= playhead
                && item.Call.Arguments.Length > 0 && item.Call.Arguments[0] == (uint)flagId)
            .Select(item => (item.Time, State: CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) switch
            {
                "ARRGlobalIntFlagSet" => (bool?)true,
                "ARRGlobalIntFlagClear" => false,
                _ => null
            }))
            .Where(item => item.State != null)
            .GroupBy(item => item.Time)
            .OrderBy(group => group.Key);

        bool? state = null;
        foreach (var writesAtTime in writes)
        {
            bool[] states = writesAtTime.Select(item => item.State!.Value).Distinct().ToArray();
            if (states.Length != 1)
            {
                ambiguous = true;
                return null;
            }
            state = states[0];
        }
        return state;
    }

    private static CinematicStartGateResult Unknown(int mask, string name, string evidence,
        string[] missingInputs) => new(mask, name, CinematicStartGateStatus.Unknown,
            CinematicStartGateEvidenceKind.RuntimeInputUnavailable, evidence, missingInputs);

    private static string DescribeTarget(CinematicScriptObjectReference? reference,
        SceneObject[] objects, string variableName)
    {
        if (reference == null) return $"unresolved {variableName}";
        SceneObject[] matches = objects.Where(sceneObject => sceneObject.GaoKey == reference.TargetKey)
            .Take(2).ToArray();
        return matches.Length == 1
            ? $"{variableName} {matches[0].Name} ({reference.TargetKey:X8})"
            : $"{variableName} target {reference.TargetKey:X8} {(matches.Length == 0 ? "is not loaded" : "is ambiguous")}";
    }

    private static int? ReadInteger(CinematicScriptOwner owner, string name)
    {
        string? value = owner.EffectiveVariable(name)?.Value;
        if (value == null) return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int hexadecimal) ? hexadecimal : null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
            ? integer : null;
    }

    private static bool HasAssignedStartCallback(CinematicScriptOwner owner) =>
        CinematicStartCallbackChain.AssignedFunctions(owner).Length > 0;
}

internal sealed record CinematicInterruptionGateEvaluation(int? EnabledMask,
    CinematicStartGateStatus Status, CinematicStartGateResult[] Gates, string Scope);
