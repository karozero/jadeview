using System.Globalization;

namespace JadeView;

/// <summary>
/// Evaluates only Cine_Events start predicates for which JadeView has an explicit preview-state
/// source. Runtime activation, scheduler state, collision, and saved-game state remain unknown.
/// </summary>
internal static partial class CinematicStartGateEvaluator
{
    private const string StartCallback = "Cine_Events_cine_tests_depart";
    internal static CinematicStartGateEvaluation Evaluate(CinematicScriptOwner owner,
        IEnumerable<SceneObject>? worldObjects,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? timelineCalls,
        float playhead)
    {
        bool callbackAssigned = owner.CallbackNames.Contains(StartCallback, StringComparer.Ordinal);
        bool callbackReferenced = callbackAssigned
            || owner.ModelFunctionNames.Contains(StartCallback, StringComparer.Ordinal);
        if (!callbackReferenced)
            return new(0, CinematicStartGateStatus.Unknown, [],
                "No Cine_Events start callback is linked to this owner.");

        var enabledGates = CinematicStartGateCatalog.StartGates
            .Where(gate => IsEnabled(owner, gate.Mask, gate.Variable)).ToArray();
        bool timelineCallsAvailable = timelineCalls != null;
        var calls = timelineCalls?.ToArray() ?? [];
        var results = enabledGates.Select(gate => gate.Mask switch
        {
            CinematicStartGateCatalog.Activity => EvaluateActivityGate(owner, worldObjects, gate),
            CinematicStartGateCatalog.Capability => EvaluateCapabilityGate(owner, worldObjects, gate),
            CinematicStartGateCatalog.CustomBits => EvaluateCustomBitGate(owner, worldObjects, calls, timelineCallsAvailable, playhead, gate),
            CinematicStartGateCatalog.SpecialFlag1 or CinematicStartGateCatalog.SpecialFlag2 => EvaluateSavedFlagGate(owner, calls, playhead, gate),
            _ => Unknown(gate, "The source gate is configured, but its runtime input is not represented "
                + "by the cinematic preview state.", gate.MissingInputs)
        }).ToArray();

        if (!callbackAssigned && results.Length > 0)
        {
            results = results.Select(result => result with
            {
                Status = CinematicStartGateStatus.Unknown,
                EvidenceKind = CinematicStartGateEvidenceKind.RuntimeInputUnavailable,
                Evidence = "The script model references Cine_Events_cine_tests_depart, but no effective owner "
                    + "callback slot is decoded. " + result.Evidence,
                RequiredInputs = [.. result.RequiredInputs, "effective owner callback-slot assignment"]
            }).ToArray();
        }

        CinematicStartGateStatus overallStatus = results.Length == 0
            ? CinematicStartGateStatus.Unknown
            : results.Any(result => result.Status == CinematicStartGateStatus.Rejected)
            ? CinematicStartGateStatus.Rejected
            : results.All(result => result.Status == CinematicStartGateStatus.Satisfied)
                ? CinematicStartGateStatus.Satisfied
                : CinematicStartGateStatus.Unknown;
        return new(EnabledMask(results), overallStatus, results,
            "Preview evidence only. This is not proof that the game instantiated the owner or dispatched its callback.");
    }

    private static CinematicStartGateResult EvaluateActivityGate(CinematicScriptOwner owner,
        IEnumerable<SceneObject>? worldObjects,
        CinematicStartGateDefinition gate)
    {
        int? expectedActivity = ReadInteger(owner, "des_i_activity_test1");
        CinematicScriptObjectReference? actorReference = owner.EffectiveObjectReference(gate.Variable);
        if (expectedActivity is null || actorReference is null)
            return Unknown(gate, "Iris compares the target's runtime flags2 bit 0 with des_i_activity_test1, "
                + "but the predicate metadata or target reference is unavailable.",
                [.. gate.MissingInputs, "resolved des_o_actor_status1 target"]);

        SceneObject[] matches = worldObjects?
            .Where(sceneObject => sceneObject.GaoKey == actorReference.TargetKey)
            .Take(2)
            .ToArray() ?? [];
        if (matches.Length != 1)
        {
            string targetReason = matches.Length == 0 ? "is not loaded in this world" : "is ambiguous in this world";
            return Unknown(gate, $"Activity target {actorReference.TargetKey:X8} {targetReason}; the runtime "
                + $"predicate is (flags2 & 1) == {expectedActivity.Value}.",
                [.. gate.MissingInputs, "unique target object"]);
        }

        SceneObject target = matches[0];
        return Unknown(gate,
            $"Iris tests {target.Name} ({target.GaoKey:X8}): (runtime flags2 & 0x1) "
                + $"== des_i_activity_test1 ({expectedActivity.Value}). JadeView has no observed runtime flags2 bit.",
            gate.MissingInputs) with
        {
            InitialActivationForecast = GameScriptInitialActivationForecaster.Evaluate(
                target.Flags, target.SerializedStateFlags)
        };
    }

    private static CinematicStartGateResult EvaluateCapabilityGate(CinematicScriptOwner owner,
        IEnumerable<SceneObject>? worldObjects,
        CinematicStartGateDefinition gate)
    {
        int? testedMask = ReadInteger(owner, "des_i_capa_test1");
        int? expectedState = ReadInteger(owner, "des_i_capa_etat1");
        CinematicScriptObjectReference? actorReference = owner.EffectiveObjectReference(gate.Variable);
        if (testedMask is null || expectedState is null || actorReference is null)
            return Unknown(gate, "Capability predicate metadata is incomplete.",
                ["des_i_capa_test1", "des_i_capa_etat1", gate.Variable + " target"]);

        SceneObject[] matches = worldObjects?
            .Where(sceneObject => sceneObject.GaoKey == actorReference.TargetKey)
            .Take(2)
            .ToArray() ?? [];
        if (matches.Length != 1)
        {
            string targetReason = matches.Length == 0 ? "target is not loaded in this world" : "target is ambiguous";
            return Unknown(gate, $"Capability target {actorReference.TargetKey:X8} {targetReason}.",
                ["unique target object", "evaluated preview capabilities"]);
        }

        SceneObject target = matches[0];
        if (target.PreviewCapabilities is not uint capabilities)
            return Unknown(gate, $"Target {target.Name} ({target.GaoKey:X8}) has no evaluated preview capability state.",
                ["current cinematic preview capability state"]);

        int matchedBits = unchecked((int)capabilities) & testedMask.Value;
        int expectedBits = unchecked(expectedState.Value * testedMask.Value);
        bool passes = matchedBits == expectedBits;
        return new(gate.Mask, gate.Label,
            passes ? CinematicStartGateStatus.Satisfied : CinematicStartGateStatus.Rejected,
            CinematicStartGateEvidenceKind.PreviewObjectState,
            $"{target.Name} ({target.GaoKey:X8}): (0x{capabilities:X8} & 0x{testedMask.Value:X8}) "
                + $"= 0x{matchedBits:X8}; expected 0x{expectedBits:X8}.",
            ["gameplay may have changed capabilities before the callback"]);
    }

    private static CinematicStartGateResult EvaluateSavedFlagGate(CinematicScriptOwner owner,
        (float Time, CinematicLiteralCall Call)[] timelineCalls,
        float playhead,
        CinematicStartGateDefinition gate)
    {
        int? encodedFlag = ReadInteger(owner, gate.Variable);
        if (encodedFlag is null)
            return Unknown(gate, $"{gate.Variable} is unavailable although the gate bit is enabled.",
                [gate.Variable]);
        if (encodedFlag < 0)
            return new(gate.Mask, gate.Label, CinematicStartGateStatus.Rejected,
                CinematicStartGateEvidenceKind.PreviewTimeline,
                $"Source callback rejects {gate.Variable}={encodedFlag} when this test is enabled.", []);

        bool requiredSet = encodedFlag <= 10000;
        int flagId = requiredSet ? encodedFlag.Value : encodedFlag.Value - 10000;
        if (flagId <= 0)
            return Unknown(gate, $"{gate.Variable}={encodedFlag} does not encode a positive saved flag.",
                ["valid saved-flag index"]);

        bool? currentState = ReadTimelineFlagState(timelineCalls, flagId, playhead);
        if (currentState is null)
            return Unknown(gate,
                $"Saved flag {flagId} must be {(requiredSet ? "set" : "clear")}; no unambiguous write "
                    + "to this flag is present at or before the current preview time.",
                [$"saved-game initial value for flag {flagId}"]);

        bool satisfied = currentState.Value == requiredSet;
        return new(gate.Mask, gate.Label,
            satisfied ? CinematicStartGateStatus.Satisfied : CinematicStartGateStatus.Rejected,
            CinematicStartGateEvidenceKind.PreviewTimeline,
            $"Serialized preview timeline leaves flag {flagId} {(currentState.Value ? "set" : "clear")}; "
                + $"the owner requires it {(requiredSet ? "set" : "clear")}.",
            ["saved-game value before the first timeline write"]);
    }

    private static bool? ReadTimelineFlagState(
        (float Time, CinematicLiteralCall Call)[] calls, int flagId, float playhead)
    {
        var writes = calls
            .Where(item => float.IsFinite(item.Time) && item.Time <= playhead
                && item.Call.Arguments.Length > 0 && item.Call.Arguments[0] == (uint)flagId)
            .Select(item => (item.Time, IsSet: CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) switch
            {
                "ARRGlobalIntFlagSet" => true,
                "ARRGlobalIntFlagClear" => false,
                _ => (bool?)null
            }))
            .Where(item => item.IsSet != null)
            .GroupBy(item => item.Time)
            .OrderBy(group => group.Key)
            .ToArray();

        bool? state = null;
        foreach (var writesAtTime in writes)
        {
            bool[] values = writesAtTime.Select(item => item.IsSet!.Value).Distinct().ToArray();
            state = values.Length == 1 ? values[0] : null;
        }
        return state;
    }

    private static CinematicStartGateResult Unknown(
        CinematicStartGateDefinition gate,
        string evidence,
        string[] missingInputs) => new(gate.Mask, gate.Label,
            CinematicStartGateStatus.Unknown,
            CinematicStartGateEvidenceKind.RuntimeInputUnavailable,
            evidence,
            missingInputs);

    private static bool IsEnabled(CinematicScriptOwner owner, int mask, string variableName)
    {
        if (mask >= CinematicStartGateCatalog.SpecialFlag1)
            return ReadInteger(owner, variableName) is not 0 and not null;
        uint? reference = ReadUnsigned(owner, variableName);
        if (reference is > 0 and < uint.MaxValue) return true;
        return mask == CinematicStartGateCatalog.Zone1
            && ReadInteger(owner, "des_i_actor_type1") is not 0 and not null;
    }

    private static int EnabledMask(CinematicStartGateResult[] results) =>
        results.Aggregate(0, (mask, result) => mask | result.Mask);

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

    private static uint? ReadUnsigned(CinematicScriptOwner owner, string name)
    {
        string? value = owner.EffectiveVariable(name)?.Value;
        if (value == null) return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint hexadecimal) ? hexadecimal : null;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint integer)
            ? integer : null;
    }
}
