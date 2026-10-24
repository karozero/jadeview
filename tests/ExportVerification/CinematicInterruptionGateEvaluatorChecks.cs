using JadeView;

internal static class CinematicInterruptionGateEvaluatorChecks
{
    internal static void Verify()
    {
        VerifyCapabilityAndSavedFlagExitPredicates();
        VerifyRuntimeExitPredicatesRemainUnknown();
        VerifyOwnersWithoutExitPredicatesDoNotPredictAStop();
        Console.WriteLine("PASS: Cine_Events exit-predicate snapshots evaluate known state and preserve runtime-only uncertainty");
    }

    private static void VerifyCapabilityAndSavedFlagExitPredicates()
    {
        var actor = new SceneObject { GaoKey = 0x9E001234, Name = "exit actor", PreviewCapabilities = 0x20 };
        CinematicScriptOwner owner = Owner(0x88,
        [
            new("des_o_actor_capa_exit1", actor.GaoKey, "instance", actor.Name)
        ],
        [
            new("des_i_capa_exit1", PcVariableFormat.IntegerType, "instance", "32"),
            new("des_i_SPFlag_test_exit1", PcVariableFormat.IntegerType, "instance", "2166")
        ]);
        var calls = new (float Time, CinematicLiteralCall Call)[]
        {
            (2, new(0x100, 0x2916, [2166]))
        };

        CinematicInterruptionGateEvaluation satisfied = CinematicInterruptionGateEvaluator.Evaluate(
            owner, [actor], calls, 3);
        actor.PreviewCapabilities = 0;
        CinematicInterruptionGateEvaluation rejected = CinematicInterruptionGateEvaluator.Evaluate(
            owner, [actor], calls, 3);
        var restoredActor = new SceneObject
        {
            GaoKey = actor.GaoKey,
            Name = actor.Name,
            PreviewCapabilities = 0x20
        };
        CinematicInterruptionGateEvaluation beforeFlagWrite = CinematicInterruptionGateEvaluator.Evaluate(
            owner, [restoredActor], calls, 1);
        var conflictingCalls = calls.Append((2f, new CinematicLiteralCall(0x100, 0x2917, [2166]))).ToArray();
        CinematicInterruptionGateEvaluation conflictingFlagWrites = CinematicInterruptionGateEvaluator.Evaluate(
            owner, [restoredActor], conflictingCalls, 3);

        Require(satisfied.Status == CinematicStartGateStatus.Satisfied
            && satisfied.Gates.Length == 2
            && satisfied.Gates.All(gate => gate.Status == CinematicStartGateStatus.Satisfied)
            && rejected.Status == CinematicStartGateStatus.Rejected
            && rejected.Gates.Single(gate => gate.Mask == 0x08).Status == CinematicStartGateStatus.Rejected
            && beforeFlagWrite.Status == CinematicStartGateStatus.Unknown
            && beforeFlagWrite.Gates.Single(gate => gate.Mask == 0x80).RequiredInputs
                .Contains("saved-game initial state for flag 2166")
            && conflictingFlagWrites.Gates.Single(gate => gate.Mask == 0x80).Status
                == CinematicStartGateStatus.Unknown,
            "Exit gate snapshot should evaluate timeline saved-flag writes and current preview capabilities without guessing initial save state.");
    }

    private static void VerifyRuntimeExitPredicatesRemainUnknown()
    {
        var detector = new SceneObject { GaoKey = 0x100, Name = "exit detector" };
        var zone = new SceneObject { GaoKey = 0x200, Name = "exit zone" };
        var actor = new SceneObject { GaoKey = 0x300, Name = "activity actor" };
        CinematicScriptOwner owner = Owner(0x05,
        [
            new("des_o_actor_detec_exit1", detector.GaoKey, "instance", detector.Name),
            new("des_o_actor_zone_exit1", zone.GaoKey, "instance", zone.Name),
            new("des_o_actor_status_exit1", actor.GaoKey, "instance", actor.Name)
        ],
        [
            new("des_i_collision_type_exit1", PcVariableFormat.IntegerType, "instance", "1"),
            new("des_i_activity_exit1", PcVariableFormat.IntegerType, "instance", "1")
        ]);

        CinematicInterruptionGateEvaluation evaluation = CinematicInterruptionGateEvaluator.Evaluate(
            owner, [detector, zone, actor], [], 0);

        Require(evaluation.Status == CinematicStartGateStatus.Unknown
            && evaluation.Gates.Length == 2
            && evaluation.Gates.All(gate => gate.Status == CinematicStartGateStatus.Unknown)
            && evaluation.Gates.Single(gate => gate.Mask == 0x01).Evidence.Contains("requires the configured detector/zone collision to be present")
            && evaluation.Gates.Single(gate => gate.Mask == 0x04).Evidence.Contains("runtime flags2"),
            "Collision and activity exits must remain unknown without live collision and flags2 state.");
    }

    private static void VerifyOwnersWithoutExitPredicatesDoNotPredictAStop()
    {
        CinematicInterruptionGateEvaluation evaluation = CinematicInterruptionGateEvaluator.Evaluate(
            Owner(0), [], [], 0);
        Require(evaluation.EnabledMask == 0
            && evaluation.Status == CinematicStartGateStatus.Unknown
            && evaluation.Gates.Length == 0
            && evaluation.Scope.Contains("Priority interruption"),
            "No configured exit gates must not be treated as a passed exit or predicted stop.");
    }

    private static CinematicScriptOwner Owner(int exitMask,
        CinematicScriptObjectReference[]? references = null,
        CinematicScriptVariable[]? variables = null)
    {
        CinematicScriptVariable[] ownerVariables =
        [
            new("i_tests_a_reussir_exit", PcVariableFormat.IntegerType, "instance", exitMask.ToString()),
            .. (variables ?? [])
        ];
        return new(
            0x500, 0x600, 0x700, 0x800, 0x900, 0x100, "Cine_Events test owner",
            [CinematicStartCallbackChain.FirstInit], references ?? [], ownerVariables);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
