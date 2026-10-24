using JadeView;

internal static class CinematicStartGateEvaluatorChecks
{
    internal static void Verify()
    {
        VerifySharedGateCatalog();
        VerifySavedFlagGateUsesOnlyKnownTimelineWrites();
        VerifyCapabilityGateUsesEvaluatedPreviewState();
        VerifyActorActivityGateRemainsUnknown();
        VerifyCustomBitGateUsesSerializedAndTimelinePreviewState();
    }

    private static void VerifySharedGateCatalog()
    {
        var gates = CinematicStartGateCatalog.StartGates;
        Require(gates.Length == 10
            && gates.Select(gate => gate.Mask).Distinct().Count() == gates.Length
            && gates.Select(gate => gate.Variable).Distinct(StringComparer.Ordinal).Count() == gates.Length
            && gates.Single(gate => gate.Mask == CinematicStartGateCatalog.Message).Variable == "des_o_acteur_msg"
            && CinematicStartGateCatalog.InterruptionGates.All(gate =>
                gates.Any(startGate => startGate.Mask == gate.Mask)),
            "Cine_Events start and interruption predicates drifted from their shared mask catalog.");
    }

    private static void VerifySavedFlagGateUsesOnlyKnownTimelineWrites()
    {
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "128"),
             new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2166")]);
        (float Time, CinematicLiteralCall Call)[] writes =
        [
            (2, new(0x100, 0x2916, [2166]))
        ];

        CinematicStartGateEvaluation unknown = CinematicStartGateEvaluator.Evaluate(owner, [], [], 3);
        CinematicStartGateEvaluation satisfied = CinematicStartGateEvaluator.Evaluate(owner, [], writes, 3);
        CinematicStartGateEvaluation beforeWrite = CinematicStartGateEvaluator.Evaluate(owner, [], writes, 1);

        Require(unknown.Status == CinematicStartGateStatus.Unknown
            && unknown.Gates.Single().RequiredInputs.Contains("saved-game initial value for flag 2166")
            && satisfied.Status == CinematicStartGateStatus.Satisfied
            && satisfied.Gates.Single().EvidenceKind == CinematicStartGateEvidenceKind.PreviewTimeline
            && beforeWrite.Status == CinematicStartGateStatus.Unknown,
            "Saved-flag gates should become satisfied only when an unambiguous serialized write precedes the preview playhead.");
    }

    private static void VerifyCapabilityGateUsesEvaluatedPreviewState()
    {
        var actor = new SceneObject { GaoKey = 0x9E001234, Name = "capability actor", PreviewCapabilities = 0x5 };
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "8"),
             new("des_o_actor_capa1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001234"),
             new("des_i_capa_test1", PcVariableFormat.IntegerType, "instance", "5"),
             new("des_i_capa_etat1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_capa1", actor.GaoKey, "instance", actor.Name)]);

        CinematicStartGateEvaluation satisfied = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], 0);
        actor.PreviewCapabilities = 0;
        CinematicStartGateEvaluation rejected = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], 0);
        actor.PreviewCapabilities = null;
        CinematicStartGateEvaluation unknown = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], 0);

        Require(satisfied.Status == CinematicStartGateStatus.Satisfied
            && satisfied.Gates.Single().EvidenceKind == CinematicStartGateEvidenceKind.PreviewObjectState
            && rejected.Status == CinematicStartGateStatus.Rejected
            && unknown.Status == CinematicStartGateStatus.Unknown,
            "Capability gates should evaluate the preview-state snapshot and preserve unknown when that state is unavailable.");
    }

    private static void VerifyActorActivityGateRemainsUnknown()
    {
        var actor = new SceneObject
        {
            GaoKey = 0x9E001235,
            Name = "actor",
            Flags = (uint)(GameScriptEotTable.EventObjects | GameScriptEotTable.AiCallbacks),
            SerializedStateFlags = 0x04000000
        };
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("des_o_actor_status1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001235"),
             new("des_i_activity_test1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_status1", actor.GaoKey, "instance", actor.Name)]);

        CinematicStartGateEvaluation result = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], 0);
        CinematicStartGateResult gate = result.Gates.Single();
        GameScriptInitialActivationForecast forecast = gate.InitialActivationForecast!;
        Require(result.Status == CinematicStartGateStatus.Unknown
            && gate.Mask == 0x004
            && gate.RequiredInputs.Contains("target runtime flags2 bit 0 (activated-world state)")
            && gate.RequiredInputs.Contains("des_i_activity_test1")
            && gate.Evidence.Contains("(runtime flags2 & 0x1) == des_i_activity_test1 (1)")
            && forecast.SerializedStateFlags == 0x04000000
            && forecast.InitialRuntimeFlags2 == 0
            && forecast.Paths[0].Eligibility == GameScriptActivationEligibility.Eligible
            && forecast.Paths[1].Eligibility == GameScriptActivationEligibility.NotEligible
            && forecast.Paths[0].EotMembership!.Tables.HasFlag(GameScriptEotTable.EventObjects)
            && forecast.Paths[0].EotMembership!.Tables.HasFlag(GameScriptEotTable.AiCallbacks)
            && forecast.Paths[1].EotMembership == null
            && forecast.Paths[1].Reason.Contains("0x04000000")
            && forecast.Scope.Contains("not observed world membership"),
            "The raw 0x04000000 state bit must not be confused with runtime flags2; activity remains unknown and initial activation is forecast separately.");

        actor.SerializedStateFlags = 0x40000000;
        GameScriptInitialActivationForecast translatedStateForecast = CinematicStartGateEvaluator
            .Evaluate(owner, [actor], [], 0).Gates.Single().InitialActivationForecast!;
        Require(translatedStateForecast.InitialRuntimeFlags2 == 0x40400000
            && translatedStateForecast.Paths[0].Eligibility == GameScriptActivationEligibility.NotEligible,
            "Iris retains serialized 0x40000000 and adds runtime flags2 0x00400000, which blocks normal-time initial activation.");
    }

    private static void VerifyCustomBitGateUsesSerializedAndTimelinePreviewState()
    {
        var actor = new SceneObject
        {
            GaoKey = 0x9E001236,
            Name = "custom-bit actor",
            SerializedStateFlags = 0x00000100
        };
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "16"),
             new("des_o_actor_cbit1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001236"),
             new("des_i_cbit_test1", PcVariableFormat.IntegerType, "instance", "256"),
             new("des_i_cbit_etat1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_cbit1", actor.GaoKey, "instance", actor.Name)]);

        CinematicStartGateEvaluation initialState = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], 0);
        CinematicLiteralCall clearCustomBit = new(actor.GaoKey, 0x1FC3, [0, 0x100]);
        CinematicStartGateEvaluation afterWrite = CinematicStartGateEvaluator.Evaluate(owner, [actor],
            [(2, clearCustomBit)], 3);
        CinematicStartGateEvaluation beforeWrite = CinematicStartGateEvaluator.Evaluate(owner, [actor],
            [(2, clearCustomBit)], 1);
        CinematicStartGateEvaluation missingTimeline = CinematicStartGateEvaluator.Evaluate(owner, [actor], null, 3);
        CinematicStartGateEvaluation ambiguousWrites = CinematicStartGateEvaluator.Evaluate(owner, [actor],
            [(2, clearCustomBit), (2, new(actor.GaoKey, 0x1FC8, [0x100]))], 3);
        CinematicStartGateEvaluation malformedWrite = CinematicStartGateEvaluator.Evaluate(owner, [actor],
            [(2, new(actor.GaoKey, 0x1FC3, []))], 3);
        CinematicStartGateEvaluation invalidTime = CinematicStartGateEvaluator.Evaluate(owner, [actor], [], float.NaN);
        actor.SerializedStateFlags = 0;
        CinematicStartGateEvaluation afterSet = CinematicStartGateEvaluator.Evaluate(owner, [actor],
            [(2, new(actor.GaoKey, 0x1FC3, [0x100, 0]))], 3);

        Require(initialState.Status == CinematicStartGateStatus.Satisfied
            && initialState.Gates.Single().EvidenceKind == CinematicStartGateEvidenceKind.PreviewObjectState
            && afterWrite.Status == CinematicStartGateStatus.Rejected
            && beforeWrite.Status == CinematicStartGateStatus.Satisfied
            && afterSet.Status == CinematicStartGateStatus.Satisfied
            && missingTimeline.Status == CinematicStartGateStatus.Unknown
            && ambiguousWrites.Status == CinematicStartGateStatus.Unknown
            && malformedWrite.Status == CinematicStartGateStatus.Unknown
            && invalidTime.Status == CinematicStartGateStatus.Unknown
            && ambiguousWrites.Gates.Single().Evidence.Contains("does not establish their execution order"),
            "Custom-bit gates should evaluate flags2 bits 8–15 plus unambiguous prior timeline writes, preserving unknown when history or same-time order is unavailable.");
    }

    private static CinematicScriptOwner CreateCinematicOwner(CinematicScriptVariable[] variables,
        CinematicScriptObjectReference[]? references = null) => new(
            0x100, 0, 0x200, 0x300, 0, 0, "Cinematic owner",
            ["Cine_Events_cine_tests_depart"], references ?? [], variables);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
