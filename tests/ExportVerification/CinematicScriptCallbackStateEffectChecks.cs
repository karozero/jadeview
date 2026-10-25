using JadeView;

internal static class CinematicScriptCallbackStateEffectChecks
{
    internal static void Verify()
    {
        var branch = new CinematicScriptBranchConditionSource("if", "should_hide == 1", 88);
        var calls = new CinematicScriptReachableEngineCall[]
        {
            Call("OBJ_FlagInvisibleSet", ["should_hide"], [branch], 92),
            new CinematicScriptReachableEngineCall("test_callback", "OBJ_FlagInvisibleSet", "C",
                ["AI_gpst_CurrentGameObject", "1"], [], [], [], [], "test_script.c", 96,
                "callback[2] -> test_callback"),
            Call("OBJ_FlagInactiveSet", ["0"], [], 101),
            new CinematicScriptReachableEngineCall("test_callback", "OBJ_CapaSet", "CURRENT",
                ["0x100", "0"], [], [], [], [], "test_script.c", 109,
                "callback[2] -> test_callback"),
            new CinematicScriptReachableEngineCall("test_callback", "OBJ_CapaSet", "C",
                ["(tGameObjectMain*)test_callback_target", "0x20", "0x10"], [], [], [], [],
                "test_script.c", 114, "callback[2] -> test_callback"),
            new CinematicScriptReachableEngineCall("test_callback", "OBJSetPos", "C",
                ["(tGameObjectMain*)test_callback_target", "1.0f", "2.0f", "3.0f"], [], [], [], [],
                "test_script.c", 117, "callback[2] -> test_callback"),
            new CinematicScriptReachableEngineCall("test_callback", "OBJ_HierarchySet", "C",
                ["(tGameObjectMain*)test_callback_target", "AI_gpst_CurrentGameObject"], [], [], [], [],
                "test_script.c", 119, "callback[2] -> test_callback"),
            Call("OBJDestroy", [], [], 120),
            new CinematicScriptReachableEngineCall("test_callback", "OBJ_FlagInvisibleSet", "C",
                ["linkedReceiver", "1"],
                [new("linkedReceiver", "(tGameObjectMain*)v1->linked", 121, 1)],
                [], [], [], "test_script.c", 121, "callback[2] -> test_callback"),
            new CinematicScriptReachableEngineCall("test_callback", "OBJDrawMaskSet", "CURRENT",
                ["8", "0"], [], [], [], [], "test_script.c", 122,
                "callback[2] -> test_callback"),
            new CinematicScriptReachableEngineCall("test_callback", "COLSetActivationSet", "C",
                ["(tGameObjectMain*)test_callback_target", "0x4", "0x2"], [], [], [], [],
                "test_script.c", 123, "callback[2] -> test_callback")
        };
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "owner", [], [], [])
        {
            ObjectReferences = [new("linked", 0x87654321, "instance", "linked object")]
        };
        var sourceReferences = new[]
        {
            new CinematicScriptReachableSourceReference("test_callback", "Object", "target",
                0x12345678, "test_script.c", 80, "callback[2] -> test_callback")
        };

        CinematicScriptCallbackStateEffect[] effects =
            CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner, calls, sourceReferences);

        Check(effects.Length == 11, "The callback effect projection included unrelated engine APIs or omitted a supported state mutation.");
        Check(effects[0].OwnerKey == owner.OwnerKey
            && effects[0].Api == "OBJ_FlagInvisibleSet"
            && effects[0].Arguments.SequenceEqual(["should_hide"])
            && effects[0].BranchConditions.SequenceEqual([branch])
            && effects[0].ExecutionStatus == "SourceCallCandidate",
            "The invisibility candidate lost its owner, source arguments, branch, or unknown execution status.");
        Check(effects[1].Api == "OBJ_FlagInvisibleSet"
            && effects[1].ReceiverKey == owner.OwnerKey
            && effects[1].ReceiverEvidence.Contains("AI_gpst_CurrentGameObject", StringComparison.Ordinal),
            "A source call that explicitly passes AI_gpst_CurrentGameObject must resolve to the callback owner.");
        Check(effects[2].Api == "OBJ_FlagInactiveSet"
            && effects[2].BranchConditions.Length == 0,
            "The inactive candidate did not preserve an unguarded source call.");
        Check(effects[3].Api == "OBJ_CapaSet"
            && effects[3].StateEffect.Contains("(current & ~remove) | set", StringComparison.Ordinal)
            && effects[3].Variant == "CURRENT"
            && effects[3].Arguments.SequenceEqual(["0x100", "0"])
            && effects[3].ReceiverKey == owner.OwnerKey,
            "The CURRENT capability candidate must preserve Iris set/remove semantics, owner receiver, and source arguments.");
        Check(effects[4].Api == "OBJ_CapaSet" && effects[4].Variant == "C"
            && effects[4].ReceiverKey == 0x12345678
            && effects[4].ReceiverEvidence.Contains("test_script.c:80", StringComparison.Ordinal),
            "A direct capability call must resolve its receiver only from a unique source pointer reference.");
        Check(effects[5].Api == "OBJSetPos" && effects[5].ReceiverKey == 0x12345678
            && effects[5].StateEffect.Contains("absolute position", StringComparison.Ordinal),
            "A source-confirmed position mutation must resolve its direct object receiver and retain the transform effect.");
        Check(effects[6].Api == "OBJ_HierarchySet" && effects[6].ReceiverKey == 0x12345678
            && effects[6].ReceiverEvidence.Contains("test_script.c:80", StringComparison.Ordinal),
            "A hierarchy call must bind its receiver from the first pointer argument and ignore a current-object parent argument.");
        Check(effects[7].Api == "OBJDestroy" && effects[7].ReceiverKey == null
            && effects[7].StateEffect.Contains("authored objects become invisible/inactive", StringComparison.Ordinal)
            && effects[7].StateEffect.Contains("generated objects may be detached and removed", StringComparison.Ordinal),
            "OBJDestroy must retain its two source-confirmed lifecycle outcomes without inventing the selected target.");
        Check(effects[8].Api == "OBJ_FlagInvisibleSet" && effects[8].ReceiverKey == 0x87654321
            && effects[8].ReceiverEvidence.Contains("Serialized OVA object reference linked", StringComparison.Ordinal),
            "A local receiver alias rooted in a serialized OVA object reference should resolve to that object.");
        Check(effects[9].Api == "OBJDrawMaskSet"
            && effects[9].StateEffect.Contains("visual flag 0x4000", StringComparison.Ordinal)
            && effects[9].ReceiverKey == owner.OwnerKey,
            "A current-owner draw-mask callback must preserve the source visual-object guard and receiver identity.");
        Check(effects[10].Api == "COLSetActivationSet" && effects[10].Variant == "C"
            && effects[10].ReceiverKey == 0x12345678
            && effects[10].StateEffect.Contains("collision-instance activation mask", StringComparison.Ordinal),
            "A collision-instance activation callback must resolve its explicit receiver and retain source set/clear semantics.");
        var identityGuardedCall = Call("OBJ_FlagInactiveSet", ["1"],
        [new("if", "AI_EvalFunc_OBJ_FlagsIdentityTest_C(AI_gpst_CurrentGameObject, 0x20)", 122)], 123);
        CinematicScriptCallbackStateEffect guardedEffect = CinematicScriptCallbackStateEffectAnalyzer.Analyze(
            owner with { SerializedGaoFlags = 0x20 }, [identityGuardedCall], [])[0];
        Check(guardedEffect.SerializedBranchFindings.Length == 1
            && guardedEffect.SerializedBranchFindings[0].Contains("if branch true", StringComparison.Ordinal)
            && guardedEffect.SerializedBranchFindings[0].Contains("0x00000020", StringComparison.Ordinal),
            "Callback state-effect candidates should carry recognized serialized identity-predicate evidence.");
        var conditionalReceiverCall = new CinematicScriptReachableEngineCall("test_callback",
            "OBJ_FlagInvisibleSet", "C", ["receiver"], [], [], [], [], "test_script.c", 118,
            "callback[2] -> test_callback",
            ArgumentAssignmentAlternatives: [new("receiver", "AI_gpst_CurrentGameObject", 118, [branch])]);
        Check(!CinematicScriptArgumentBindingResolver.FirstArgumentUsesCurrentGameObject(conditionalReceiverCall)
            && CinematicScriptArgumentBindingResolver.ResolveFirstArgumentObjectReferences(
                conditionalReceiverCall, sourceReferences).Length == 0,
            "A branch-dependent receiver alternative must remain unresolved instead of being attributed to the owner.");

        bool capabilitySetApplied = CinematicScriptCallbackPlayback.TryApplyCapabilitySet(
            0x0030, ["0x0100", "0x0020"], out uint capabilitiesAfterSet);
        bool signedCapabilitySetApplied = CinematicScriptCallbackPlayback.TryApplyCapabilitySet(
            0x0000, ["-1", "0"], out uint signedCapabilitiesAfterSet);
        bool invalidCapabilitySetApplied = CinematicScriptCallbackPlayback.TryApplyCapabilitySet(
            0x0030, ["dynamic_mask", "0"], out _);
        bool receiverCapabilitySetApplied = CinematicScriptCallbackPlayback.TryApplyCapabilitySet(
            0x0030, CinematicScriptCallbackPlayback.MutationArguments(effects[4]), out uint receiverCapabilitiesAfterSet);
        bool implicitReceiverCapabilitySetApplied = CinematicScriptCallbackPlayback.TryApplyCapabilitySet(
            0x0030, CinematicScriptCallbackPlayback.MutationArguments(effects[3]), out uint implicitReceiverCapabilities);
        CinematicScriptCallbackStateEffect explicitCurrentPointer = effects[1] with
        {
            Variant = string.Empty,
            Arguments = ["AI_gpst_CurrentGameObject", "1"]
        };
        Check(capabilitySetApplied && capabilitiesAfterSet == 0x0110
            && signedCapabilitySetApplied && signedCapabilitiesAfterSet == ushort.MaxValue
            && !invalidCapabilitySetApplied && receiverCapabilitySetApplied
            && receiverCapabilitiesAfterSet == 0x0020 && implicitReceiverCapabilitySetApplied
            && implicitReceiverCapabilities == 0x0130
            && CinematicScriptCallbackPlayback.MutationArguments(explicitCurrentPointer)
                .SequenceEqual(["1"]),
            "Callback OBJ_CapaSet preview must distinguish implicit CURRENT receivers from explicit receiver arguments and preserve Iris's 16-bit set/clear semantics.");

        bool drawMaskHideApplied = CinematicScriptCallbackPlayback.TryApplyDrawMask(
            0x0108, ["0", "8"], out uint hiddenDrawMask);
        bool drawMaskShowApplied = CinematicScriptCallbackPlayback.TryApplyDrawMask(
            hiddenDrawMask, ["8", "0"], out uint shownDrawMask);
        bool invalidDrawMaskApplied = CinematicScriptCallbackPlayback.TryApplyDrawMask(
            0x0108, ["runtime_mask", "0"], out _);
        Check(drawMaskHideApplied && hiddenDrawMask == 0x0100
            && drawMaskShowApplied && shownDrawMask == 0x0108
            && !invalidDrawMaskApplied,
            "Callback OBJDrawMaskSet preview must apply source set/remove mask semantics and leave dynamic operands unresolved.");
        CinematicScriptCallbackStateEffect explicitDrawMask = effects[9] with
        {
            Variant = "C",
            Arguments = ["receiver", "8", "0"],
            ReceiverEvidence = "Source pointer target resolves as the callback receiver."
        };
        bool receiverDrawMaskApplied = CinematicScriptCallbackPlayback.TryApplyDrawMask(
            0x0100, CinematicScriptCallbackPlayback.MutationArguments(explicitDrawMask),
            out uint receiverDrawMask);
        CinematicScriptCallbackStateEffect unknownVariant = explicitDrawMask with
        {
            Variant = "unknown",
            Arguments = ["receiver", "8", "0"]
        };
        Check(receiverDrawMaskApplied && receiverDrawMask == 0x0108
            && CinematicScriptCallbackPlayback.MutationArguments(unknownVariant)
                .SequenceEqual(["8", "0"]),
            "Callback state mutations must use resolved receiver evidence rather than guessing from an API variant label.");

        bool collisionActivationApplied = CinematicScriptCallbackPlayback.TryApplyCollisionActivationSet(
            0xFF02, CinematicScriptCallbackPlayback.MutationArguments(effects[10]),
            out ushort collisionActivationMask);
        bool signedCollisionActivationApplied = CinematicScriptCallbackPlayback.TryApplyCollisionActivationSet(
            0, ["-1", "0"], out ushort signedCollisionActivationMask);
        bool invalidCollisionActivationApplied = CinematicScriptCallbackPlayback.TryApplyCollisionActivationSet(
            0x0002, ["runtime_mask", "0"], out _);
        Check(collisionActivationApplied && collisionActivationMask == 0xFF04
            && signedCollisionActivationApplied && signedCollisionActivationMask == ushort.MaxValue
            && !invalidCollisionActivationApplied,
            "COLSetActivationSet callback preview must preserve the 16-bit (current & ~clear) | set behavior and leave dynamic masks unresolved.");
        var visualTargetOwner = new CinematicScriptOwner(0x87654321, 0x200, 0x300,
            0x400, 0x500, 0x600, "visual target", [], [], [])
        {
            SerializedGaoFlags = 0x4000
        };
        var nonVisualTargetOwner = visualTargetOwner with { SerializedGaoFlags = 0 };
        Check(CinematicScriptCallbackPlayback.HasResolvedVisualDrawMaskSupport(
                visualTargetOwner.OwnerKey, new Dictionary<uint, CinematicScriptOwner[]>
                {
                    [visualTargetOwner.OwnerKey] = [visualTargetOwner]
                })
            && !CinematicScriptCallbackPlayback.HasResolvedVisualDrawMaskSupport(
                visualTargetOwner.OwnerKey, new Dictionary<uint, CinematicScriptOwner[]>
                {
                    [visualTargetOwner.OwnerKey] = [nonVisualTargetOwner]
                })
            && !CinematicScriptCallbackPlayback.HasResolvedVisualDrawMaskSupport(
                visualTargetOwner.OwnerKey, new Dictionary<uint, CinematicScriptOwner[]>
                {
                    [visualTargetOwner.OwnerKey] = [visualTargetOwner, visualTargetOwner with { WorldKey = 0x401 }]
                }),
            "Callback OBJDrawMaskSet preview must require one target with the serialized Iris visual-object guard.");

        bool equivalentVisibility = CinematicScriptCallbackPlayback.TryResolveEquivalentVisibility(
            CinematicObjectState.Invisible,
            CinematicObjectState.Invisible | CinematicObjectState.Inactive,
            out bool hiddenInBothBranches);
        bool divergentVisibility = CinematicScriptCallbackPlayback.TryResolveEquivalentVisibility(
            CinematicObjectState.Invisible, 0, out _);
        Check(equivalentVisibility && !hiddenInBothBranches && !divergentVisibility,
            "Unknown saved-flag branches may collapse to a visual state only when both branches agree on visibility; unrelated lifecycle bits must not prevent that visual consensus.");

        Console.WriteLine("PASS: callback state-effect preview retains source guards and resolves visibility, capability, draw-mask and collision activation changes");

        static CinematicScriptReachableEngineCall Call(string api, string[] arguments,
            CinematicScriptBranchConditionSource[] branches, int line) => new(
            "test_callback", api, "", arguments, [], [], [], branches,
            "test_script.c", line, "callback[2] -> test_callback");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
