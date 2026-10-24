using JadeView;

internal static class CinematicStartGateEvaluatorChecks
{
    internal static void Verify()
    {
        VerifySharedGateCatalog();
        VerifySavedFlagGateUsesOnlyKnownTimelineWrites();
        VerifyFirstInitAssignmentEntersTheEvaluatedStartChain();
        VerifyCapabilityGateUsesEvaluatedPreviewState();
        VerifyOwnerCapabilitySelectsPredicateBranch();
        VerifyZoneGatesExplainIrisCollisionBranchesButRemainUnknown();
        VerifyActorActivityGateRemainsUnknown();
        VerifyCustomBitGateUsesSerializedAndTimelinePreviewState();
        CinematicInterruptionGateEvaluatorChecks.Verify();
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

        SceneObject ownerState = CreateOwnerState(owner, 0);
        CinematicStartGateEvaluation unknown = CinematicStartGateEvaluator.Evaluate(owner, [ownerState], [], 3);
        CinematicStartGateEvaluation satisfied = CinematicStartGateEvaluator.Evaluate(owner, [ownerState], writes, 3);
        CinematicStartGateEvaluation beforeWrite = CinematicStartGateEvaluator.Evaluate(owner, [ownerState], writes, 1);

        Require(unknown.Status == CinematicStartGateStatus.Unknown
            && unknown.Gates.Single().RequiredInputs.Contains("saved-game initial value for flag 2166")
            && satisfied.Status == CinematicStartGateStatus.Satisfied
            && satisfied.Gates.Single().EvidenceKind == CinematicStartGateEvidenceKind.PreviewTimeline
            && beforeWrite.Status == CinematicStartGateStatus.Unknown,
            "Saved-flag gates should become satisfied only when an unambiguous serialized write precedes the preview playhead.");
    }

    private static void VerifyFirstInitAssignmentEntersTheEvaluatedStartChain()
    {
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "128"),
             new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2166")]) with
        {
            CallbackNames = [CinematicStartCallbackChain.FirstInit],
            CallbackSlots = [new(2, 0x26004C6C, CinematicStartCallbackChain.FirstInit)],
            ModelFunctionNames =
            [
                CinematicStartCallbackChain.FirstInit,
                CinematicStartCallbackChain.Initialize,
                CinematicStartCallbackChain.DepartureTest
            ]
        };
        SceneObject ownerState = CreateOwnerState(owner, 0);

        CinematicStartGateEvaluation evaluation = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState], [], 0);
        CinematicStartGateResult gate = evaluation.Gates.Single();

        Require(evaluation.Status == CinematicStartGateStatus.Unknown
            && gate.Status == CinematicStartGateStatus.Unknown
            && gate.RequiredInputs.Contains("saved-game initial value for flag 2166")
            && !gate.RequiredInputs.Any(input => input.Contains("callback-slot assignment", StringComparison.Ordinal))
            && !gate.Evidence.Contains("no effective owner callback slot", StringComparison.Ordinal),
            "An assigned Cine_Events firstinit callback must reach start-gate evaluation while preserving unknown save/runtime state.");
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

        SceneObject ownerState = CreateOwnerState(owner, 0);
        CinematicStartGateEvaluation satisfied = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor], [], 0);
        actor.PreviewCapabilities = 0;
        CinematicStartGateEvaluation rejected = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor], [], 0);
        actor.PreviewCapabilities = null;
        CinematicStartGateEvaluation unknown = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor], [], 0);

        Require(satisfied.Status == CinematicStartGateStatus.Satisfied
            && satisfied.Gates.Single().EvidenceKind == CinematicStartGateEvidenceKind.PreviewObjectState
            && rejected.Status == CinematicStartGateStatus.Rejected
            && unknown.Status == CinematicStartGateStatus.Unknown,
            "Capability gates should evaluate the preview-state snapshot and preserve unknown when that state is unavailable.");
    }

    private static void VerifyOwnerCapabilitySelectsPredicateBranch()
    {
        CinematicScriptOwner owner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "128"),
             new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2166")]);
        var ownerState = CreateOwnerState(owner, 0x100);

        CinematicStartGateEvaluation bypassed = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState], [], 0);
        ownerState.PreviewCapabilities = 0;
        CinematicStartGateEvaluation evaluated = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState], [], 0);
        ownerState.PreviewCapabilities = null;
        CinematicStartGateEvaluation unknownBranch = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState], [], 0);
        CinematicStartGateEvaluation missingOwner = CinematicStartGateEvaluator.Evaluate(owner,
            [], [], 0);
        CinematicStartGateEvaluation ambiguousOwner = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState, CreateOwnerState(owner, 0x100)], [], 0);

        Require(bypassed.Branch?.Status == CinematicStartGateBranchStatus.PredicatesBypassed
            && bypassed.Gates.Length == 0 && bypassed.Status == CinematicStartGateStatus.Unknown
            && bypassed.Branch.Evidence.Contains("skips the configured Cine_Events start predicates")
            && evaluated.Branch?.Status == CinematicStartGateBranchStatus.PredicatesEvaluated
            && evaluated.Gates.Length == 1 && evaluated.Status == CinematicStartGateStatus.Unknown
            && unknownBranch.Branch?.Status == CinematicStartGateBranchStatus.Unknown
            && unknownBranch.Status == CinematicStartGateStatus.Unknown
            && missingOwner.Branch?.Status == CinematicStartGateBranchStatus.Unknown
            && missingOwner.Branch.Evidence.Contains("is not present")
            && ambiguousOwner.Branch?.Status == CinematicStartGateBranchStatus.Unknown
            && ambiguousOwner.Branch.Evidence.Contains("is ambiguous"),
            "Iris's owner capability 0x100 must bypass configured Cine_Events predicates; missing owner state must keep the path unresolved.");
    }

    private static void VerifyZoneGatesExplainIrisCollisionBranchesButRemainUnknown()
    {
        var zoneOne = new SceneObject { GaoKey = 0x9E001241, Name = "zone one" };
        var detectorOne = new SceneObject { GaoKey = 0x9E001242, Name = "detector one" };
        CinematicScriptOwner modelFilteredOwner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_o_actor_zone1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001241"),
             new("des_o_actor_detec1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001242"),
             new("des_i_actor_type1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_zone1", zoneOne.GaoKey, "instance", zoneOne.Name),
             new("des_o_actor_detec1", detectorOne.GaoKey, "instance", detectorOne.Name)]);

        SceneObject ownerState = CreateOwnerState(modelFilteredOwner, 0);
        CinematicStartGateResult zoneOneResult = CinematicStartGateEvaluator.Evaluate(modelFilteredOwner,
            [ownerState, zoneOne, detectorOne], [], 0).Gates.Single();

        var zoneTwo = new SceneObject { GaoKey = 0x9E001243, Name = "zone two" };
        var detectorTwo = new SceneObject { GaoKey = 0x9E001244, Name = "detector two" };
        CinematicScriptOwner inverseOwner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "2"),
             new("des_o_actor_zone2", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001243"),
             new("des_o_actor_detec2", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001244"),
             new("des_i_collision_type2", PcVariableFormat.IntegerType, "instance", "0")],
            [new("des_o_actor_zone2", zoneTwo.GaoKey, "instance", zoneTwo.Name),
             new("des_o_actor_detec2", detectorTwo.GaoKey, "instance", detectorTwo.Name)]);
        SceneObject inverseOwnerState = CreateOwnerState(inverseOwner, 0);
        CinematicStartGateResult zoneTwoResult = CinematicStartGateEvaluator.Evaluate(inverseOwner,
            [inverseOwnerState, zoneTwo, detectorTwo], [], 0).Gates.Single();

        CinematicScriptOwner playerSlotOwner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_o_actor_zone1", PcVariableFormat.ObjectReferenceType, "instance", "1"),
             new("des_o_actor_detec1", PcVariableFormat.ObjectReferenceType, "instance", "2"),
             new("des_i_actor_type1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_zone1", 1, "instance", "AI_MainActor slot 1"),
             new("des_o_actor_detec1", 2, "instance", "AI_MainActor slot 2")]);
        SceneObject playerSlotOwnerState = CreateOwnerState(playerSlotOwner, 0);
        CinematicStartGateResult playerSlotResult = CinematicStartGateEvaluator.Evaluate(playerSlotOwner,
            [playerSlotOwnerState], [], 0).Gates.Single();

        CinematicScriptOwner sentinelOwner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_o_actor_zone1", PcVariableFormat.ObjectReferenceType, "instance", "0xFFFFFFFF"),
             new("des_o_actor_detec1", PcVariableFormat.ObjectReferenceType, "instance", "0"),
             new("des_i_actor_type1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_zone1", uint.MaxValue, "instance", "0xFFFFFFFF"),
             new("des_o_actor_detec1", 0, "instance", "0")]);
        SceneObject sentinelOwnerState = CreateOwnerState(sentinelOwner, 0);
        CinematicStartGateResult sentinelResult = CinematicStartGateEvaluator.Evaluate(sentinelOwner,
            [sentinelOwnerState], [], 0).Gates.Single();

        CinematicScriptOwner fallbackOwner = CreateCinematicOwner(
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_o_actor_zone1", PcVariableFormat.ObjectReferenceType, "instance", "0"),
             new("des_o_actor_detec1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001245"),
             new("des_i_actor_type1", PcVariableFormat.IntegerType, "instance", "1")],
            [new("des_o_actor_zone1", 0, "instance", null),
             new("des_o_actor_detec1", 0x9E001245, "instance", "detector")]);
        var fallbackDetector = new SceneObject { GaoKey = 0x9E001245, Name = "detector" };
        SceneObject fallbackOwnerState = CreateOwnerState(fallbackOwner, 0);
        CinematicStartGateResult fallbackResult = CinematicStartGateEvaluator.Evaluate(fallbackOwner,
            [fallbackOwnerState, fallbackDetector], [], 0).Gates.Single();

        Require(zoneOneResult.Status == CinematicStartGateStatus.Unknown
            && zoneOneResult.Evidence.Contains("collision detection list")
            && zoneOneResult.Evidence.Contains("object mask 0x20000")
            && zoneOneResult.Evidence.Contains("first configured model filter")
            && zoneOneResult.RequiredInputs.Contains("runtime collision detection-list contents on zone 1")
            && zoneTwoResult.Status == CinematicStartGateStatus.Unknown
            && zoneTwoResult.Evidence.Contains("COLZDEPivotCollide")
            && zoneTwoResult.Evidence.Contains("COLBVObjectObject")
            && zoneTwoResult.Evidence.Contains("inverts the selected zone collision result")
            && zoneTwoResult.RequiredInputs.Contains("runtime identity flag 0x400 on the zone")
            && playerSlotResult.Evidence.Contains("AI_MainActor slot 1")
            && playerSlotResult.Evidence.Contains("AI_MainActor slot 2")
            && playerSlotResult.Evidence.Contains("if that runtime slot is empty, Iris assigns current owner")
            && playerSlotResult.RequiredInputs.Contains("runtime AI_MainActor[0] binding (AI_MainActor slot 1) for zone 1")
            && playerSlotResult.RequiredInputs.Contains("runtime AI_MainActor[1] binding (AI_MainActor slot 2) for des_o_actor_detec1")
            && sentinelResult.Evidence.Contains("serialized key 0xFFFFFFFF")
            && sentinelResult.RequiredInputs.Contains("field-specific interpretation of 0xFFFFFFFF zone 1 reference"),
            "Zone-gate evidence should expose Iris's configured collision branch while refusing to infer live overlaps from static scene data.");

        Require(fallbackResult.Evidence.Contains("implicit current-owner zone")
            && fallbackResult.Evidence.Contains("assigns the current owner as zone 1")
            && fallbackResult.RequiredInputs.Contains("runtime collision detection-list contents on zone 1")
            && fallbackResult.RequiredInputs.Contains("runtime model identity for overlapped objects"),
            "A null zone1 reference with a nonzero actor type must describe Iris's current-owner fallback while keeping overlap results unknown.");
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

        SceneObject ownerState = CreateOwnerState(owner, 0);
        CinematicStartGateEvaluation result = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState, actor], [], 0);
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
            .Evaluate(owner, [ownerState, actor], [], 0).Gates.Single().InitialActivationForecast!;
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

        SceneObject ownerState = CreateOwnerState(owner, 0);
        CinematicStartGateEvaluation initialState = CinematicStartGateEvaluator.Evaluate(owner,
            [ownerState, actor], [], 0);
        CinematicLiteralCall clearCustomBit = new(actor.GaoKey, 0x1FC3, [0, 0x100]);
        CinematicStartGateEvaluation afterWrite = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor],
            [(2, clearCustomBit)], 3);
        CinematicStartGateEvaluation beforeWrite = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor],
            [(2, clearCustomBit)], 1);
        CinematicStartGateEvaluation missingTimeline = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor], null, 3);
        CinematicStartGateEvaluation ambiguousWrites = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor],
            [(2, clearCustomBit), (2, new(actor.GaoKey, 0x1FC8, [0x100]))], 3);
        CinematicStartGateEvaluation malformedWrite = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor],
            [(2, new(actor.GaoKey, 0x1FC3, []))], 3);
        CinematicStartGateEvaluation invalidTime = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor], [], float.NaN);
        actor.SerializedStateFlags = 0;
        CinematicStartGateEvaluation afterSet = CinematicStartGateEvaluator.Evaluate(owner, [ownerState, actor],
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

    private static SceneObject CreateOwnerState(CinematicScriptOwner owner, uint? capabilities) =>
        new() { GaoKey = owner.OwnerKey, Name = owner.Name, PreviewCapabilities = capabilities };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
