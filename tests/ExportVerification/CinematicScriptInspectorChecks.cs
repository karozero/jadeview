using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class CinematicScriptInspectorChecks
{
    internal static void Verify()
    {
        Check(CinematicScriptReader.DescribeCallbackName("sc_dummy")
                == "sc_dummy (source stub; behavior unknown)"
            && CinematicScriptReader.DescribeCallbackName("known_callback") == "known_callback",
            "The inspector should distinguish source placeholder callbacks from recovered implementations");
        Check(CinematicScriptReader.EffectiveCallbackNames(null).Length == 0
            && CinematicScriptReader.EffectiveCallbackNames(
                [new(0, 0xD9005D71, "known_callback"), new(1, 0xD9005D72, null)])
                .SequenceEqual(["known_callback"]),
            "Static callback analysis should use decoded owner slots, not model references or unmapped IDs");
        Check(CinematicScriptCallbackCoverage.ResolveConfiguredRange(
                    [new(0, 0, null), new(2, 0xD9005D72, null), new(4, 0xD9005D74, "known_callback")])
                == new CinematicCallbackSlotRange(2, 4)
            && CinematicScriptCallbackCoverage.ResolveConfiguredRange([]) == null,
            "Callback execution span should cover the lowest and highest configured model slots, including empty gaps, and be empty when no callback is configured");

        var targetedEventList = new CinematicEmbeddedEventTrackList(0x100, "event owner", 0x10,
            "SceneEvents", 0x200,
            [new CinematicEmbeddedEventTrack(3, 0, -1, 0, 1, 2)
            {
                Calls =
                [
                    (1.5f, new CinematicLiteralCall(0x300, 0, [1])),
                    (0.5f, new CinematicLiteralCall(0x300, 1, [2]))
                ]
            }]);
        var otherWorldEventList = targetedEventList with
        {
            WorldKey = 0x11,
            OwnerName = "other world owner"
        };
        var targetCommands = CinematicEventTargetCommandResolver.Resolve(
            [otherWorldEventList, targetedEventList], 0x10, 0x300);
        Check(targetCommands.Length == 2
            && targetCommands[0].Time == 0.5f && targetCommands[1].Time == 1.5f
            && targetCommands.All(command => command.EventList.WorldKey == 0x10)
            && CinematicEventTargetCommandResolver.Resolve([targetedEventList], 0x10, 0).Length == 0,
            "Reverse EVE target lookup should return ordered literal commands from the exact world and ignore invalid keys");

        var callbackCoverageOwner = new CinematicScriptOwner(0xD9005D70, 0, 0x71003F70, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Callback coverage fixture.gao", [], [], [])
        {
            CallbackSlots =
            [
                new(0, 0, null),
                new(1, 0xD9005D71, "known_callback"),
                new(2, 0xD9005D72, null)
            ]
        };
        var unmappedCallbacks = CinematicScriptCallbackCoverage.FindUnmapped([callbackCoverageOwner]);
        Check(unmappedCallbacks.Length == 1 && unmappedCallbacks[0].Slot.Index == 2
                && unmappedCallbacks[0].Slot.FunctionKey == 0xD9005D72,
            "Callback coverage should report configured unknown IDs while excluding empty and named slots");
        var worldSchedulerCallback = new CinematicScriptOwner(0xD9005D6F, 0, 0x71003F6F, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "World scheduler callback fixture", ["Cine_FigeleMonde_flm_fige"], [], [])
        {
            ModelFunctionNames = ["Cine_FigeleMonde_flm_fige"]
        };
        var worldSchedulerOperations = CinematicScriptReader.ReachableWorldSchedulerOperations(worldSchedulerCallback);
        Check(worldSchedulerOperations.Length == 2
                && worldSchedulerOperations.Any(operation => operation.Operation == "Pause"
                    && operation.Arguments.Contains("0x20", StringComparison.Ordinal)
                    && operation.ArgumentSources.Any(source => source.Contains("WORCurIndexGet", StringComparison.Ordinal))
                    && operation.Source.EndsWith("ai_compiledscripts_global_managers.c", StringComparison.Ordinal)
                    && operation.CallbackPath == "Cine_FigeleMonde_flm_fige"),
            "The inspector should surface source-confirmed world pause operations reachable from the configured callback slot");
        var cinematicStartOwner = new CinematicScriptOwner(0xD9005D6B, 0, 0x71003F6B, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Cinematic callback transition fixture",
            ["Cine_Events_cine_tests_depart"], [], [])
        {
            ModelFunctionNames =
            [
                "Cine_Events_cine_tests_depart",
                "Cine_Events_cine_tests_interruption",
                "Cine_Events_cine_exec_test_priorite"
            ]
        };
        var conditionalTransitionPaths = CinematicScriptReader.NestedCallbackPaths(cinematicStartOwner);
        Check(conditionalTransitionPaths.Any(path => path.FunctionName == "Cine_Events_cine_tests_interruption"
                && path.Path.Contains("if (v114->i_tests_a_reussir_exit)", StringComparison.Ordinal)
                && path.Path.Contains("source dispatch sites:", StringComparison.Ordinal)),
            "Callback paths should retain Iris source conditions at track transitions, including alternative branches");
        var indirectCinematicTrackOwner = new CinematicScriptOwner(0xD9005D6A, 0, 0x71003F6A, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Indirect callback fixture",
            ["Cine_Events_cine_test_fin_sons"], [], [])
        {
            ModelFunctionNames = ["Cine_Events_cine_test_fin_sons", "Cine_Events_cine_init"]
        };
        var indirectTrackPath = CinematicScriptReader.NestedCallbackPaths(indirectCinematicTrackOwner)
            .FirstOrDefault(path => path.FunctionName == "Cine_Events_cine_init");
        Check(indirectTrackPath?.Path.Contains("through pointer field f_track_init", StringComparison.Ordinal) == true
            && indirectTrackPath.Path.Contains("in Cine_Events_cine_init at ai_bge/ai_compiledscripts_global_managers.c:4223", StringComparison.Ordinal)
            && indirectTrackPath.Path.Contains("if (v1->des_i_ripley == 1)", StringComparison.Ordinal),
            "Callback graph should resolve serialized source function-pointer fields to their assignment and guarded dispatch sites");
        var soundCompletionOwner = new CinematicScriptOwner(0xD9005D69, 0, 0x71003F69, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Sound completion callback fixture",
            ["Cine_Events_Cine_exec_EndOfEvents"], [], [])
        {
            ModelFunctionNames = ["Cine_Events_Cine_exec_EndOfEvents", "Cine_Events_cine_test_fin_sons"]
        };
        var soundCompletionPath = CinematicScriptReader.NestedCallbackPaths(soundCompletionOwner)
            .FirstOrDefault(path => path.FunctionName == "Cine_Events_cine_test_fin_sons");
        Check(soundCompletionPath?.Path.Contains("replace-current-track", StringComparison.Ordinal) == true
            && soundCompletionPath.Path.Contains("ai_compiledscripts_global_managers.c:6116", StringComparison.Ordinal)
            && soundCompletionPath.Path.Contains("3500B021", StringComparison.Ordinal),
            "The source call graph should preserve dynamically resolved sound-completion track transitions and their key/provenance");
        var sourceEngineCalls = CinematicScriptReader.ReachableEngineCalls(worldSchedulerCallback);
        Check(sourceEngineCalls.Any(call => call.Api == "GFXMaterialSet"
                && call.FunctionName == "Cine_FigeleMonde_flm_fige"
                && call.Arguments.SequenceEqual(["a1", "v3", "9"])
                && call.Source.EndsWith("ai_compiledscripts_global_managers.c", StringComparison.Ordinal))
            && sourceEngineCalls.Any(call => call.Api == "AITrackCurChange"),
            "Reachable callback inspection should index direct source engine API calls with function and source provenance");
        var nativeMergeOwner = new CinematicScriptOwner(0xD9005D6A, 0, 0x71003F6A, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Native action merge fixture",
            ["Sally_sa_exec_actionkitselector"], [], [])
        {
            ModelFunctionNames = ["Sally_sa_exec_actionkitselector"]
        };
        var nativeMergeCalls = CinematicScriptReader.ReachableEngineCalls(nativeMergeOwner)
            .Where(call => call.Api == "ACTKitMerge").ToArray();
        Check(nativeMergeCalls.Length == 2
                && nativeMergeCalls.All(call => call.Variant == "NO_OVERWRITE"
                    && call.Source.EndsWith("ai_compiledscripts_sally.c", StringComparison.Ordinal)
                    && call.Arguments[2..].SequenceEqual(["10", "19"]))
                && nativeMergeCalls.Any(call => call.Arguments[0] == "AI_gpst_CurrentGameObject"
                    && call.Arguments[1] == "v2"
                    && call.ArgumentFields.Any(field => field.Name == "o_action_kit_owner"
                        && field.AliasVariable == "v2")),
            "Native callback action-kit merges should retain source variant, argument range, OVA alias and provenance");
        var mergeDestination = new CinematicScriptOwner(0x100, 0, 0, 0x200,
            uint.MaxValue, uint.MaxValue, "merge destination", [],
            [new("o_source", 0x301, "instance", "source action owner")], []);
        var mergeCall = new CinematicScriptReachableEngineCall("merge_fixture", "ACTKitMerge", "NO_OVERWRITE",
            ["AI_gpst_CurrentGameObject", "v2", "10", "19"], [],
            [new("o_source", "v2", 5)], [], [], "fixture.c", 6, "merge_fixture");
        var mergeActionKits = new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
        {
            [(0x200, 0x100)] = new() { [10] = new(0, []) },
            [(0x200, 0x301)] = new() { [10] = new(0, []), [11] = new(0, []), [20] = new(0, []) }
        };
        var mergeAnalysis = CinematicScriptActionKitMergeResolver.Analyze(mergeDestination, [mergeCall], mergeActionKits);
        Check(mergeAnalysis.Resolved.Length == 1 && mergeAnalysis.Unresolved.Length == 0
                && mergeAnalysis.Resolved[0] is
                    { DestinationKey: 0x100, SourceKey: 0x301, FirstAction: 10, LastAction: 19,
                        SourceSlotCount: 2, DestinationConflictCount: 1 }
                && mergeAnalysis.Resolved[0].SourceBinding.Contains("o_source", StringComparison.Ordinal),
            "Callback action-kit analysis should join a current-owner target and effective OVA source without treating the request as executed");
        var dynamicMergeAnalysis = CinematicScriptActionKitMergeResolver.Analyze(mergeDestination,
            [mergeCall with { Arguments = ["AI_MainActor[(uint32_t)v2 - 1]", "v2", "10", "19"] }], mergeActionKits);
        Check(dynamicMergeAnalysis.Resolved.Length == 0 && dynamicMergeAnalysis.Unresolved.Length == 1
                && dynamicMergeAnalysis.Unresolved[0].Reason.Contains("dynamic or ambiguous", StringComparison.Ordinal),
            "Callback action-kit analysis should preserve a dynamic destination as unresolved instead of guessing its owner");
        var fallbackMergeOwner = new CinematicScriptOwner(0xA001, 0, 0xB001, 0xC001,
            uint.MaxValue, uint.MaxValue, "Generic action merge fallback fixture",
            ["Merge_ActionKit_mak_init"],
            [new("des_o_perso", 0xD001, "instance", "merge destination")],
            [new("des_o_actionkit", 40, "model", "0x00000000"),
             new("des_i_act_debut", 33, "model", "200"),
             new("des_i_act_fin", 33, "model", "230")])
        {
            ModelFunctionNames = ["Merge_ActionKit_mak_init"]
        };
        var fallbackActionKits = new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
        {
            [(0xC001, 0xA001)] = new() { [200] = new(0, []), [201] = new(0, []) },
            [(0xC001, 0xD001)] = new() { [200] = new(0, []) }
        };
        var fallbackMergeAnalysis = CinematicScriptActionKitMergeResolver.Analyze(fallbackMergeOwner,
            CinematicScriptReader.ReachableEngineCalls(fallbackMergeOwner), fallbackActionKits);
        Check(fallbackMergeAnalysis.Unresolved.Length == 0
                && fallbackMergeAnalysis.Resolved.Any(merge => merge.DestinationKey == 0xD001
                && merge.SourceKey == fallbackMergeOwner.OwnerKey
                && merge.FirstAction == 200 && merge.LastAction == 230
                && merge.SourceSlotCount == 2),
            $"Native callback flow should follow a null OVA action-kit field initialized to the current owner and discard the statically false null-destination branch. Unresolved: {string.Join("; ", fallbackMergeAnalysis.Unresolved.Select(item => item.Reason))}");
        var sourceEngineCallSummary = CinematicScriptReader.SummarizeReachableEngineCalls(worldSchedulerCallback);
        Check(sourceEngineCallSummary.Any(summary => summary.Api == "GFXFlagSet"
                && summary.Variant == "CURRENT" && summary.CallSiteCount == 2
                && summary.Examples.All(example => example.CallbackPath == "Cine_FigeleMonde_flm_fige")),
            "Engine-call inventory should group repeated direct APIs and retain representative callback paths");
        var sourceReferences = CinematicScriptReader.ReachableSourceReferences(worldSchedulerCallback);
        Check(sourceReferences.Any(reference => reference.Kind == "Object"
                && reference.Symbol == "INV_MatFade_gao" && reference.Key == 0x5E006934
                && reference.FunctionName == "Cine_FigeleMonde_flm_fige"
                && reference.CallbackPath == "Cine_FigeleMonde_flm_fige"
                && reference.Source.EndsWith("ai_compiledscripts_global_managers.c", StringComparison.Ordinal))
            && sourceReferences.Any(reference => reference.Kind == "Function"
                && reference.Symbol == "Cine_FigeleMonde_flm_defige" && reference.Key == 0x3500B902),
            "Reachable callback inspection should expose source-resolved object and function keys with provenance");
        var sourceArgumentCall = new CinematicScriptReachableEngineCall("fixture_callback", "OBJ_CapaSet", "",
            ["(tGameObjectMain*)fixture_callback_target", "0x100", "0"], [], [], [], [],
            "fixture.c", 12, "fixture_callback");
        var sourceArgumentReferences = CinematicScriptArgumentBindingResolver.ResolveSourceObjectReferences(sourceArgumentCall,
        [
            new("fixture_callback", "Object", "target", 0x1234, "fixture.c", 3, "fixture_callback"),
            new("fixture_callback", "Object", "target_extra", 0x5678, "fixture.c", 4, "fixture_callback")
        ]);
        Check(sourceArgumentReferences.Length == 1 && sourceArgumentReferences[0].Key == 0x1234,
            "Source command arguments should join only exact function-local object-reference identifiers");
        var specialActorSlotReferences = CinematicScriptArgumentBindingResolver.ResolveSourceObjectReferences(
            sourceArgumentCall,
            [new("fixture_callback", "Object", "target", 1, "fixture.c", 3, "fixture_callback")]);
        Check(specialActorSlotReferences.Length == 0,
            "AI_MainActor slot references must not be treated as archive object dependencies");
        var sourceAssignmentCall = sourceArgumentCall with
        {
            Arguments = ["v26"],
            ArgumentAssignments =
            [
                new("v26", "AI_MainActor[fixture_callback_target - 1]", 10, 0),
                new("fixture_callback_target", "AI_ul_RealResolveOneRefObject(0x1234u)", 8, 1)
            ],
            BranchConditions = [new("if", "v2->des_i_enabled != 0", 7)]
        };
        var assignedObjectReference = CinematicScriptArgumentBindingResolver.ResolveSourceObjectReferences(
            sourceAssignmentCall,
            [new("fixture_callback", "Object", "target", 0x1234, "fixture.c", 3, "fixture_callback")]);
        Check(assignedObjectReference.Length == 1 && assignedObjectReference[0].Key == 0x1234,
            "Source object references should follow local assignments from a resolved object key to the API argument");
        var sourceArgumentOwner = new CinematicScriptOwner(0x100, 0, 0, 0x200, 0, 0,
            "OVA argument fixture", [], [],
            [new("des_i_SubMat", PcVariableFormat.IntegerType, "model", "3"),
             new("des_i_SubMat", PcVariableFormat.IntegerType, "instance", "5"),
             new("des_i_SubMatLevel", PcVariableFormat.IntegerType, "model", "2"),
             new("des_i_SubMatLevelExtra", PcVariableFormat.IntegerType, "instance", "99")]);
        var ovaArgumentCall = sourceArgumentCall with
        {
            Arguments = ["v2->des_i_SubMat", "v2->des_i_SubMatLevel"]
        };
        var ovaArgumentBindings = CinematicScriptArgumentBindingResolver.ResolveOwnerFields(ovaArgumentCall,
            sourceArgumentOwner);
        Check(ovaArgumentBindings.Length == 2
            && ovaArgumentBindings.Single(binding => binding.VariableName == "des_i_SubMat") is
                { Value: "5", Source: "instance" }
            && ovaArgumentBindings.Single(binding => binding.VariableName == "des_i_SubMatLevel") is
                { Value: "2", Source: "model" },
            "Source call arguments should resolve exact OVA field names using effective instance/model precedence");
        var gatedOvaCall = sourceArgumentCall with
        {
            Arguments = ["AI_gpst_CurrentGameObject"],
            BranchConditions = [new("if", "v1->i_sf_fade", 4133)]
        };
        var gatedOvaOwner = sourceArgumentOwner with
        {
            Variables = [new("i_sf_fade", PcVariableFormat.IntegerType, "model", "0")]
        };
        var gatedOvaBindings = CinematicScriptArgumentBindingResolver.ResolveOwnerFields(
            gatedOvaCall, gatedOvaOwner);
        Check(gatedOvaBindings.Length == 1
                && gatedOvaBindings[0] is { VariableName: "i_sf_fade", Value: "0", Source: "model" },
            "Source API summaries should resolve OVA fields used only by branch conditions");
        var ovaUsageOwner = new CinematicScriptOwner(0x101, 0, 0, 0x200, 0, 0,
            "OVA source-use fixture", [],
            [new("o_target", 0x301, "instance", "linked target"),
             new("o_unused", 0x302, "instance", "unreferenced model object")],
            [new("des_i_enabled", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_i_enabled_extra", PcVariableFormat.IntegerType, "model", "0")]);
        var ovaUsageCall = sourceArgumentCall with
        {
            Arguments = ["AI_gpst_CurrentGameObject"],
            BranchConditions = [new("if", "v2->des_i_enabled != 0", 29)],
            OvaFieldWrites = [new("o_target", "v2->o_target", 30,
                [new("if", "v2->des_i_enabled != 0", 29)])]
        };
        var ovaFieldUsages = CinematicScriptOvaUsageResolver.Analyze(ovaUsageOwner, [ovaUsageCall]);
        Check(ovaFieldUsages.Length == 2
            && ovaFieldUsages.Single(usage => usage.VariableName == "des_i_enabled") is
                { Access: "read", FunctionName: "fixture_callback", Line: 12 }
            && ovaFieldUsages.Single(usage => usage.VariableName == "o_target") is
                { Access: "write", Writes.Length: 1 }
            && ovaFieldUsages.All(usage => usage.VariableName != "o_unused")
            && CinematicScriptOvaUsageResolver.Analyze(ovaUsageOwner, []).Length == 0,
            "OVA source-use analysis should connect captured source reads/writes and avoid treating unused serialized references as callback behavior");
        var indexedOvaOwner = new CinematicScriptOwner(0x102, 0, 0, 0x200, 0, 0,
            "Indexed OVA source fixture", ["action_reaction_ar_init"],
            [new("o_activateur", 0x401, "instance", "activation target"),
             new("o_unused", 0x402, "instance", "unreferenced target")],
            [new("des_i_nb_kick_for_break", PcVariableFormat.IntegerType, "instance", "3"),
             new("des_i_unused", PcVariableFormat.IntegerType, "instance", "0")])
        {
            ModelFunctionNames = ["action_reaction_ar_init"]
        };
        var indexedOvaAccesses = CinematicScriptReader.ReachableOvaFieldAccesses(indexedOvaOwner);
        var indexedOvaUsages = CinematicScriptOvaUsageResolver.Analyze(indexedOvaOwner);
        Check(indexedOvaAccesses.Any(access => access.VariableName == "des_i_nb_kick_for_break"
                && access.Access == "read" && access.Source == "instance"
                && access.BranchConditions.Length > 0)
            && indexedOvaAccesses.Any(access => access.VariableName == "o_activateur"
                && access.Access == "write" && access.Source == "instance")
            && indexedOvaUsages.Any(usage => usage.VariableName == "des_i_nb_kick_for_break")
            && indexedOvaUsages.Any(usage => usage.VariableName == "o_activateur")
            && indexedOvaUsages.All(usage => usage.VariableName is not ("o_unused" or "des_i_unused")),
            "Generated Iris field-access data should connect instance OVA reads/writes and source branch conditions through configured callback reachability only");
        var objectFieldOwner = sourceArgumentOwner with
        {
            ObjectReferences = [new("des_o_Interrupteur", 0x9E000123, "instance", "linked switch")]
        };
        var aliasedOvaCall = sourceArgumentCall with
        {
            Arguments = ["v7", "0x100"],
            ArgumentFields = [new("des_o_Interrupteur", "v7", 106)]
        };
        var aliasedOvaBinding = CinematicScriptArgumentBindingResolver.ResolveOwnerFields(aliasedOvaCall,
            objectFieldOwner);
        Check(aliasedOvaBinding.Length == 1 && aliasedOvaBinding[0] is
                { VariableName: "des_o_Interrupteur", ObjectKey: 0x9E000123, TargetName: "linked switch",
                    Source: "instance" },
            "A source call should join a function-local object alias to the effective OVA object reference while retaining the alias line");
        var assignmentChainCall = aliasedOvaCall with
        {
            ArgumentFields = [],
            ArgumentAssignments = [new("v7", "v4->des_o_Interrupteur", 106, 0)]
        };
        var assignmentChainBinding = CinematicScriptArgumentBindingResolver.ResolveOwnerFields(
            assignmentChainCall, objectFieldOwner);
        Check(assignmentChainBinding.Length == 1 && assignmentChainBinding[0].TargetName == "linked switch",
            "A bounded local assignment chain should link a referenced OVA member to its effective object target");
        var mpegPlayerSourceOwner = new CinematicScriptOwner(0xD9005D6E, 0, 0x71003F6E, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "MPEG player callback fixture", ["mpegplayer_init"], [], [])
        {
            ModelFunctionNames = ["mpegplayer_init", "mpegplayer_loop"]
        };
        var mpegPlayerCapabilityCall = CinematicScriptReader.ReachableEngineCalls(mpegPlayerSourceOwner)
            .FirstOrDefault(call => call.Api == "OBJ_CapaTest" && call.Arguments.FirstOrDefault() == "v5");
        Check(mpegPlayerCapabilityCall?.ArgumentFields.Any(field =>
                field.Name == "des_o_Interrupteur" && field.AliasVariable == "v5"
                && field.AssignmentLine == 132) == true,
            "Iris C source should preserve a unique local pointer-to-OVA-field alias on reachable engine calls");
        var stopAtEndMaterialCall = CinematicScriptReader.ReachableEngineCalls(mpegPlayerSourceOwner)
            .FirstOrDefault(call => call.Api == "MATProSeti" && call.Arguments.Length == 3
                && call.Arguments[1] == "1" && call.Arguments[2] == "1");
        Check(stopAtEndMaterialCall?.BranchConditions.Any(condition =>
                condition.Expression.Contains("des_i_StopAtEnd", StringComparison.Ordinal)) == true,
            "Source API inventory should preserve enclosing braced-if expressions without evaluating them");
        var nullSwitchCapabilityCall = CinematicScriptReader.ReachableEngineCalls(mpegPlayerSourceOwner)
            .FirstOrDefault(call => call.Api == "OBJ_CapaTest" && call.Arguments.FirstOrDefault() == "0");
        Check(nullSwitchCapabilityCall?.BranchConditions.Any(condition => condition.Kind == "else"
                && condition.Expression == "v5") == true,
            "Source API inventory should associate a direct else branch with its originating if expression");
        var mpegPlayerObjectCalls = CinematicScriptReferencedObjectCallResolver.Resolve(mpegPlayerSourceOwner with
        {
            ObjectReferences =
            [
                new("des_o_Interrupteur", 0x9E000123, "instance", "linked computer switch"),
                new("des_o_stopsound", 0x9E000124, "instance", "linked radio")
            ]
        });
        Check(mpegPlayerObjectCalls.Any(call => call.VariableName == "des_o_Interrupteur"
                && call.TargetName == "linked computer switch" && call.Api == "OBJ_CapaTest")
            && mpegPlayerObjectCalls.Any(call => call.VariableName == "des_o_stopsound"
                && call.TargetName == "linked radio" && call.Api == "OBJ_FlagInactiveSet"
                && call.Branches.Length > 0),
            "Linked-object call inspection should also expose generic media-player switch and radio operations with source conditions");
        var specialActorSlotCalls = CinematicScriptReferencedObjectCallResolver.Resolve(
            mpegPlayerSourceOwner with
            {
                ObjectReferences =
                [new("des_o_Interrupteur", 1, "instance", "AI_MainActor slot")]
            });
        Check(!specialActorSlotCalls.Any(call => call.VariableName == "des_o_Interrupteur"),
            "Linked-object call inspection must not report a runtime actor slot as an archive GAO dependency");
        var activatorSourceOwner = new CinematicScriptOwner(0xD9005D6C, 0, 0x71003F6C, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Nested branch fixture", ["activator_simple_ac_loop"], [], [])
        {
            ModelFunctionNames = ["activator_simple_ac_loop"]
        };
        var elseIfResetCall = CinematicScriptReader.ReachableEngineCalls(activatorSourceOwner)
            .FirstOrDefault(call => call.Api == "GRPSectInvisibleSet"
                && call.Arguments.Length == 3 && call.Arguments[2] == "-1");
        Check(elseIfResetCall?.BranchConditions.Any(condition => condition.Kind == "else"
                && condition.Expression.Contains("COLBVPivotCollide", StringComparison.Ordinal)) == true
            && elseIfResetCall.BranchConditions.Any(condition => condition.Kind == "if"
                && condition.Expression == "v5->i_ON"),
            "Nested else-if source paths should retain the rejected outer condition and the selected nested condition");
        var encodedFlagDetectorOwner = new CinematicScriptOwner(0xD9005D6B, 0, 0x71003F6B, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Encoded saved-flag detector fixture",
            ["Detect_o_Capa_doc_une_a_vrai"], [], [])
        {
            ModelFunctionNames = ["Detect_o_Capa_doc_une_a_vrai"]
        };
        var encodedFlagSetCalls = CinematicScriptReader.ReachableEngineCalls(encodedFlagDetectorOwner)
            .Where(call => call.Api == "ARRGlobalIntFlagSet"
                && call.Arguments.FirstOrDefault() == "v2->des_i_SFlag").ToArray();
        Check(encodedFlagSetCalls.Length >= 2 && encodedFlagSetCalls.Any(call =>
                call.BranchConditions.Any(condition => condition.Kind == "else"
                    && condition.Expression == "v2->des_i_SFlag <= 10000")),
            "Source API inventory should retain encoded saved-flag else branches on both detector paths");
        var cinematicMessageOwner = new CinematicScriptOwner(0xD9005D6D, 0, 0x71003F6D, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "Cinematic source message fixture",
            ["Cine_Events_cine_play_eventtrack"], [], [])
        {
            ModelFunctionNames = ["Cine_Events_cine_play_eventtrack"]
        };
        var sourceMessages = CinematicScriptReader.ReachableEngineCalls(cinematicMessageOwner)
            .Where(call => call.Api == "MSGSend" && call.Arguments.Skip(1).Any(argument => argument == "&Src"))
            .Select(call => call.MessageFields.FirstOrDefault(field => field.Name == "msgID"))
            .Where(field => field != null).Select(field => field!.Expression).ToArray();
        Check(sourceMessages.Contains("47") && sourceMessages.Contains("60"),
            "Source message sends should expose nearby message-ID field assignments without evaluating branch conditions");
        var mainActorMessageSend = CinematicScriptReader.ReachableEngineCalls(cinematicMessageOwner)
            .FirstOrDefault(call => call.Api == "MSGSend" && call.Arguments.FirstOrDefault() == "v5");
        Check(mainActorMessageSend?.ArgumentAssignments.Any(assignment => assignment is
                { Variable: "v5", Depth: 0 } && assignment.Expression.Contains("AI_MainActor", StringComparison.Ordinal)) == true
            && mainActorMessageSend.ArgumentAssignments.Any(assignment => assignment is
                { Variable: "v8", Depth: 1 } && assignment.Expression.Contains("AI_MainActor", StringComparison.Ordinal)),
            "Source message recipients should retain bounded local assignment chains without evaluating runtime branches");
        var modelOverrideVariable = new CinematicOwnerDefaults.Variable("v_LanguePos",
            (1u << 30) | 3, PcVariableFormat.IntegerType,
            [.. BitConverter.GetBytes(3), .. BitConverter.GetBytes(10), .. BitConverter.GetBytes(20),
                .. BitConverter.GetBytes(30)], "model");
        var shorterInstanceOverride = new CinematicOwnerDefaults.Variable("V_LANGUEPOS",
            (1u << 30) | 2, PcVariableFormat.IntegerType,
            [.. BitConverter.GetBytes(2), .. BitConverter.GetBytes(90), .. BitConverter.GetBytes(80)], "instance");
        var effectiveOverride = CinematicOwnerDefaults.OverlayVariable(modelOverrideVariable,
            shorterInstanceOverride);
        Check(effectiveOverride.Source == "instance prefix"
                && effectiveOverride.PackedLength == modelOverrideVariable.PackedLength
                && BinaryPrimitives.ReadInt32LittleEndian(effectiveOverride.RawBytes.AsSpan(4)) == 90
                && BinaryPrimitives.ReadInt32LittleEndian(effectiveOverride.RawBytes.AsSpan(8)) == 80
                && BinaryPrimitives.ReadInt32LittleEndian(effectiveOverride.RawBytes.AsSpan(12)) == 30,
            "Unequal OVA arrays should overlay the instance prefix and preserve the model tail per Iris initialization");

        var worldTransitionOwner = new CinematicScriptOwner(0xD9005D6E, 0, 0x71003F6E, 0xFF005833,
            uint.MaxValue, 0xC1009BF9, "ES_MDisk_0_2.gao",
            ["ES_au_sol_esas_play_events"], [], [])
        {
            ModelFunctionNames = ["ES_au_sol_esas_play_events"]
        };
        string[] worldTransitionFindings = CinematicScriptBehaviorAnalyzer.Analyze(worldTransitionOwner);
        Check(worldTransitionFindings.Any(finding => finding.Contains("SceneEvents indices 3, 4 and 5", StringComparison.Ordinal))
            && worldTransitionFindings.Any(finding => finding.Contains("live jump node", StringComparison.Ordinal)),
            "The shared ES_au_sol event callback should expose source-confirmed channel branches and their runtime-state limits");
        var worldTransitionSourceCalls = CinematicScriptReader.ReachableEngineCalls(worldTransitionOwner);
        var worldTransitionEventInventory = new Dictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]>
        {
            [(worldTransitionOwner.WorldKey, worldTransitionOwner.OwnerKey)] =
            [new(worldTransitionOwner.OwnerKey, worldTransitionOwner.Name, worldTransitionOwner.WorldKey,
                "SceneEvents", worldTransitionOwner.SceneEventsKey,
                [new(3, 0, -1, 0, 4, 1), new(4, 0xA100, 2, 0, 8, 2), new(5, 0xA200, 3, 0, 12, 3)])]
        };
        var referencedOwner = worldTransitionOwner with
        {
            OwnerKey = 0xB100,
            SceneEventsKey = 0xB200,
            ObjectReferences = [new("o_event_target", 0xB300, "instance", "event target")]
        };
        worldTransitionEventInventory[(referencedOwner.WorldKey, 0xB300)] =
        [new(0xB300, "referenced event target", referencedOwner.WorldKey, "SceneEvents", 0xB301,
            [new(2, 0, -1, 0, 7, 1.5f)])];
        var referencedTrackCall = worldTransitionSourceCalls.First(call => call.Api == "TRAPlay") with
        {
            Arguments = ["eventList", "2"],
            ArgumentFields = [new("o_event_target", "eventList", 10)]
        };
        var assignedReferencedTrackCall = referencedTrackCall with
        {
            Arguments = ["targetPointer", "2"],
            ArgumentFields = [],
            ArgumentAssignments = [new("targetPointer", "(tGameObjectMain*)v34->o_event_target", 12, 0)]
        };
        var referencedTrackInvocation = CinematicScriptEventTrackCallResolver.Resolve(referencedOwner,
            [referencedTrackCall], worldTransitionEventInventory).Single();
        var assignedReferencedTrackInvocation = CinematicScriptEventTrackCallResolver.Resolve(referencedOwner,
            [assignedReferencedTrackCall], worldTransitionEventInventory).Single();
        var sourceTrackInvocations = CinematicScriptEventTrackCallResolver.Resolve(worldTransitionOwner,
            worldTransitionSourceCalls, worldTransitionEventInventory);
        var wrongOwnerSourceCall = worldTransitionSourceCalls.First(call => call.Api == "TRAPlay")
            with { Arguments = ["other_object", "3"] };
        var dynamicIndexSourceCall = worldTransitionSourceCalls.First(call => call.Api == "TRAPlay")
            with { Arguments = ["AI_gpst_CurrentGameObject", "track_index"] };
        var playAllSourceCall = worldTransitionSourceCalls.First(call => call.Api == "TRAPlay")
            with { Api = "TRAPlayAll", Arguments = ["AI_gpst_CurrentGameObject"] };
        var boundedTrackInvocations = CinematicScriptEventTrackCallResolver.Resolve(worldTransitionOwner,
            [.. worldTransitionSourceCalls, wrongOwnerSourceCall, dynamicIndexSourceCall, playAllSourceCall],
            worldTransitionEventInventory);
        var sourceTrackStartCalls = sourceTrackInvocations.Where(invocation => invocation.Operation == "TRAPlay")
            .ToArray();
        var boundedTrackStartCalls = boundedTrackInvocations.Where(invocation => invocation.Operation == "TRAPlay")
            .ToArray();
        var allTrackInvocation = boundedTrackInvocations.Single(invocation => invocation.Operation == "TRAPlayAll");
        Check(sourceTrackStartCalls.Length == 3
                && sourceTrackStartCalls.Select(invocation => invocation.Tracks.Single().Index)
                    .Order().SequenceEqual([3, 4, 5])
                && sourceTrackStartCalls.All(invocation => invocation.TargetBinding == "current owner")
                && sourceTrackStartCalls.All(invocation => invocation.SourceCall.BranchConditions.Length > 0)
                && boundedTrackStartCalls.Length == sourceTrackStartCalls.Length
                && allTrackInvocation.TrackIndex == null
                && allTrackInvocation.Tracks.Select(track => track.Index).Order().SequenceEqual([3, 4, 5])
                && referencedTrackInvocation.EventList.OwnerKey == 0xB300
                && referencedTrackInvocation.Tracks.Single().Index == 2
                && referencedTrackInvocation.TargetBinding.Contains("o_event_target", StringComparison.Ordinal)
                && assignedReferencedTrackInvocation.EventList.OwnerKey == 0xB300
                && assignedReferencedTrackInvocation.Tracks.Single().Index == 2,
            "Reachable source TRA calls should resolve current-owner and OVA-referenced receivers through argument aliases or local member assignments, literal indices and all-track operations while preserving branch evidence and rejecting unknown receivers/indices");

        var trackIndexOwner = worldTransitionOwner with
        {
            OwnerKey = 0xB500,
            SceneEventsKey = 0xB501,
            Variables = [new("des_i_nb_kick_for_break", 0, "instance", "3")]
        };
        var trackIndexInventory = new Dictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]>
        {
            [(trackIndexOwner.WorldKey, trackIndexOwner.OwnerKey)] =
            [new(trackIndexOwner.OwnerKey, trackIndexOwner.Name, trackIndexOwner.WorldKey,
                "SceneEvents", trackIndexOwner.SceneEventsKey,
                Enumerable.Range(0, 10).Select(index => new CinematicEmbeddedEventTrack(index,
                    0, -1, 0, 1, 1)).ToArray())]
        };
        var arithmeticTrackCall = worldTransitionSourceCalls.First(call => call.Api == "TRAPlay") with
        {
            Arguments = ["AI_gpst_CurrentGameObject", "a2a + 2"],
            ArgumentAssignments = [new("a2a", "3 * (v1->des_i_nb_kick_for_break - 1)", 71, 0)]
        };
        var arithmeticTrackInvocation = CinematicScriptEventTrackCallResolver.Resolve(trackIndexOwner,
            [arithmeticTrackCall], trackIndexInventory).Single();
        var sourceArithmeticOwner = trackIndexOwner with
        {
            CallbackNames = ["action_reaction_ar_init"],
            ModelFunctionNames = ["action_reaction_ar_init"]
        };
        var sourceArithmeticInvocations = CinematicScriptEventTrackCallResolver.Resolve(sourceArithmeticOwner,
            CinematicScriptReader.ReachableEngineCalls(sourceArithmeticOwner)
                .Where(call => call.Api == "TRAPlay"), trackIndexInventory);
        var scalarOvaTrackOwner = trackIndexOwner with
        {
            Variables = [new("i_nbr_track_ouverture", 0, "instance", "2")]
        };
        var scalarOvaTrackCall = arithmeticTrackCall with
        {
            Arguments = ["AI_gpst_CurrentGameObject", "track"],
            ArgumentAssignments = [new("track", "v34->i_nbr_track_ouverture", 72, 0)]
        };
        var scalarOvaTrackInvocation = CinematicScriptEventTrackCallResolver.Resolve(scalarOvaTrackOwner,
            [scalarOvaTrackCall], trackIndexInventory).Single();
        var loopedTrackCall = scalarOvaTrackCall with
        {
            LoopContexts = [new("for", "track = 0; track < count; ++track", 70)]
        };
        var ambiguousAssignmentTrackCall = scalarOvaTrackCall with
        {
            ArgumentAssignments = [new("track", "2", 72, 0, IsUnique: false)]
        };
        var randomizedTrackCall = scalarOvaTrackCall with
        {
            Arguments = ["AI_gpst_CurrentGameObject", "v1->i_track_crt"],
            ArgumentAssignments = [new("i_track_crt", "AI_EvalFunc_MATHRandInt_C(0, v1->i_nb_track)", 73, 0)]
        };
        var sourceDoorOwner = trackIndexOwner with
        {
            CallbackNames = ["porte_po_ouvre_toi"],
            ModelFunctionNames = ["porte_po_ouvre_toi"]
        };
        var sourceDoorTrackCall = CinematicScriptReader.ReachableEngineCalls(sourceDoorOwner)
            .FirstOrDefault(call => call.Api == "TRAPlay" && call.Arguments.Length > 1
                && call.Arguments[1] == "i");
        var unresolvedTrackAnalysis = CinematicScriptEventTrackCallResolver.Analyze(trackIndexOwner,
            [loopedTrackCall, randomizedTrackCall], trackIndexInventory);
        var missingSceneEventsAnalysis = CinematicScriptEventTrackCallResolver.Analyze(trackIndexOwner,
            [scalarOvaTrackCall], new Dictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]>());
        var unsetEventListOwner = referencedOwner with
        {
            ObjectReferences = [],
            Variables = [new("o_event_target", 40, "model", "0x00000000")]
        };
        var unsetEventListCall = referencedTrackCall with
        {
            Arguments = ["eventList", "2"],
            ArgumentFields = [new("o_event_target", "eventList", 10)]
        };
        var unsetEventListAnalysis = CinematicScriptEventTrackCallResolver.Analyze(unsetEventListOwner,
            [unsetEventListCall], worldTransitionEventInventory);
        var literalNullEventListAnalysis = CinematicScriptEventTrackCallResolver.Analyze(worldTransitionOwner,
            [playAllSourceCall with { Arguments = ["0"] }], worldTransitionEventInventory);
        var setEndCall = scalarOvaTrackCall with
        {
            Api = "TRASetEnd",
            Arguments = ["AI_gpst_CurrentGameObject", "2"],
            ArgumentAssignments = []
        };
        var setEndAnalysis = CinematicScriptEventTrackCallResolver.Analyze(trackIndexOwner,
            [setEndCall], trackIndexInventory);
        var doorLoopOwner = trackIndexOwner with
        {
            Variables = [new("i_nbr_track_ouverture", 0, "model", "4")]
        };
        var doorLoopCall = scalarOvaTrackCall with
        {
            Arguments = ["AI_gpst_CurrentGameObject", "i"],
            ArgumentAssignments = [],
            LoopContexts = [new("for",
                "i = v34->i_nbr_track_ouverture; i < 2 * v34->i_nbr_track_ouverture; ++i", 70)]
        };
        var doorLoopAnalysis = CinematicScriptEventTrackCallResolver.Analyze(doorLoopOwner,
            [doorLoopCall], trackIndexInventory);
        var mutatedDoorLoopAnalysis = CinematicScriptEventTrackCallResolver.Analyze(doorLoopOwner,
            [doorLoopCall with
            {
                LoopContexts = [doorLoopCall.LoopContexts![0] with { InductionVariableModified = true }]
            }], trackIndexInventory);
        var nonIncrementingLoopAnalysis = CinematicScriptEventTrackCallResolver.Analyze(doorLoopOwner,
            [doorLoopCall with
            {
                LoopContexts = [doorLoopCall.LoopContexts![0] with { Expression = "i = 0; i < 4; i - 1" }]
            }], trackIndexInventory);
        var overflowingLoopAnalysis = CinematicScriptEventTrackCallResolver.Analyze(doorLoopOwner,
            [doorLoopCall with
            {
                LoopContexts = [doorLoopCall.LoopContexts![0] with
                {
                    Expression = "i = -9223372036854775807; i < 9223372036854775807; ++i"
                }]
            }], trackIndexInventory);
        Check(arithmeticTrackInvocation.TrackIndex == 8
                && arithmeticTrackInvocation.IndexBinding == "OVA des_i_nb_kick_for_break=3"
                && sourceArithmeticInvocations.Select(invocation => invocation.TrackIndex)
                    .Order().SequenceEqual(new int?[] { 6, 7, 8 })
                && sourceArithmeticInvocations.All(invocation =>
                    invocation.IndexBinding == "OVA des_i_nb_kick_for_break=3")
                && scalarOvaTrackInvocation.TrackIndex == 2
                && scalarOvaTrackInvocation.IndexBinding == "OVA i_nbr_track_ouverture=2"
                && sourceDoorTrackCall?.LoopContexts is { Length: > 0 }
                && CinematicScriptEventTrackCallResolver.Resolve(trackIndexOwner,
                    [loopedTrackCall, randomizedTrackCall, ambiguousAssignmentTrackCall],
                    trackIndexInventory).Length == 0,
            "TRA indices should resolve bounded integer expressions and scalar OVA aliases, while loops, ambiguous assignments and runtime-randomized indices remain unresolved");
        Check(unresolvedTrackAnalysis.Unresolved.Length == 2
                && unresolvedTrackAnalysis.Unresolved.Any(call => call.Reason.Contains("inside a for loop", StringComparison.Ordinal))
                && unresolvedTrackAnalysis.Unresolved.Any(call => call.Reason.Contains("uniquely assigned scalar OVA", StringComparison.Ordinal))
                && missingSceneEventsAnalysis.Unresolved.Single().Reason.Contains("did not resolve to exactly one decoded list", StringComparison.Ordinal)
                && setEndAnalysis.Resolved.Single().Operation == "TRASetEnd"
                && setEndAnalysis.Resolved.Single().TrackIndex == 2
                && doorLoopAnalysis.Resolved.Select(invocation => invocation.TrackIndex).Order()
                    .SequenceEqual(new int?[] { 4, 5, 6, 7 })
                && doorLoopAnalysis.Resolved.All(invocation => invocation.IndexBinding!.Contains(
                    "OVA i_nbr_track_ouverture=4", StringComparison.Ordinal))
                && mutatedDoorLoopAnalysis.Resolved.Length == 0
                && mutatedDoorLoopAnalysis.Unresolved.Single().Reason.Contains(
                    "iteration behavior cannot be safely reduced", StringComparison.Ordinal)
                && nonIncrementingLoopAnalysis.Resolved.Length == 0
                && overflowingLoopAnalysis.Resolved.Length == 0,
            "TRA inspection should explain unresolved loops and missing SceneEvents links, include TRASetEnd and expand only bounded for-loop indices using stable induction variables and scalar OVA values");
        Check(unsetEventListAnalysis.Unresolved.Single().Reason.Contains("o_event_target", StringComparison.Ordinal)
                && unsetEventListAnalysis.Unresolved.Single().Reason.Contains("effective value 0", StringComparison.Ordinal)
                && literalNullEventListAnalysis.Unresolved.Single().Reason.Contains("literal null receiver", StringComparison.Ordinal),
            "TRA inspection should distinguish an explicitly zero OVA event-list reference and literal null receiver from unresolved nonzero references");

        var localBrain = new PcSceneLoader.Record(0xB100, 'q', 0, 8) { LoadWorldKey = 0x10 };
        var duplicateBrain = new PcSceneLoader.Record(0xB100, 'q', 8, 8) { LoadWorldKey = 0x20 };
        var sharedModel = new PcSceneLoader.Record(0xB200, 's', 16, 8) { LoadWorldKey = 0 };
        var ambiguousResourceIndex = new PcSceneResourceIndex([localBrain, duplicateBrain, sharedModel]);
        Check(ReferenceEquals(ambiguousResourceIndex.Resolve(0xB100, 0x10), localBrain)
            && ReferenceEquals(ambiguousResourceIndex.Resolve(0xB100, 0x20), duplicateBrain),
            "Script resources with duplicate keys should resolve within the owning world first");
        Check(ReferenceEquals(ambiguousResourceIndex.Resolve(0xB200, 0x10), sharedModel),
            "A world-zero shared script resource should be a valid fallback");
        Check(ambiguousResourceIndex.Resolve(0xB100, 0x30) == null,
            "A duplicate script resource without a local or shared candidate should remain unresolved");
        var localActorEvent = new PcSceneLoader.Record(0xB301, 'i', 24, 8) { LoadWorldKey = 0x10 };
        var duplicateActorEventA = new PcSceneLoader.Record(0xB302, 'i', 32, 8) { LoadWorldKey = 0x20 };
        var duplicateActorEventB = new PcSceneLoader.Record(0xB302, 'i', 40, 8) { LoadWorldKey = 0x30 };
        var actorEventResources = new PcSceneResourceIndex(
            [localActorEvent, duplicateActorEventA, duplicateActorEventB]);
        Check(ReferenceEquals(actorEventResources.Resolve(0xB301, 0x30), localActorEvent)
            && actorEventResources.Resolve(0xB302, 0x40) == null,
            "Base Events resources should resolve from a unique actor source world and leave duplicate cross-world keys ambiguous");
        var duplicateLocalEventA = new PcSceneLoader.Record(0xB303, 'i', 48, 8) { LoadWorldKey = 0x10 };
        var duplicateLocalEventB = new PcSceneLoader.Record(0xB303, 'i', 56, 8) { LoadWorldKey = 0x10 };
        var duplicateLocalEventResources = new PcSceneResourceIndex(
            [duplicateLocalEventA, duplicateLocalEventB]);
        PcSceneResourceResolution duplicateLocalResolution = duplicateLocalEventResources
            .ResolveDetailed(0xB303, 0x10);
        Check(duplicateLocalResolution.Record == null
            && duplicateLocalResolution.Failure == "multiple records in the preferred world",
            "Resource resolution must preserve duplicate keys inside the preferred world as ambiguous instead of choosing the last record");
        Check(ArchiveCinematicScriptIndex.VerifyOwnerResourceResolution(),
            "Archive script indexing must reject conflicting preferred resource payloads and accept a unique shared payload");

        var actionLinkOwner = new CinematicScriptOwner(0x910, 0, 0x920, 0x30,
            0xA100, 0xA200, "actor with event/action link", [], [], []);
        var actionLinkTable = new Dictionary<int, CinematicAction>
        {
            [3] = new(byte.MaxValue, [new(new AnimationClip { Key = 0xA100 }, 0, 64, 1)]),
            [8] = new(byte.MaxValue, [new(new AnimationClip { Key = 0xA101 }, 0, 64, 1)])
        };
        var currentActionCall = new CinematicScriptReachableEngineCall("actor_action", "ACTActionSet",
            "CURRENT", ["3"], [], [], [], [new("if", "saved flag 2166", 42)],
            "ai_bge/actor.c", 43, "actor_init -> actor_action");
        var directActionCall = currentActionCall with
        {
            Variant = "",
            Arguments = ["AI_gpst_CurrentGameObject", "8"]
        };
        var forcedActionCall = currentActionCall with { Arguments = ["-2147483645"] };
        var frameActionCall = currentActionCall with
        {
            Api = "ACTActionFrameSet",
            Arguments = ["8", "12"]
        };
        var frequencyActionCall = currentActionCall with
        {
            Api = "ACTActionFrequencySet",
            Variant = "",
            Arguments = ["owner", "3", "64"]
        };
        var actionRequestAnalysis = CinematicScriptActionRequestResolver.Analyze(actionLinkOwner,
            [currentActionCall, directActionCall, forcedActionCall, frameActionCall, frequencyActionCall,
                directActionCall with { Arguments = ["owner", "99"] }],
            actionLinkTable);
        Check(CinematicActorActionLinkResolver.FindBaseEventActionSlots(actionLinkOwner, actionLinkTable)
                .SequenceEqual([3]),
            "The inspector should identify action slots whose clip key matches the actor's base Events resource");
        Check(CinematicActorActionLinkResolver.FindBaseEventActionSlots(
                actionLinkOwner with { EventsKey = uint.MaxValue }, actionLinkTable).Length == 0,
            "An absent base Events reference should not produce action-slot links");
        Check(actionRequestAnalysis.Resolved.Select(request => request.ActionIndex).SequenceEqual([3, 8, 3, 8, 3])
                && actionRequestAnalysis.Resolved[0].Action.Items.Single().Clip.Key == 0xA100
                && actionRequestAnalysis.Resolved[0].SourceCall.BranchConditions.Single().Expression == "saved flag 2166"
                && actionRequestAnalysis.Resolved[3] is { ControlValue: 12, SourceCall.Api: "ACTActionFrameSet" }
                && actionRequestAnalysis.Resolved[4] is { ControlValue: 64, SourceCall.Api: "ACTActionFrequencySet" }
                && actionRequestAnalysis.Unresolved.Single().Reason.Contains("absent from the owner's decoded action table", StringComparison.Ordinal),
            "Source-reachable action, frame and frequency requests should join action slots and branch provenance to decoded clips");
        var unresolvedFrequency = CinematicScriptActionRequestResolver.Analyze(actionLinkOwner,
            [frequencyActionCall with { Arguments = ["owner", "3", "runtime_frequency"] }], actionLinkTable);
        Check(unresolvedFrequency.Resolved.Length == 0
            && unresolvedFrequency.Unresolved.Single().Reason.Contains("action frequency", StringComparison.Ordinal),
            "Dynamic callback animation frequency should remain unresolved rather than being guessed");
        var serializedActionTrack = new CinematicScriptEventTrack(0x30, 0x910, "Events", 0xA100,
            "event action link fixture", 3,
            [
                (0f, new CinematicLiteralCall(0x910, 0x3391, [3])),
                (1f, new CinematicLiteralCall(0x910, 0x339B, [8, 12])),
                (2f, new CinematicLiteralCall(0x910, 0x339C, [unchecked((uint)-2147483645), 64])),
                (3f, new CinematicLiteralCall(0x911, 0x3391, [99])),
                (4f, new CinematicLiteralCall(0x910, 0x339B, [8]))
            ], []);
        var serializedActionLinks = CinematicEventActionLinkResolver.Analyze(serializedActionTrack,
            new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
            {
                [(0x30, 0x910)] = actionLinkTable
            });
        Check(serializedActionLinks.Resolved.Select(link => link.ActionIndex).SequenceEqual([3, 8, 3])
            && serializedActionLinks.Resolved[0].Action.Items.Single().Clip.Key == 0xA100
            && serializedActionLinks.Resolved[1] is { Function: "ACTActionFrameSet", ControlValue: 12 }
            && serializedActionLinks.Resolved[2] is { Function: "ACTActionFrequencySet", ControlValue: 64 }
            && serializedActionLinks.Unresolved.Length == 2
            && serializedActionLinks.Unresolved.Any(link => link.Reason.Contains("no decoded action table", StringComparison.Ordinal))
            && serializedActionLinks.Unresolved.Any(link => link.Reason.Contains("missing its frame value", StringComparison.Ordinal)),
            "Serialized EVE action, frame and frequency commands should resolve their target actor's clip table without guessing missing assets");
        var otherWorldActionTable = new Dictionary<int, CinematicAction> { [3] = actionLinkTable[8] };
        var scopedActionTables = new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
        {
            [(0x30, 0x910)] = actionLinkTable,
            [(0x40, 0x910)] = otherWorldActionTable
        };
        var localActionKit = CinematicActionKitScopeResolver.Resolve(0x910, 0x30, scopedActionTables);
        var ambiguousActionKit = CinematicActionKitScopeResolver.Resolve(0x910, 0x50, scopedActionTables);
        Check(localActionKit is { IsResolved: true, SourceWorldKey: 0x30 }
                && ReferenceEquals(localActionKit.Actions, actionLinkTable)
                && !ambiguousActionKit.IsResolved
                && ambiguousActionKit.Failure.Contains("multiple source worlds", StringComparison.Ordinal),
            "Action-kit joins should prefer the target world and refuse an ambiguous cross-world key fallback");
        var sharedActionKit = CinematicActionKitScopeResolver.Resolve(0x910, 0x50,
            new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
            {
                [(0, 0x910)] = actionLinkTable,
                [(0x40, 0x910)] = otherWorldActionTable
            });
        Check(sharedActionKit is { IsResolved: true, SourceWorldKey: 0 },
            "A shared-world action table should take precedence over an otherwise ambiguous source-world fallback");

        var mergeSourceKit = new Dictionary<int, CinematicAction>
        {
            [4] = new(byte.MaxValue, [new(new AnimationClip { Key = 0xA104 }, 0, 64, 1)]),
            [8] = new(byte.MaxValue, [new(new AnimationClip { Key = 0xA108 }, 0, 64, 1)])
        };
        var mergeActionTrack = new CinematicScriptEventTrack(0x30, 0x910, "Events", 0xA100,
            "time-scoped kit merge fixture", 4,
            [
                (0f, new CinematicLiteralCall(0x910, 0x3398, [0x912, 4, 8])),
                (.5f, new CinematicLiteralCall(0x910, 0x3391, [4])),
                (1f, new CinematicLiteralCall(0x910, 0x3391, [8])),
                (2f, new CinematicLiteralCall(0x910, 0x3399, [0x912, 3, 8])),
                (2.5f, new CinematicLiteralCall(0x910, 0x3391, [8])),
                (3f, new CinematicLiteralCall(0x910, 0x3391, [3]))
            ], []);
        var mergedActionLinks = CinematicEventActionLinkResolver.Analyze(mergeActionTrack,
            new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
            {
                [(0x30, 0x910)] = actionLinkTable,
                [(0x40, 0x912)] = mergeSourceKit
            });
        Check(mergedActionLinks.Resolved.Select(link => link.ActionIndex).SequenceEqual([4, 8, 8])
                && mergedActionLinks.Resolved[0].Action.Items.Single().Clip.Key == 0xA104
                && mergedActionLinks.Resolved[0].ActionTableWorldKey == 0x40
                && mergedActionLinks.Resolved[1].Action.Items.Single().Clip.Key == 0xA101
                && mergedActionLinks.Resolved[2].Action.Items.Single().Clip.Key == 0xA108
                && mergedActionLinks.Resolved[2].ActionTableWorldKey == 0x40
                && mergedActionLinks.Unresolved.Single().Reason.Contains("effective action table", StringComparison.Ordinal),
            "EVE ACTKitMerge should add missing slots, ACTKitMergeOver should replace/clear its range, and action links should retain source-world provenance");
        var linkedGameplayTrack = new CinematicEmbeddedEventTrack(0, 0x910, -1, 20, 2, 0)
        {
            Calls = mergeActionTrack.Calls.Take(2).ToArray()
        };
        var linkedGameplayAction = CinematicEventActionLinkResolver.AnalyzeCalls(linkedGameplayTrack.Calls,
            0x30, new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
            {
                [(0x30, 0x910)] = actionLinkTable,
                [(0x40, 0x912)] = mergeSourceKit
            });
        Check(linkedGameplayAction.Resolved.Single() is
                { ActionIndex: 4, ActionTableWorldKey: 0x40 }
                && linkedGameplayAction.Resolved[0].Action.Items.Single().Clip.Key == 0xA104,
            "Embedded EVE channels should follow an action-kit merge into the subsequent selected slot and preserve source-world provenance");
        Check(CinematicEngineFunctionCatalog.Describe(new CinematicLiteralCall(0x910, 0x3399,
                    [0x912, 3, 8])).Contains("replacing target slots", StringComparison.Ordinal),
            "ACTKitMergeOver should be described with its source-confirmed overwrite semantics");

        byte[] objectVariables = CreateObjectReferenceVariables("des_o_target", 0xBF000DA6);
        var references = PcObjectReferenceListReader.Read(objectVariables);
        Check(references.Length == 1 && references[0] == ("des_o_target", 0xBF000DA6, null),
            "Scalar script OVA references should retain their variable names and serialized GAO keys");
        byte[] objectReferenceArray = CreateObjectReferenceArrayVariables("des_o_actor_capa",
            [0x9E001C75u, 0x9E001C75u, 0x9E001C76u]);
        var indexedReferences = PcObjectReferenceListReader.Read(objectReferenceArray);
        Check(indexedReferences.SequenceEqual([
                ("des_o_actor_capa", 0x9E001C75u, (int?)0),
                ("des_o_actor_capa", 0x9E001C75u, (int?)1),
                ("des_o_actor_capa", 0x9E001C76u, (int?)2)]) ,
            "Script OVA object arrays should preserve zero-based slots, including repeated target keys");
        var objectReferenceArraySnapshot = PcVariableSnapshotReader.Read(objectReferenceArray, "model");
        Check(objectReferenceArraySnapshot.Length == 1
            && objectReferenceArraySnapshot[0].Value
                == "[0x9E001C75, 0x9E001C75, 0x9E001C76] (3 entries)",
            "Typed OVA snapshots should skip array dimension headers and display bounded object-reference values");
        byte[] modelObjectReferences = CreateObjectReferenceArrayVariables("des_o_actor_capa",
            [0x9E001C70u, 0x9E001C71u, 0x9E001C72u]);
        byte[] instanceObjectReferences = CreateObjectReferenceArrayVariables("DES_O_ACTOR_CAPA",
            [0x9E001C75u, 0x9E001C76u]);
        var effectiveObjectReferences = PcObjectReferenceListReader.ReadOverlayBlocks(
            [modelObjectReferences], instanceObjectReferences);
        Check(effectiveObjectReferences.SequenceEqual([
                new PcObjectReferenceValue("des_o_actor_capa", 0x9E001C75u, "instance", 0),
                new PcObjectReferenceValue("des_o_actor_capa", 0x9E001C76u, "instance", 1),
                new PcObjectReferenceValue("des_o_actor_capa", 0x9E001C72u, "model", 2)]),
            $"Effective script references should overlay prefixes and retain model tails; got {string.Join(", ", effectiveObjectReferences.Select(reference => $"{reference.VariableName}[{reference.ArrayIndex}]={reference.TargetKey:X8}/{reference.Source}"))}");
        var modelIntegerArray = PcVariableSnapshotReader.Read(CreateIntegerArrayVariables(
            "des_ai_special_flags", [2166, 12777, 0, 3]), "model")[0];
        var instanceIntegerArray = PcVariableSnapshotReader.Read(CreateIntegerArrayVariables(
            "des_ai_special_flags", [7, 8]), "instance")[0];
        Check(modelIntegerArray.IntegerArrayValues?.SequenceEqual([2166, 12777, 0, 3]) == true
            && modelIntegerArray.ArrayValueCount == 4,
            "Integer OVA arrays should retain typed bounded elements for source-backed script analysis");
        var overlayArrayOwner = new CinematicScriptOwner(0x100, 0, 0, 0x20, 0, 0, "array overlay",
            [], [], [modelIntegerArray, instanceIntegerArray]);
        var overlayArray = overlayArrayOwner.EffectiveIntegerArray("des_ai_special_flags");
        Check(overlayArray.SequenceEqual([
                new CinematicScriptIntegerArrayValue(0, 7, "instance"),
                new CinematicScriptIntegerArrayValue(1, 8, "instance"),
                new CinematicScriptIntegerArrayValue(2, 0, "model"),
                new CinematicScriptIntegerArrayValue(3, 3, "model")]),
            "Effective integer arrays should copy the instance prefix and retain the model tail with provenance");
        byte[] integerVariables = CreateScalarVariables("des_i_specialFlag_test1", PcVariableFormat.IntegerType,
            BitConverter.GetBytes(0x874));
        var variables = PcVariableSnapshotReader.Read(integerVariables, "instance");
        Check(variables.Length == 1 && variables[0].Name == "des_i_specialFlag_test1"
            && variables[0].Source == "instance" && variables[0].Value == "2164",
            "Script OVA snapshots should decode named scalar values from serialized storage");
        byte[] dialogueLineTable = CreateTextReferenceArray("des_at_lignes_texte",
            [(0xC1009667u, 32), (0xC1009667u, 33)]);
        var decodedDialogueLines = PcDialogueLineTableReader.ReadOverlay(dialogueLineTable, []) ?? [];
        Check(decodedDialogueLines.Length == 2
            && decodedDialogueLines[0] == new CinematicDialogueLine(0, 0xC1009667, 32)
            && decodedDialogueLines[1] == new CinematicDialogueLine(1, 0xC1009667, 33),
            "Dialogue line tables should expose validated type-44 text bank and entry pairs by slot");
        byte[] dialogueModelTables = CreateDialogueArrays(
            ("des_at_lignes_texte", 44, 2, [0xC1009667u, 32, 0xC1009667, 33]),
            ("des_ai_son_associe", PcVariableFormat.IntegerType, 2, [301u, 302]),
            ("ai_son_differents", PcVariableFormat.IntegerType, 2, [2u, 0]),
            ("des_ai_encart", PcVariableFormat.FloatType, 2,
                [(uint)BitConverter.SingleToInt32Bits(1), (uint)BitConverter.SingleToInt32Bits(0)]),
            ("des_af_volume_associe", PcVariableFormat.FloatType, 2,
                [(uint)BitConverter.SingleToInt32Bits(.7f), (uint)BitConverter.SingleToInt32Bits(.8f)]),
            ("des_af_near_associe", PcVariableFormat.FloatType, 2,
                [(uint)BitConverter.SingleToInt32Bits(4), (uint)BitConverter.SingleToInt32Bits(5)]),
            ("des_af_far_associe", PcVariableFormat.FloatType, 2,
                [(uint)BitConverter.SingleToInt32Bits(30), (uint)BitConverter.SingleToInt32Bits(40)]));
        byte[] dialogueInstanceOverride = CreateDialogueArrays(
            ("des_ai_son_associe", PcVariableFormat.IntegerType, 2, [401u, 402]));
        var decodedDialogueAudio = PcDialogueLineTableReader.ReadOverlay(dialogueModelTables,
            dialogueInstanceOverride) ?? [];
        Check(decodedDialogueAudio.Length == 2
            && decodedDialogueAudio[0] == new CinematicDialogueLine(0, 0xC1009667, 32,
                401, 2, 1, .7f, 4, 30)
            && decodedDialogueAudio[1].SoundId == 402 && decodedDialogueAudio[1].TextId == 33
            && decodedDialogueAudio[1].Far == 40,
            "Dialogue OVA arrays should decode per-line voice IDs, variant ranges, captions and audio distances with instance-over-model table overrides");
        byte[] helperTextArrays = CreateDialogueArrays(
            ("des_at_lignes_texte", 44, 1, [0xC1009667u, 32]),
            ("des_ai_son_associe", PcVariableFormat.IntegerType, 1, [401u]),
            ("des_ai_encart", PcVariableFormat.IntegerType, 1, [3u]),
            ("des_af_volume_associe", PcVariableFormat.FloatType, 1,
                [(uint)BitConverter.SingleToInt32Bits(.6f)]),
            ("des_af_near_associe", PcVariableFormat.FloatType, 1,
                [(uint)BitConverter.SingleToInt32Bits(2)]),
            ("des_af_far_associe", PcVariableFormat.FloatType, 1,
                [(uint)BitConverter.SingleToInt32Bits(18)]),
            ("des_f_duree_dialogues", PcVariableFormat.FloatType, 1,
                [(uint)BitConverter.SingleToInt32Bits(2.5f)]),
            ("des_ai_perso_localisation", PcVariableFormat.ObjectReferenceType, 1, [0xD0001234u]));
        var decodedHelperTextLines = PcDialogueLineTableReader.ReadOverlay(helperTextArrays, []) ?? [];
        Check(decodedHelperTextLines.Length == 1
            && decodedHelperTextLines[0] == new CinematicDialogueLine(0, 0xC1009667, 32,
                401, TextBox: 3, Volume: .6f, Near: 2, Far: 18, Duration: 2.5f, SpeakerKey: 0xD0001234),
            "Helper-text OVA rows should retain integer text-box indices, per-line duration and speaker references alongside voice metadata");
        var helperTextOwner = new CinematicScriptOwner(0xD0001230, 0, 0x350074D6, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "HLP_text_fixture.gao",
            ["helper_text_hlptxt_init", "helper_text_hlptxt_tests_depart", "helper_text_hlptxt_exec_ask_display"],
            [new("des_o_acteur_msg", 0xD0001231, "instance", "Message actor")],
            [new("des_o_acteur_msg", PcVariableFormat.ObjectReferenceType, "instance", "0xD0001231"),
             new("des_i_msg_ID", PcVariableFormat.IntegerType, "instance", "37"),
             new("des_i_msg_INT1", PcVariableFormat.IntegerType, "instance", "7"),
             new("des_i_auto_delay", PcVariableFormat.FloatType, "instance", "0")],
            decodedHelperTextLines);
        string[] helperTextFindings = CinematicScriptBehaviorAnalyzer.Analyze(helperTextOwner);
        Check(helperTextFindings.Any(finding => finding.Contains("msgID 37", StringComparison.Ordinal)
                && finding.Contains("Int1 mask 0x7", StringComparison.Ordinal))
            && helperTextFindings.Any(finding => finding.Contains("shared text/audio manager", StringComparison.Ordinal)
                && finding.Contains("sound duration", StringComparison.Ordinal))
            && helperTextFindings.Any(finding => finding.Contains("Line slot 0", StringComparison.Ordinal)
                && finding.Contains("text C1009667:32", StringComparison.Ordinal)
                && finding.Contains("speaker reference D0001234", StringComparison.Ordinal)
                && finding.Contains("duration 2.5 s", StringComparison.Ordinal)),
            $"Helper-text analysis should connect its message gate, text/audio timeline and effective line metadata: {string.Join(" | ", helperTextFindings)}");
        var helperTextFlagOwner = helperTextOwner with
        {
            CallbackNames = [.. helperTextOwner.CallbackNames,
                "helper_text_hlptxt_exec_fin_ou_abort", "helper_text_hlptxt_tests_wait"],
            Variables = [.. helperTextOwner.Variables,
                new("des_i_ripley", PcVariableFormat.IntegerType, "instance", "0"),
                new("des_i_SPFlag_associe_vrai", PcVariableFormat.IntegerType, "instance", "2201"),
                new("des_i_SPFlag_associe_faux", PcVariableFormat.IntegerType, "instance", "2202"),
                new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2020"),
                new("des_i_specialFlag_test2", PcVariableFormat.IntegerType, "instance", "12030")]
        };
        var helperTextFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(helperTextFlagOwner);
        Check(helperTextFlagAccesses.Any(access => access.Flag == 2020 && access.Access == "reads"
                && access.Description.Contains("requires set", StringComparison.Ordinal))
            && helperTextFlagAccesses.Any(access => access.Flag == 2030 && access.Access == "reads"
                && access.Description.Contains("requires clear", StringComparison.Ordinal))
            && helperTextFlagAccesses.Any(access => access.Flag == 2201 && access.Access == "writes")
            && helperTextFlagAccesses.Any(access => access.Flag == 2202 && access.Access == "writes"),
            "Helper-text flag analysis should link source polarity checks and conditional completion writers using effective OVA values");
        byte[] splitDialogueText = CreateDialogueArrays(
            ("des_at_lignes_texte", 44, 1, [0xC1009667u, 32]));
        byte[] splitDialogueSound = CreateDialogueArrays(
            ("des_ai_son_associe", PcVariableFormat.IntegerType, 1, [301u]));
        var mergedDialogueArrays = PcDialogueLineTableReader.ReadOverlayBlocks(
            new ReadOnlyMemory<byte>[] { splitDialogueText, splitDialogueSound }, ReadOnlyMemory<byte>.Empty) ?? [];
        Check(mergedDialogueArrays.Length == 1 && mergedDialogueArrays[0].TextId == 32
            && mergedDialogueArrays[0].SoundId == 301,
            "Dialogue arrays distributed across multiple model OVA blocks should merge into one slot table");
        byte[] malformedDialogueArray = CreateDialogueArrays(
            ("des_ai_son_associe", PcVariableFormat.IntegerType, 2, [301u, 302]));
        BinaryPrimitives.WriteUInt32LittleEndian(malformedDialogueArray.AsSpan(PcVariableFormat.BlockLengthSize),
            uint.MaxValue);
        bool rejectsMalformedDialogueArray = false;
        try { PcDialogueLineTableReader.ReadOverlay(malformedDialogueArray, []); }
        catch (InvalidDataException) { rejectsMalformedDialogueArray = true; }
        Check(rejectsMalformedDialogueArray,
            "Dialogue audio arrays should reject offsets outside the OVA storage block");
        var dialogueReceiver = new CinematicScriptOwner(0xBF00012E, 0, 0x350091B0, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "Cine_0002_Dialogues.gao", [], [],
            [new("des_i_priorite_par_defaut", PcVariableFormat.IntegerType, "model", "0")], decodedDialogueAudio);
        CinematicScriptOwner[] dialogueOwners = [dialogueReceiver];
        var slotMessage = new CinematicTimelineMessage(7, dialogueReceiver.OwnerKey, 0, 0,
            default, default, 0, 0, 1, 0, 30);
        var slotResolution = CinematicDialogueMessageResolver.Resolve(slotMessage, dialogueOwners,
            dialogueReceiver.WorldKey);
        var advanceResolution = CinematicDialogueMessageResolver.Resolve(slotMessage with { Int1 = 200 },
            dialogueOwners, dialogueReceiver.WorldKey);
        Check(slotResolution is { Kind: CinematicDialogueMessageResolutionKind.TextReference, Slot: 1,
                TextBankKey: 0xC1009667, TextId: 33, EffectivePriority: 5000,
                Line.SoundId: 402, Line.Volume: .8f, Line.Near: 5, Line.Far: 40 }
            && advanceResolution.Kind == CinematicDialogueMessageResolutionKind.AdvanceRequest,
            "Dialogue type-30 requests should resolve a context receiver, selected line audio metadata and the source-default priority, while advance requests remain cursor-dependent");
        string[] dialogueDetails = CinematicDialogueMessageResolver.DescribeSelectedLine(slotMessage, slotResolution);
        var explicitPriority = CinematicDialogueMessageResolver.Resolve(slotMessage with { Int2 = 700 },
            dialogueOwners, dialogueReceiver.WorldKey);
        var ignoredPriority = CinematicDialogueMessageResolver.Resolve(slotMessage with { Int2 = -1 },
            dialogueOwners, dialogueReceiver.WorldKey);
        Check(dialogueDetails.Any(detail => detail.Contains("Queue priority 5000", StringComparison.Ordinal)
                && detail.Contains("receiver default", StringComparison.Ordinal))
            && dialogueDetails.Any(detail => detail.Contains("Associated sound parameter 402", StringComparison.Ordinal))
            && dialogueDetails.Any(detail => detail.Contains("Associated audio volume 0.8", StringComparison.Ordinal))
            && explicitPriority.EffectivePriority == 700
            && ignoredPriority.EffectivePriority == null
            && CinematicDialogueMessageResolver.DescribeSelectedLine(slotMessage with { Int2 = -1 }, ignoredPriority)
                .Any(detail => detail.Contains("ignored", StringComparison.Ordinal)),
            "Type-30 inspection should distinguish explicit/default/ignored priorities and expose source-decoded voice metadata without simulating queue delivery");
        Check(CinematicEngineFunctionCatalog.NameFor(0x1FD8) == "OBJ_FlagInvisibleSet"
            && CinematicEngineFunctionCatalog.NameFor(0x3D56) == "XMEC_OnOff",
            "Engine selectors should resolve through the shared function catalog");
        string pauseWorldDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x1F4A, [2, 0x20]));
        string resumeWorldDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x1F4B, [2, 0x20]));
        string specialTimeDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x1F53, [0x3F800000, 0x3D888889]));
        Check(pauseWorldDescription.Contains("GAO flags match 0x00000020", StringComparison.Ordinal)
                && pauseWorldDescription.Contains("flags2 bit 0x04000000", StringComparison.Ordinal)
                && resumeWorldDescription.Contains("activation gate 0x00400000", StringComparison.Ordinal)
                && specialTimeDescription.Contains("ENG_gf_SpecialTime", StringComparison.Ordinal)
                && specialTimeDescription.Contains("special-time steps", StringComparison.Ordinal)
                && CinematicEngineFunctionCatalog.Describe(new(1, 0x1F53, [1])) == "",
            "World pause, resume and special-time event commands should expose source-verified world-scheduler effects and reject incomplete payloads");
        CinematicScriptNamedReference[] namedReferences =
        [
            new(0x100, 0x20, "local object", IsObject: true),
            new(0x100, 0x20, "local brain", IsObject: false),
            new(0x200, 0x20, "other-world duplicate", IsObject: true),
            new(0x200, 0x21, "unique linked object", IsObject: true),
            new(0x300, 0x22, "ambiguous candidate A", IsObject: true),
            new(0x400, 0x22, "ambiguous candidate B", IsObject: true)
        ];
        Check(CinematicScriptReferenceNameResolver.Resolve(0x20, 0x100, namedReferences)?.Name == "local object"
            && CinematicScriptReferenceNameResolver.Resolve(0x21, 0x100, namedReferences)?.Name == "unique linked object"
            && CinematicScriptReferenceNameResolver.Resolve(0x22, 0x100, namedReferences) == null
            && CinematicScriptReferenceNameResolver.Resolve(1, 0x100,
                [new(0x100, 1, "not an archive object", IsObject: true)]) == null,
            "Archive reference labels should prefer the current world, use only unique external matches, and leave cross-world key collisions unresolved");
        var specialSlotOwner = new CinematicScriptOwner(1, 0, 0, 0x600,
            uint.MaxValue, uint.MaxValue, "not an archive owner", [], [], []);
        Check(CinematicScriptOwnerResolver.Resolve(1, 0x600, [specialSlotOwner]) == null,
            "Runtime AI_MainActor slots must not resolve to script owners whose numeric key happens to match");
        Check(CinematicObjectKey.SpecialActorSlotName(1) == "AI_MainActor slot 1"
                && CinematicObjectKey.SpecialActorSlotName(2) == "AI_MainActor slot 2"
                && CinematicObjectKey.SpecialActorSlotName(3) == null,
            "AI object references should distinguish the two runtime actor slots from archive objects");
        var namedReferenceDocument = new SceneDocument();
        var namedReferenceWorld = new WorldData { Key = 0x500 };
        var actorRoot = new SceneObject { GaoKey = 0x501, Name = "scripted actor root" };
        var actorMember = new SceneObject { GaoKey = 0x502, Name = "scripted actor component" };
        namedReferenceWorld.Actors.Add(new SceneActor { Root = actorRoot, Members = [actorMember] });
        namedReferenceDocument.Worlds.Add(namedReferenceWorld);
        CinematicScriptNamedReference[] actorReferenceCatalog =
            CinematicScriptNamedReferenceCatalog.ForDocument(namedReferenceDocument);
        Check(CinematicScriptReferenceNameResolver.Resolve(0x501, 0x500, actorReferenceCatalog)?.Name
                == "scripted actor root"
            && CinematicScriptReferenceNameResolver.Resolve(0x502, 0x500, actorReferenceCatalog)?.Name
                == "scripted actor component",
            "Script reference catalogs should resolve actor roots and component meshes alongside standalone world objects");
        CinematicScriptOwnerReferenceCandidate[] scriptOwnerCandidates =
        [
            new("FF000001", 0x100, 0x20, "local script owner"),
            new("FF000001", 0x200, 0x20, "other-world duplicate"),
            new("FF000002", 0x100, 0x20, "local script owner"),
            new("FF000002", 0x200, 0x21, "unique linked script owner")
        ];
        var localScriptOwner = CinematicScriptOwnerReferenceResolver.Resolve("FF000001", 0x100,
            0x20, scriptOwnerCandidates);
        var ambiguousScriptOwner = CinematicScriptOwnerReferenceResolver.Resolve("FF000001", 0x300,
            0x20, scriptOwnerCandidates);
        var uniqueExternalScriptOwner = CinematicScriptOwnerReferenceResolver.Resolve("FF000001", 0x300,
            0x21, scriptOwnerCandidates);
        Check(localScriptOwner.IsResolved && localScriptOwner.Owner?.WorldKey == 0x100
            && localScriptOwner.Status == "same container and world"
            && !ambiguousScriptOwner.IsResolved
            && ambiguousScriptOwner.Status == "ambiguous across loaded worlds"
            && ambiguousScriptOwner.Candidates.Length == 2,
            "Archive script-owner reference joins should prefer the source container/world and retain cross-world key collisions as ambiguous");
        Check(uniqueExternalScriptOwner.IsResolved && uniqueExternalScriptOwner.Owner?.OwnerName == "unique linked script owner"
            && uniqueExternalScriptOwner.Status == "unique across loaded worlds",
            "An external script-owner reference should resolve only when its key identifies one owner across the loaded archive");
        CinematicScriptOwner[] duplicateLocalOwners =
        [
            new(0x20, 1, 2, 0x100, 0, 0, "local candidate A", [], [], []),
            new(0x20, 3, 4, 0x100, 0, 0, "local candidate B", [], [], []),
            new(0x20, 5, 6, 0x200, 0, 0, "external candidate", [], [], [])
        ];
        Check(CinematicScriptOwnerResolver.Resolve(0x20, 0x100, duplicateLocalOwners) == null
            && CinematicScriptOwnerResolver.Resolve(0x20, 0x300, duplicateLocalOwners) == null,
            "Scene-scope owner lookup should leave duplicate local and cross-world owner keys unresolved rather than selecting the first record");
        string xmecDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x3D56, [1, 1]));
        Check(CinematicEngineFunctionCatalog.Describe(new(1, 0x1FD8, [1])) == "hide target"
            && xmecDescription.Contains("enable XMEC modifier type 10", StringComparison.Ordinal)
            && xmecDescription.Contains("user ID 1; Iris ignores this parameter", StringComparison.Ordinal)
            && !xmecDescription.Contains("mode", StringComparison.Ordinal),
            "Supported engine calls should show source-verified effects beside their raw arguments");
        Check(CinematicEngineFunctionCatalog.Describe(new(1, 0x1FC3, [0xFFFF, 0xFFFF]))
                .Contains("set target custom bits 0xFF00, clear 0xFF00", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x1FDB, [0x9E000123]))
                .Contains("copy the target's absolute transform from object 9E000123", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x1FF9, [uint.MaxValue, 7]))
                .Contains("target actor bone display-list order to 7", StringComparison.Ordinal),
            "Object script calls should describe source-verified custom-bit, transform-copy and bone-order effects");
        Check(CinematicEngineFunctionCatalog.Describe(new(0x3500BA1C, 0x3CF3, [7, 1, 0]))
                .Contains("enable remanence", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x3500BA33, 0x3CF3, [1, 1, 0]))
                .Contains("enable motion blur", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x3CF3, [3, 1, 0])) == "",
            "After Effects master events should name only modes with source-backed semantics");
        string actionFrequencyDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x339C, [200, 64]));
        string alphaThresholdDescription = CinematicEngineFunctionCatalog.Describe(new(0xABCD1234, 0x3AA2, [2, 1, 255]));
        string hapticPulseDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x1B63, [100, 8]));
        string hapticEnableDescription = CinematicEngineFunctionCatalog.Describe(new(1, 0x1B62, [4]));
        Check(actionFrequencyDescription
                .Contains("set target action 200 and restart it at frequency 64", StringComparison.Ordinal)
            && alphaThresholdDescription
                .Contains("material ABCD1234, submaterial 2, texture layer 1 to 255", StringComparison.Ordinal)
            && hapticPulseDescription
                .Replace(',', '.').Contains("intensity 100 for 0.256 seconds", StringComparison.Ordinal)
            && hapticEnableDescription
                .Replace(',', '.').Contains("0.128 seconds when enabled", StringComparison.Ordinal),
            $"Animation/material/haptic descriptions mismatch: '{actionFrequencyDescription}' | '{alphaThresholdDescription}' | '{hapticPulseDescription}' | '{hapticEnableDescription}'");
        Check(CinematicEngineFunctionCatalog.Describe(new(1, 0x3AA2, [2, 1])) == "",
            "Alpha-threshold calls with incomplete argument tuples must remain undescribed");
        uint particleRate = BitConverter.SingleToUInt32Bits(12.5f);
        uint particleOffset = BitConverter.SingleToUInt32Bits(2.25f);
        Check(CinematicEngineFunctionCatalog.Describe(new(0x3500A264, 0x20D0, [0x3500A265, particleRate]))
                .Contains("object 3500A265 at 12.5 particles/second", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x3500A264, 0x20D2, [7, particleRate]))
                .Contains("generator ID 7 frequency to 12.5 particles/second", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x3500A264, 0x20D6, [0x40, 0x80]))
                .Contains("set PAG flag bits 0x40 and clear 0x80", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x3500A264, 0x20D8, [22, particleOffset]))
                .Contains("Friction (22) to 2.25", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x3500A264, 0x2109, [0x3500A266]))
                .Contains("object-vertex emission matrix reference", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x20D2, [7])) == "",
            "PAG script calls should decode source-backed generator, flag, float-parameter and matrix-binding effects");
        var haloEffect = new CinematicScriptOwner(0xD0002E30, 0, 0x62001249, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "generic halo effect",
            ["SFX_HaloSimple_init", "SFX_HaloSimple_move"],
            [new("des_o_gaomat", 0xABCD1234, "instance", "material source")],
            [new("des_o_gaomat", PcVariableFormat.ObjectReferenceType, "instance", "0xABCD1234"),
             new("des_i_Type", PcVariableFormat.IntegerType, "instance", "4"),
             new("des_f_Radius", PcVariableFormat.FloatType, "instance", "3.5"),
             new("des_f_Offset", PcVariableFormat.FloatType, "instance", "0.25"),
             new("des_f_Extrude", PcVariableFormat.FloatType, "instance", "7"),
             new("des_i_canmove", PcVariableFormat.IntegerType, "instance", "0"),
             new("des_i_clignote", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_f_time_init", PcVariableFormat.FloatType, "instance", "2"),
             new("des_f_freq", PcVariableFormat.FloatType, "instance", "0.5"),
             new("des_f_speed_clignote", PcVariableFormat.FloatType, "instance", "4")]);
        string[] haloFindings = CinematicScriptBehaviorAnalyzer.Analyze(haloEffect);
        Check(haloFindings.Any(finding => finding.Contains("type 4 uses the default GFX effect 21 setup", StringComparison.Ordinal)
                && finding.Contains("sight direction", StringComparison.Ordinal))
            && haloFindings.Any(finding => finding.Contains("material source", StringComparison.Ordinal)
                && finding.Contains("waits 2 seconds", StringComparison.Ordinal))
            && haloFindings.Any(finding => finding.Contains("des_i_canmove=0", StringComparison.Ordinal)
                && finding.Contains("does not remove the separately created effect", StringComparison.Ordinal))
            && haloFindings.Any(finding => finding.Contains("blends the packed effect color", StringComparison.Ordinal)
                && finding.Contains("frequency (0.5)", StringComparison.Ordinal)
                && finding.Contains("speed (4)", StringComparison.Ordinal)),
            "SFX_HaloSimple analysis should derive effect configuration, material binding, inactivity, and blink behavior from effective OVA values");
        var disabledHalo = haloEffect with
        {
            Variables = [new("des_i_Type", PcVariableFormat.IntegerType, "instance", "0")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(disabledHalo)
                .Any(finding => finding.Contains("des_i_Type=0", StringComparison.Ordinal)
                    && finding.Contains("does not create a GFX effect", StringComparison.Ordinal)),
            "An explicitly disabled SFX_HaloSimple type should not be described as a created effect");
        var moveOnlyHalo = haloEffect with { CallbackNames = ["SFX_HaloSimple_move"] };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(moveOnlyHalo)
                .Any(finding => finding.Contains("without its init callback", StringComparison.Ordinal)
                    && finding.Contains("does not prove that a GFX effect instance exists", StringComparison.Ordinal)),
            "A move callback without init must not imply that the native effect was created");
        var negativeTypeHalo = haloEffect with
        {
            Variables = [new("des_i_Type", PcVariableFormat.IntegerType, "instance", "-1")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(negativeTypeHalo)
                .Any(finding => finding.Contains("negative type -1", StringComparison.Ordinal)
                    && finding.Contains("sight-direction update is only enabled for type values above 3", StringComparison.Ordinal)),
            "Negative halo type values should follow the source default initialization branch without claiming sight tracking");
        var cameraScript = new CinematicScriptOwner(0xD0002E31, 0, 0x8D005218, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "camera controller",
            ["gencam_mario_gcm_first", "gencam_mario_gcm_exec_activation", "gencam_mario_gcm_normal"],
            [new("des_o_actor_detected", 1, "model", "main actor"),
             new("des_o_actor_CapaSwitch", 0, "model", "null")],
            [new("des_ai_special_flags", PcVariableFormat.IntegerType, "model", "[2166, 12777] (2 entries)",
                 [2166, 12777], 2),
             new("des_ai_special_flags_off", PcVariableFormat.IntegerType, "model", "[300, 10301] (2 entries)",
                 [300, 10301], 2),
             new("des_i_fight", PcVariableFormat.IntegerType, "model", "0")]);
        var cameraFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(cameraScript);
        string[] cameraFindings = CinematicScriptBehaviorAnalyzer.Analyze(cameraScript);
        Check(cameraFlagAccesses.Any(access => access.Flag == 2166 && access.Description.Contains("requires set", StringComparison.Ordinal))
            && cameraFlagAccesses.Any(access => access.Flag == 2777 && access.Description.Contains("requires clear", StringComparison.Ordinal))
            && cameraFlagAccesses.Any(access => access.Flag == 300 && access.Description.Contains("requires clear", StringComparison.Ordinal))
            && cameraFlagAccesses.Any(access => access.Flag == 301 && access.Description.Contains("requires set", StringComparison.Ordinal))
            && cameraFindings.Any(finding => finding.Contains("BV-pivot collision", StringComparison.Ordinal))
            && cameraFindings.Any(finding => finding.Contains("capability 0x4 then forces readiness", StringComparison.Ordinal))
            && cameraFindings.Any(finding => finding.Contains("normal callback contains the ongoing camera/target update", StringComparison.Ordinal)),
            "General camera activation should expose encoded on/off flag polarity, collision/capability gates, and the non-emulated normal update");
        var sectorActivator = new CinematicScriptOwner(0xD0002E32, 0, 0x5E000487, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "sector activator",
            ["Activator_act_init", "Activator_act_loop", "Activator_act_afterrec", "Activator_act_cutter"],
            [new("des_o_detect", 0x9E000100, "instance", "detection volume.gao"),
             new("des_o_cacher_groupe_de", 0x9E000101, "instance", "sector group.gao"),
             new("des_ao_portes", 0x9E000102, "instance", "door.gao", 0),
             new("des_ao_virer_les_secteurs", 0x9E000110, "instance", "linked sector.gao", 0),
             new("des_ao_montrer_les_secteurs", 0x9E000110, "instance", "linked sector.gao", 0)],
            [new("des_i_register", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_i_auto_hide", PcVariableFormat.IntegerType, "instance", "1"),
             new("i_forceshow", PcVariableFormat.IntegerType, "instance", "0"),
             new("des_f_distcut", PcVariableFormat.FloatType, "instance", "25"),
              new("des_ao_virer_les_secteurs", PcVariableFormat.ObjectReferenceType, "model", "[0x9E000110] (1 entries)", null, 1),
              new("des_ao_montrer_les_secteurs", PcVariableFormat.ObjectReferenceType, "model", "[0x9E000110] (1 entries)", null, 1)]);
        var linkedSectorActivator = new CinematicScriptOwner(0x9E000110, 0, 0x5E000487, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "linked sector", ["Activator_act_init", "Activator_act_afterrec"], [],
            [new("i_cptact", PcVariableFormat.IntegerType, "instance", "0"),
             new("i_forceshow", PcVariableFormat.IntegerType, "instance", "0")]);
        string[] activatorFindings = CinematicScriptBehaviorAnalyzer.Analyze(sectorActivator,
            [sectorActivator, linkedSectorActivator]);
        Check(activatorFindings.Any(finding => finding.Contains("registers this owner as a render sector", StringComparison.Ordinal))
            && activatorFindings.Any(finding => finding.Contains("BV-pivot collision test", StringComparison.Ordinal)
                && finding.Contains("detection volume.gao", StringComparison.Ordinal))
            && activatorFindings.Any(finding => finding.Contains("auto-hide", StringComparison.Ordinal)
                && finding.Contains("visibility counter", StringComparison.Ordinal))
            && activatorFindings.Any(finding => finding.Contains("des_ao_virer_les_secteurs", StringComparison.Ordinal)
                && finding.Contains("increments the referenced Activator_act owner's i_cptact", StringComparison.Ordinal))
            && activatorFindings.Any(finding => finding.Contains("des_ao_montrer_les_secteurs", StringComparison.Ordinal)
                && finding.Contains("sets the referenced Activator_act owner's i_forceshow", StringComparison.Ordinal))
            && activatorFindings.Any(finding => finding.Contains("viewer distance threshold 25", StringComparison.Ordinal)
                && finding.Contains("door control flag 0x200000", StringComparison.Ordinal)),
            "Sector activator analysis should join serialized sector-list references to compatible Activator_act owners and derive their callback fields from source offsets");
        var sameKeyInOtherWorld = linkedSectorActivator with { WorldKey = 0xD0002E25 };
        string[] unresolvedSectorLinkFindings = CinematicScriptBehaviorAnalyzer.Analyze(sectorActivator,
            [sectorActivator, sameKeyInOtherWorld]);
        Check(unresolvedSectorLinkFindings.Any(finding => finding.Contains("no referenced target was resolved to an Activator_act owner", StringComparison.Ordinal))
            && !unresolvedSectorLinkFindings.Any(finding => finding.Contains("increments the referenced Activator_act owner's i_cptact", StringComparison.Ordinal)),
            "Sector-list field meanings should not cross world boundaries when GAO keys collide");
        var boundingVolumeActivation = new CinematicScriptOwner(0x9E000120, 0, 0x7100F56F, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "volume activation fixture",
            ["BV_Active_Desactive_BV_AD", "BV_Active_Desactive_BV_In",
             "BV_Active_Desactive_BV_Out", "BV_Active_Desactive_BV_CBits_In",
             "BV_Active_Desactive_BV_CBits_Out"],
            [new("o_BV_a_qui", 0x9E000121, "instance", "trigger volume.gao"),
             new("acteur_a_detecter", 0x9E000122, "instance", "Jade.gao"),
             new("object_a_activer1", 0x9E000124, "instance", "robot.gao", 0)],
            [new("i_detection_type", PcVariableFormat.IntegerType, "instance", "1"),
             new("i_bv_pivot", PcVariableFormat.IntegerType, "instance", "0"),
             new("o_BV_a_qui", PcVariableFormat.ObjectReferenceType, "instance", "0x9E000121"),
             new("acteur_avec_CBit", PcVariableFormat.IntegerType, "instance", "256"),
             new("active_dans_bv", PcVariableFormat.IntegerType, "instance", "1"),
             new("object_a_activer1", PcVariableFormat.ObjectReferenceType, "instance", "0x9E000124"),
             new("ne_fait_pas_l_activation", PcVariableFormat.IntegerType, "instance", "0"),
             new("ia_reinit1", PcVariableFormat.IntegerType, "instance", "1"),
             new("rend_invisible_aussi", PcVariableFormat.IntegerType, "instance", "1"),
             new("ne_fait_qu_activer", PcVariableFormat.IntegerType, "instance", "1"),
             new("se_desactive_apres", PcVariableFormat.IntegerType, "instance", "1")]);
        string[] activationFindings = CinematicScriptBehaviorAnalyzer.Analyze(boundingVolumeActivation);
        Check(activationFindings.Any(finding => finding.Contains("selects the custom-bit overlap callback route", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("mask 0x00000100", StringComparison.Ordinal)
                && finding.Contains("not an object reference", StringComparison.Ordinal)
                && finding.Contains("tests flags2", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("configured-actor In/Out callbacks", StringComparison.Ordinal)
                && finding.Contains("selects the custom-bit route", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("In callback writes the target's inactive flag as true", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("reinitializes the AI variables", StringComparison.Ordinal)
                && finding.Contains("robot.gao", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("matching group operation", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("does not switch to the paired In/Out callback", StringComparison.Ordinal))
            && activationFindings.Any(finding => finding.Contains("does not run the collision tests", StringComparison.Ordinal)),
            "Bounding-volume activation should derive detection routes, target state, AI reset, visibility and callback switching from source-confirmed OVA fields");
        var actorRouteActivation = boundingVolumeActivation with
        {
            Variables = boundingVolumeActivation.Variables.Select(variable =>
                variable.Name == "i_detection_type" ? variable with { Value = "0" } : variable).ToArray()
        };
        string[] actorRouteFindings = CinematicScriptBehaviorAnalyzer.Analyze(actorRouteActivation);
        Check(actorRouteFindings.Any(finding => finding.Contains("selects the configured-actor collision callback route", StringComparison.Ordinal))
            && actorRouteFindings.Any(finding => finding.Contains("COLBVObjectObject", StringComparison.Ordinal))
            && actorRouteFindings.Any(finding => finding.Contains("custom-bit callbacks", StringComparison.Ordinal)
                && finding.Contains("selects the configured-actor route", StringComparison.Ordinal)),
            "The initialization selector should activate only the configured-actor route when i_detection_type is zero");
        var incompleteBoundingVolumeActivation = boundingVolumeActivation with
        {
            CallbackNames = ["BV_Active_Desactive_BV_AD", "BV_Active_Desactive_BV_In"],
            Variables = [],
            ObjectReferences = []
        };
        string[] incompleteBoundingVolumeFindings = CinematicScriptBehaviorAnalyzer.Analyze(incompleteBoundingVolumeActivation);
        Check(incompleteBoundingVolumeFindings.Any(finding => finding.Contains("i_detection_type", StringComparison.Ordinal)
                && finding.Contains("unavailable", StringComparison.Ordinal))
            && incompleteBoundingVolumeFindings.Any(finding => finding.Contains("active_dans_bv is unavailable", StringComparison.Ordinal))
            && incompleteBoundingVolumeFindings.Any(finding => finding.Contains("writes are suppressed is unknown", StringComparison.Ordinal)),
            "Bounding-volume activation should not infer runtime configuration when OVA values are missing");
        var unconfiguredSectorActivator = sectorActivator with
        {
            CallbackNames = ["Activator_act_init", "Activator_act_afterrec"],
            Variables = [],
            ObjectReferences = []
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(unconfiguredSectorActivator)
                .Any(finding => finding.Contains("des_i_register", StringComparison.Ordinal)
                    && finding.Contains("unknown", StringComparison.Ordinal))
            && CinematicScriptBehaviorAnalyzer.Analyze(unconfiguredSectorActivator)
                .Any(finding => finding.Contains("reference unavailable", StringComparison.Ordinal)),
            "Sector activator analysis should preserve unknown behavior when effective OVA values are absent");
        var gatedOwner = new CinematicScriptOwner(0x100, 0, 0x350091C3, 0x10,
            uint.MaxValue, uint.MaxValue, "cine gate fixture", ["Cine_Events_cine_tests_depart"], [],
            [new("des_i_specialFlag_test1", 0, "instance", "2166"),
             new("des_i_specialFlag_test2", 0, "instance", "12167")]);
        var identityFlagOwner = gatedOwner with { SerializedGaoFlags = 0x0800F060 };
        CinematicScriptBranchConditionSource[] identityFlagBranches =
        [
            new("if", "AI_EvalFunc_OBJ_FlagsIdentityTest_C(AI_gpst_CurrentGameObject, 0x2000000)", 10),
            new("if", "!AI_EvalFunc_OBJ_FlagsIdentityTest_C(AI_gpst_CurrentGameObject, 0x100)", 11),
            new("else", "AI_EvalFunc_OBJ_FlagsIdentityTest_C(AI_gpst_CurrentGameObject, 0x2000000)", 12),
            new("if", "AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x100)", 13),
            new("if", "!AI_EvalFunc_OBJ_CapaTest_C(AI_gpst_CurrentGameObject, 256)", 14),
            new("if", "AI_EvalFunc_OBJ_CapaTest_C(otherObject, 0x100)", 15)
        ];
        string[] identityFlagFindings = CinematicScriptBranchAnalyzer.Describe(
            identityFlagOwner, identityFlagBranches);
        Check(identityFlagFindings.Length == 5
            && identityFlagFindings.Any(finding => finding.Contains("0x02000000 makes this if branch false",
                StringComparison.Ordinal))
            && identityFlagFindings.Any(finding => finding.Contains("0x00000100 makes this if branch true",
                StringComparison.Ordinal))
            && identityFlagFindings.Any(finding => finding.Contains("0x02000000 makes this else branch true",
                StringComparison.Ordinal))
            && identityFlagFindings.Count(finding => finding.Contains("runtime capability test", StringComparison.Ordinal)) == 2
            && identityFlagFindings.Any(finding => finding.Contains("mask 0x0100", StringComparison.Ordinal)
                && finding.Contains("any masked capability bit is set", StringComparison.Ordinal)
                && finding.Contains("branch result is unknown", StringComparison.Ordinal))
            && identityFlagFindings.Any(finding => finding.Contains("negated runtime capability test", StringComparison.Ordinal))
            && identityFlagFindings.Where(finding => finding.Contains("identity-flag check", StringComparison.Ordinal))
                .All(finding => finding.Contains("runtime flag changes", StringComparison.Ordinal)),
            "Source branch analysis should evaluate saved identity flags but leave runtime capability predicates unresolved");
        (float Time, CinematicLiteralCall Call)[] flagWrites =
        [
            (2, new(0x100, 0x2916, [2166])),
            (4, new(0x100, 0x2917, [2167]))
        ];
        var gateSnapshotBeforeSecondWrite = CinematicTimelineFlagAnalyzer.Analyze(gatedOwner, flagWrites, 3);
        var gateSnapshotAfterWrites = CinematicTimelineFlagAnalyzer.Analyze(gatedOwner, flagWrites, 5);
        float? firstSatisfiedStartFlagTime = CinematicTimelineFlagAnalyzer.FindFirstSatisfiedStartFlagTime(
            gatedOwner, flagWrites);
        (float Time, CinematicLiteralCall Call)[] conflictingFlagWrites =
        [
            (2, new(0x100, 0x2916, [2166])),
            (4, new(0x100, 0x2917, [2167])),
            (4, new(0x100, 0x2916, [2166])),
            (4, new(0x100, 0x2917, [2166])),
            (5, new(0x100, 0x2916, [2166]))
        ];
        float? firstUnambiguousStartFlagTime = CinematicTimelineFlagAnalyzer.FindFirstSatisfiedStartFlagTime(
            gatedOwner, conflictingFlagWrites);
        Check(gateSnapshotBeforeSecondWrite.Length == 2
            && gateSnapshotBeforeSecondWrite.Single(gate => gate.Flag == 2166).Satisfied == true
            && gateSnapshotBeforeSecondWrite.Single(gate => gate.Flag == 2167) is
                { CurrentSet: null, NextWriteTime: 4, Satisfied: null }
            && gateSnapshotAfterWrites.Single(gate => gate.Flag == 2166).Satisfied == true
            && gateSnapshotAfterWrites.Single(gate => gate.Flag == 2167) is
                { CurrentSet: false, Satisfied: true }
            && firstSatisfiedStartFlagTime == 4
            && firstUnambiguousStartFlagTime == 5,
            "Cinematic start-gate snapshots should preserve unknown/conflicting state and find the first unambiguous matching time");
        var actionKitCall = CinematicLiteralCall.Decode(CreateCallPayload(0x3398,
            (0x3500A57E, 0x01000028), (200, 0x01000021), (50, 0x01000021)), 0x9E001C75, 0);
        Check(actionKitCall is { Target: 0x9E001C75, Selector: 0x3398 }
            && actionKitCall.Arguments.SequenceEqual([0x3500A57Eu, 200u, 50u])
            && CinematicEngineFunctionCatalog.Describe(actionKitCall)
                .Contains("reversed action range 200–50", StringComparison.Ordinal),
            "Source event tracks should decode object-reference arguments and identify a reversed ACTKitMerge range");
        var actionSetCall = new CinematicLiteralCall(0x9E001C75, 0x3391, [200, 0]);
        Check(CinematicEngineFunctionCatalog.Describe(actionSetCall)
                .Contains("set target action 200", StringComparison.Ordinal),
            "ACTActionSet should have a source-backed action and clock-reset description");
        Check(CinematicEngineFunctionCatalog.Describe(new(1, 0x339B, [200, 12]))
                .Contains("action clock at frame 12", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x32C9, [2, 64]))
                .Contains("animation track 2 frequency to 64", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x2C24, [0x10, 0x04]))
                .Contains("set collision activation bits 0x10, clear 0x4", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x1FF1, [0x100, 0x20]))
                .Contains("add draw-mask bits 0x100, remove 0x20", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x3A9E, [0x3500DA08, 2, 1]))
                .Contains("enable multi-texture level 2", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x3AA0, [0x3500DA08, 1, 248]))
                .Contains("to 248 (stored in steps of 8)", StringComparison.Ordinal),
            "Action, frequency, collision and material-state calls should expose source-backed parameter effects");
        Check(CinematicEngineFunctionCatalog.Describe(new(1, 0x3BC4, [1]))
                == "hide every object in the target group"
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x3BC5, [0]))
                == "clear inactive state on every object in the target group",
            "Group visibility and activity calls should describe their propagation to member objects");
        Check(CinematicEngineFunctionCatalog.Describe(new(0x100, 0x2F1A, [2]))
                .Contains("play sound modifier 2 attached to the target object", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x2F1C, [uint.MaxValue]))
                .Contains("stop all sound modifiers", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x2F1A, [unchecked((uint)-10)]))
                .Contains("ignored by the engine", StringComparison.Ordinal),
            "Sound-modifier commands should distinguish one modifier, all modifiers and the native no-op sentinel");
        Check(CinematicEngineFunctionCatalog.Describe(new(0x100, 0x2F48, [0, 0x08000000]))
                .Contains("set runtime flags 0x8000000", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x2F49, [uint.MaxValue, 0x08000000]))
                .Contains("clear runtime flags 0x8000000 on all sound modifiers", StringComparison.Ordinal),
            "Sound-modifier flag commands should expose the selected modifier scope and mask operation");
        Check(CinematicEngineFunctionCatalog.Describe(new(0x100, 0x3B64, [4]))
                .Contains("start embedded event track 4", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x3B64, [4]))
                .Contains("not restarted", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x3B69, [2]))
                .Contains("reset embedded event track 2", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(0x100, 0x3B62, []))
                .Contains("pause every embedded event track", StringComparison.Ordinal),
            "TRA commands should describe indexed/all-track start, pause and reset behavior without executing tracks");
        var eventTrackDocument = new SceneDocument();
        var eventTrackWorld = new WorldData { Key = 0x10 };
        eventTrackWorld.Objects.Add(new SceneObject { GaoKey = 0x300, Name = "nested event target" });
        eventTrackDocument.Worlds.Add(eventTrackWorld);
        eventTrackDocument.CinematicEmbeddedEventTracks[(0x10, 0x100)] =
        [
            new(0x100, "static prop without brain", 0x10, "Events", 0x200,
                [new(0, 0x400, 1, 0, 30, 3.5f)]),
            new(0x100, "static prop without brain", 0x10, "SceneEvents", 0x201,
                [new(0, 0, -1, 0x8000, 12, 1.25f), new(1, 0x300, 2, 0x8120, 8, 2.5f)])
        ];
        eventTrackDocument.CinematicEmbeddedEventTracks[(0x10, 0x101)] =
        [new(0x101, "actor animation only", 0x10, "Events", 0x202,
            [new(0, 0, -1, 0x8000, 12, 1.25f)])];
        eventTrackDocument.CinematicEmbeddedEventTracks[(0x20, 0x101)] =
        [new(0x101, "other-world script EVE", 0x20, "SceneEvents", 0x203,
            [new(0, 0, -1, 0x8000, 12, 1.25f)])];
        string? indexedTrackDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x100, 0x3B64, [1]), eventTrackDocument, 0x10);
        string? allTrackDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x100, 0x3B60, []), eventTrackDocument, 0x10);
        string? missingTrackDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x101, 0x3B64, [0]), eventTrackDocument, 0x10);
        string? outOfRangeTrackDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x100, 0x3B64, [2]), eventTrackDocument, 0x10);
        var crossWorldEventDocument = new SceneDocument();
        crossWorldEventDocument.CinematicEmbeddedEventTracks[(0x20, 0x100)] =
        [
            new(0x100, "unique source-world owner", 0x20, "SceneEvents", 0x300,
                [new(0, 0, -1, 0x8000, 4, 1)])
        ];
        string? uniqueCrossWorldDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x100, 0x3B64, [0]), crossWorldEventDocument, 0x10);
        crossWorldEventDocument.CinematicEmbeddedEventTracks[(0x21, 0x100)] =
        [
            new(0x100, "ambiguous source-world owner", 0x21, "SceneEvents", 0x301,
                [new(0, 0, -1, 0x8000, 4, 1)])
        ];
        string? ambiguousCrossWorldDescription = CinematicEmbeddedEventTrackResolver.Describe(
            new(0x100, 0x3B64, [0]), crossWorldEventDocument, 0x10);
        Check(indexedTrackDescription != null
                && indexedTrackDescription.Contains("SceneEvents 00000201", StringComparison.Ordinal)
                && indexedTrackDescription.Contains("EVE track 1", StringComparison.Ordinal)
                && indexedTrackDescription.Contains("target nested event target (00000300)", StringComparison.Ordinal)
                && indexedTrackDescription.Contains("8 keys", StringComparison.Ordinal)
                && (indexedTrackDescription.Contains("2.50s", StringComparison.Ordinal)
                    || indexedTrackDescription.Contains("2,50s", StringComparison.Ordinal))
            && allTrackDescription != null && allTrackDescription.Contains("2 track(s)", StringComparison.Ordinal)
                && allTrackDescription.Contains("SceneEvents 00000201 (2 tracks)", StringComparison.Ordinal)
                && !allTrackDescription.Contains("Events 00000200", StringComparison.Ordinal)
            && missingTrackDescription != null && missingTrackDescription.Contains("not present", StringComparison.Ordinal)
                && !missingTrackDescription.Contains("source world", StringComparison.Ordinal)
            && outOfRangeTrackDescription != null && outOfRangeTrackDescription.Contains("outside", StringComparison.Ordinal)
            && uniqueCrossWorldDescription != null && uniqueCrossWorldDescription.Contains("source world 00000020", StringComparison.Ordinal)
            && ambiguousCrossWorldDescription != null && ambiguousCrossWorldDescription.Contains("multiple source worlds", StringComparison.Ordinal),
            $"TRA call inspection should resolve serialized EVE indices for brainless owners, all-track calls and world-scoped/missing/out-of-range cases: indexed='{indexedTrackDescription}', all='{allTrackDescription}', missing='{missingTrackDescription}', range='{outOfRangeTrackDescription}', cross='{uniqueCrossWorldDescription}', ambiguous='{ambiguousCrossWorldDescription}'");
        Check(CinematicEngineFunctionCatalog.Describe(new(0x3500A70B, 0x2046, [0x71003F5A, 0, 3]))
                .Contains("drive morph channel 3 on object 71003F5A", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x2047, [0, 2, 4, 5]))
                .Contains("module-channel entry 2.4 to 5", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x1FD9, [1]))
                .Contains("mark target inactive", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(new(1, 0x1FB1, []))
                .Contains("hides/deactivates it", StringComparison.Ordinal),
            "Morph-carrier, morph-module, inactive-state and object-destroy calls should use source-backed semantics");
        var ownerContextClip = new AnimationClip { Loaded = true, CinematicOwnerKey = 0xABCDEF01 };
        ownerContextClip.Tracks.Add(new AnimationTrack
        {
            Bone = -1,
            Target = 0,
            Keys = [new AnimationKey { OpaquePayloads = [CreateCallPayload(0x1FD8, (1, 0x01000021))] }]
        });
        var contextualCalls = CinematicLiteralCall.ReadTimeline(ownerContextClip);
        Check(contextualCalls.Length == 1 && contextualCalls[0].Call.Target == 0xABCDEF01,
            "Owner-linked event tracks with an empty track target should inherit their script-owner context");
        ownerContextClip.CinematicOwnerKey = 0;
        var actorActionCalls = CinematicLiteralCall.ReadTimeline(ownerContextClip, 0xF1000001);
        Check(actorActionCalls.Length == 1 && actorActionCalls[0].Call.Target == 0xF1000001,
            "Unowned gameplay action calls with no EVE track target should inherit their actor-root context");
        var typedMessageClip = new AnimationClip { Loaded = true, CinematicOwnerKey = 0xBF00012E };
        typedMessageClip.Tracks.Add(new AnimationTrack
        {
            Bone = -1,
            Target = 0,
            Keys =
            [
                new AnimationKey
                {
                    Time = 7,
                    OpaquePayloads = [CreateTypedMessagePayload(30, 40, 5000, 0x9E001C75, 0)]
                },
                new AnimationKey
                {
                    Time = 8,
                    OpaquePayloads = [CreateTypedMessagePayload(53, 2, 1, 0x71003F5A, 0x9E001C75)]
                }
            ]
        });
        var typedMessages = CinematicTimelineMessage.ReadTimeline(typedMessageClip);
        Check(typedMessages.Length == 2
            && typedMessages[0].Message.Type == 30 && typedMessages[0].Message.Int1 == 40
            && typedMessages[0].Message.Int2 == 5000 && typedMessages[0].Message.ExecutionObjectKey == 0xBF00012E
            && typedMessages[0].Message.Gao == 0x9E001C75
            && typedMessages[1].Message.Type == 53 && typedMessages[1].Message.Gao2 == 0x9E001C75,
            "Bounded serialized messages should expose type, GAO fields, line/priority integers and owner-track context without executing delivery");
        typedMessageClip.CinematicOwnerKey = 0;
        var actorActionMessages = CinematicTimelineMessage.ReadTimeline(typedMessageClip, 0xF1000001);
        Check(actorActionMessages.Length == 2
            && actorActionMessages.All(item => item.Message.ExecutionObjectKey == 0xF1000001),
            "An unowned gameplay action clip should use its actor-root context when its EVE tracks have no target override");
        string[] mpegCallbacks = CinematicScriptReader.CallbacksFor(0x4901BEE6);
        Check(mpegCallbacks.Contains("mpegplayer_init") && mpegCallbacks.Contains("mpegplayer_loop"),
            "Script models should expose their source-mapped callback catalog");
        string[] activatorCallbacks = CinematicScriptReader.CallbacksFor(0x5E000487);
        Check(CinematicScriptReader.CallbackCatalogModelCount >= 400
            && CinematicScriptReader.HasCallbackCatalog(0x5E000487)
            && activatorCallbacks.Contains("Activator_act_init")
            && CinematicScriptReader.CallbacksFor(0xDEADBEEF).Length == 0
            && !CinematicScriptReader.HasCallbackCatalog(0xDEADBEEF),
            "The expanded callback catalog should cover archive models beyond the hand-audited set and distinguish unknown model keys");
        byte[] callbackSlotsBytes = new byte[12 + 5 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(callbackSlotsBytes.AsSpan(12), 0x1001);
        BinaryPrimitives.WriteUInt32LittleEndian(callbackSlotsBytes.AsSpan(12 + 2 * sizeof(uint)), 0x1003);
        BinaryPrimitives.WriteUInt32LittleEndian(callbackSlotsBytes.AsSpan(12 + 3 * sizeof(uint)), 0x1002);
        var parsedCallbackSlots = PcScriptCallbackSlotReader.Read(callbackSlotsBytes,
            new Dictionary<uint, string> { [0x1001] = "first_callback", [0x1003] = "third_callback" });
        Check(parsedCallbackSlots is { Length: 5 }
            && parsedCallbackSlots[0] == new CinematicScriptCallbackSlot(0, 0x1001, "first_callback")
            && parsedCallbackSlots[1] == new CinematicScriptCallbackSlot(1, 0, null)
            && parsedCallbackSlots[2] == new CinematicScriptCallbackSlot(2, 0x1003, "third_callback")
            && parsedCallbackSlots[3] == new CinematicScriptCallbackSlot(3, 0x1002, null)
            && parsedCallbackSlots[4] == new CinematicScriptCallbackSlot(4, 0, null)
            && PcScriptCallbackSlotReader.Read(callbackSlotsBytes.AsSpan(0, 16),
                new Dictionary<uint, string>()) == null,
            "Model OVA callback parsing should preserve all five serialized positions, empty gaps and truncated-block failure");
        byte[] engineCallbackBytes = new byte[callbackSlotsBytes.Length + sizeof(int) + 3 * sizeof(uint)];
        callbackSlotsBytes.CopyTo(engineCallbackBytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(engineCallbackBytes.AsSpan(callbackSlotsBytes.Length), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(engineCallbackBytes.AsSpan(callbackSlotsBytes.Length + sizeof(int)), 0x1003);
        BinaryPrimitives.WriteUInt32LittleEndian(engineCallbackBytes.AsSpan(callbackSlotsBytes.Length + sizeof(int) * 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(engineCallbackBytes.AsSpan(callbackSlotsBytes.Length + sizeof(int) * 3), 0x1001);
        var parsedEngineCallbacks = PcScriptEngineCallbackReader.Read(engineCallbackBytes,
            new Dictionary<uint, string> { [0x1001] = "first_callback", [0x1003] = "third_callback" });
        byte[] truncatedEngineCallbackBytes = engineCallbackBytes.AsSpan(0, engineCallbackBytes.Length - sizeof(uint)).ToArray();
        Check(PcScriptEngineCallbackReader.Read(callbackSlotsBytes, new Dictionary<uint, string>()) is { Length: 0 }
            && parsedEngineCallbacks is { Length: 3 }
            && parsedEngineCallbacks[0] == new CinematicScriptEngineCallbackSlot(0, 0x1003, "third_callback")
            && parsedEngineCallbacks[1] == new CinematicScriptEngineCallbackSlot(1, 0, null)
            && parsedEngineCallbacks[2] == new CinematicScriptEngineCallbackSlot(2, 0x1001, "first_callback")
            && PcScriptEngineCallbackReader.Read(truncatedEngineCallbackBytes, new Dictionary<uint, string>()) == null
            && CinematicEngineCallbackCatalog.Describe(2) == "After record"
            && CinematicEngineCallbackCatalog.IsConfirmedByIrisCallSite(2)
            && !CinematicEngineCallbackCatalog.IsConfirmedByIrisCallSite(4),
            "Optional OVA engine-event callbacks should preserve event IDs and null entries, reject truncated tails and label source confidence");
        var mdiskDroneOwner = new CinematicScriptOwner(0x72005128, 0x7200512B, 0x720050F1, 0xD90057CC,
            uint.MaxValue, uint.MaxValue, "reusable Mdisk drone", ["Drone_Mdisk_Drone_Mdisk_init"],
            [new("des_o_drone_vert", 0x72005129, "instance", "PNJ_Drone_Mdisk_V.gao"),
             new("des_o_plug_wp", 0x72005135, "instance", "WP_NET_Drone_Mdisk_04.gao")],
            [new("des_o_drone_vert", PcVariableFormat.ObjectReferenceType, "instance", "0x72005129"),
             new("des_o_plug_wp", PcVariableFormat.ObjectReferenceType, "instance", "0x72005135"),
             new("SF_deplacement_done", PcVariableFormat.IntegerType, "instance", "2833")])
        {
            ModelFunctionNames =
            [
                "Drone_Mdisk_Drone_Mdisk_init",
                "Drone_Mdisk_Drone_Mdisk_mode_wait",
                "Drone_Mdisk_Drone_Mdisk_exec_GFX_Plasma",
                "Drone_Mdisk_Drone_Mdisk_exec_GFX_Halo",
                "Drone_Mdisk_Drone_Mdisk_mode_deplacement",
                "Drone_Mdisk_Drone_Mdisk_mode_plug",
                "Drone_Mdisk_Drone_Mdisk_exec_check_dialog"
            ]
        };
        var linkedObjectCalls = CinematicScriptReferencedObjectCallResolver.Resolve(mdiskDroneOwner);
        Check(linkedObjectCalls.Any(call => call.VariableName == "des_o_drone_vert"
                && call.ObjectKey == 0x72005129 && call.TargetName == "PNJ_Drone_Mdisk_V.gao"
                && call.Api == "OBJ_FlagInactiveSet" && call.FunctionName == "Drone_Mdisk_Drone_Mdisk_init"
                && call.AliasVariable != null && call.AssignmentLine is > 0)
            && linkedObjectCalls.Any(call => call.VariableName == "des_o_drone_vert"
                && call.TargetName == "PNJ_Drone_Mdisk_V.gao"
                && call.Api == "OBJ_FlagInvisibleSet"
                && call.FunctionName == "Drone_Mdisk_Drone_Mdisk_mode_plug"
                && call.Branches.Length > 0),
            $"Source object-call inspection should resolve linked OVA targets across callback paths and retain source conditions: {string.Join(" | ", linkedObjectCalls.Select(call => $"{call.VariableName}:{call.TargetName}:{call.Api}:{call.FunctionName}:{call.Branches.Length}"))}");
        string[] mdiskDroneFindings = CinematicScriptBehaviorAnalyzer.Analyze(mdiskDroneOwner);
        Check(mdiskDroneFindings.Any(finding => finding.Contains("aligns and parents", StringComparison.Ordinal)
                && finding.Contains("PNJ_Drone_Mdisk_V.gao", StringComparison.Ordinal))
            && mdiskDroneFindings.Any(finding => finding.Contains("saved flag is set", StringComparison.Ordinal)
                && finding.Contains("WP_NET_Drone_Mdisk_04.gao", StringComparison.Ordinal)
                && finding.Contains("mode_plug", StringComparison.Ordinal)
                && finding.Contains("mode_wait", StringComparison.Ordinal)
                && finding.Contains("Current save state is unavailable", StringComparison.Ordinal)),
            $"The reusable Drone_Mdisk init callback should expose its component and saved-flag/waypoint branches without claiming live activation: {string.Join(" | ", mdiskDroneFindings)}");
        var nullLinkedDrone = mdiskDroneOwner with
        {
            ObjectReferences = [],
            Variables = [new("des_o_drone_vert", PcVariableFormat.ObjectReferenceType, "model", "0x00000000"),
                new("des_o_plug_wp", PcVariableFormat.ObjectReferenceType, "model", "0x00000000"),
                new("SF_deplacement_done", PcVariableFormat.IntegerType, "instance", "2832")]
        };
        string[] nullLinkedDroneFindings = CinematicScriptBehaviorAnalyzer.Analyze(nullLinkedDrone);
        Check(nullLinkedDroneFindings.Any(finding => finding.Contains("explicitly null", StringComparison.Ordinal)
                && finding.Contains("vertical component", StringComparison.Ordinal))
            && nullLinkedDroneFindings.Any(finding => finding.Contains("explicitly null", StringComparison.Ordinal)
                && finding.Contains("destroys the owner", StringComparison.Ordinal)),
            "Drone_Mdisk analysis should distinguish explicitly null OVA links from unresolved references");
        var cinematicOwner = new CinematicScriptOwner(0x100, 0, 0x200, 0x10,
            uint.MaxValue, uint.MaxValue, "cinematic controller", [],
            [new("des_o_actor", 0x300, "instance", "actor.gao")], []);
        var referencedActor = new CinematicScriptOwner(0x300, 0, 0x400, 0x20,
            0x500, uint.MaxValue, "reusable actor", [], [], []);
        var unrelatedOwner = new CinematicScriptOwner(0x600, 0, 0x700, 0x30,
            uint.MaxValue, uint.MaxValue, "unrelated world object", [], [], []);
        var scopedOwners = CinematicScriptScope.ForClip([cinematicOwner, referencedActor, unrelatedOwner],
            0x10, [], []);
        Check(scopedOwners.Select(owner => owner.OwnerKey).Order().SequenceEqual([0x100u, 0x300u]),
            "A cinematic script scope should include referenced cross-world actor brains and exclude unrelated world scripts");
        var worldScriptScope = CinematicScriptScope.ForWorld(
            [cinematicOwner, referencedActor, unrelatedOwner], 0x10, []);
        Check(worldScriptScope.Select(owner => owner.OwnerKey).Order().SequenceEqual([0x100u, 0x300u]),
            "Static world script inspection should remain available without a selected playback clip and retain referenced cross-world owners");
        var trackedActorOwner = new CinematicScriptOwner(0x650, 0, 0x660, 0x70,
            uint.MaxValue, uint.MaxValue, "tracked actor brain", [], [], []);
        var trackedActorScope = CinematicScriptScope.ForClip(
            [cinematicOwner, referencedActor, unrelatedOwner, trackedActorOwner],
            0x10, [], [trackedActorOwner.OwnerKey]);
        Check(trackedActorScope.Any(owner => owner.OwnerKey == trackedActorOwner.OwnerKey)
                && !trackedActorScope.Any(owner => owner.OwnerKey == unrelatedOwner.OwnerKey),
            "An object referenced only by a cinematic transform track should bring its uniquely resolved cross-world brain into script scope");
        var worldObjectScope = CinematicScriptScope.ForWorld(
            [cinematicOwner, referencedActor, unrelatedOwner, trackedActorOwner], 0x10,
            [trackedActorOwner.OwnerKey]);
        Check(worldObjectScope.Any(owner => owner.OwnerKey == trackedActorOwner.OwnerKey)
                && !worldObjectScope.Any(owner => owner.OwnerKey == unrelatedOwner.OwnerKey),
            "World-only script inspection should resolve unique cross-world owners from the world's serialized object keys");
        var selectedFlagLinkedOwners = CinematicScriptScope.ForClip([cinematicOwner, referencedActor, unrelatedOwner],
            0x10, [], [], selectedOwnerId: unrelatedOwner.Id);
        Check(selectedFlagLinkedOwners.Any(owner => owner.OwnerKey == unrelatedOwner.OwnerKey),
            "An explicitly selected cross-world script owner should remain visible outside the initial cinematic scope");
        var messageTargetOwner = new CinematicScriptOwner(0x800, 0, 0x900, 0x40,
            uint.MaxValue, uint.MaxValue, "message target brain", [], [], []);
        var messageGaoOwner = new CinematicScriptOwner(0x900, 0, 0xA00, 0x50,
            uint.MaxValue, uint.MaxValue, "message GAO brain", [], [], []);
        var messageGao2Owner = new CinematicScriptOwner(0xA00, 0, 0xB00, 0x60,
            uint.MaxValue, uint.MaxValue, "message GAO2 brain", [], [], []);
        var messageScope = CinematicScriptScope.ForClip(
            [cinematicOwner, referencedActor, unrelatedOwner, messageTargetOwner, messageGaoOwner, messageGao2Owner],
            0x10, [], [],
            [new(1, 0x800, 0, 0, default, default, 0, 0, 53, 0, 53),
             new(2, 0, 0x900, 0, default, default, 0, 0, 53, 0, 53),
             new(3, 0, 0, 0xA00, default, default, 0, 0, 53, 0, 53)]);
        Check(messageScope.Select(owner => owner.OwnerKey).Order()
                .SequenceEqual([0x100u, 0x300u, 0x800u, 0x900u, 0xA00u]),
            "Typed-message execution object, GAO and GAO2 should each bring linked cross-world script owners into the inspector scope");
        var secondHopOwner = new CinematicScriptOwner(0xB00, 0, 0xC00, 0x70,
            uint.MaxValue, uint.MaxValue, "second-hop script owner", [], [], []);
        var eventLinkedOwner = messageGaoOwner with
        {
            ObjectReferences = [new("des_o_nested", secondHopOwner.OwnerKey, "instance", secondHopOwner.Name)]
        };
        var linkedOwnerEventTrack = new CinematicScriptEventTrack(referencedActor.WorldKey, referencedActor.OwnerKey,
            "Events", referencedActor.EventsKey, "linked actor event", 1,
            [(0.5f, new CinematicLiteralCall(eventLinkedOwner.OwnerKey, 12, []))],
            [(0.75f, new CinematicTimelineMessage(0.75f, 0, 0, messageGao2Owner.OwnerKey,
                default, default, 0, 0, 53, 0, 53))]);
        var transitiveScriptScope = CinematicScriptScope.ForClip(
            [cinematicOwner, referencedActor, unrelatedOwner, eventLinkedOwner, secondHopOwner, messageGao2Owner],
            0x10, [], [], ownerEventTracks: [linkedOwnerEventTrack]);
        Check(transitiveScriptScope.Select(owner => owner.OwnerKey).Order()
                .SequenceEqual([0x100u, 0x300u, 0x900u, 0xA00u, 0xB00u]),
            "The inspector scope should follow linked event commands, typed-message references and nested OVA links");
        var cinematicWorldEventTarget = new CinematicScriptOwner(0xD00, 0, 0xD10, 0x11,
            uint.MaxValue, uint.MaxValue, "cinematic-world event target", [], [], []);
        var cinematicWorldEventOwner = cinematicOwner with { SceneEventsKey = 0xD20 };
        var cinematicWorldEventTrack = new CinematicScriptEventTrack(cinematicWorldEventOwner.WorldKey,
            cinematicWorldEventOwner.OwnerKey, "SceneEvents", cinematicWorldEventOwner.SceneEventsKey,
            "scene event target", 1,
            [(1f, new CinematicLiteralCall(cinematicWorldEventTarget.OwnerKey, 12, []))], []);
        var cinematicEventScope = CinematicScriptScope.ForClip(
            [cinematicWorldEventOwner, cinematicWorldEventTarget, unrelatedOwner], 0x10, [], [],
            ownerEventTracks: [cinematicWorldEventTrack]);
        Check(cinematicEventScope.Any(owner => owner.OwnerKey == cinematicWorldEventTarget.OwnerKey)
                && !cinematicEventScope.Any(owner => owner.OwnerKey == unrelatedOwner.OwnerKey),
            "SceneEvents owned by the cinematic world should bring their resolved external command targets into scope");
        var duplicateCinematicEventTarget = cinematicWorldEventTarget with { WorldKey = 0x12 };
        var ambiguousCinematicEventScope = CinematicScriptScope.ForClip(
            [cinematicWorldEventOwner, cinematicWorldEventTarget, duplicateCinematicEventTarget], 0x10,
            [], [], ownerEventTracks: [cinematicWorldEventTrack]);
        Check(!ambiguousCinematicEventScope.Any(owner => owner.OwnerKey == cinematicWorldEventTarget.OwnerKey),
            "Cinematic SceneEvents should leave duplicate external target keys unresolved instead of selecting a world arbitrarily");
        var companionOwner = new CinematicScriptOwner(0x9E001C75, 0, 0x35001FF8, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "_PJ_Peyj_le_petit_cochon.gao",
            ["companion_co_action_cine", "companion_co_exec_dialogue", "companion_co_exec_reflex"], [], []);
        var companionCineMessage = new CinematicTimelineMessage(15, companionOwner.OwnerKey,
            0x71003F5A, 0, default, default, 0, 0, 0, 0, 46);
        string? companionMessageFinding = CinematicScriptMessageAnalyzer.Describe(companionCineMessage,
            [companionOwner], companionOwner.WorldKey);
        Check(companionMessageFinding != null
            && companionMessageFinding.Contains("GAO 71003F5A as the gaze target", StringComparison.Ordinal)
            && companionMessageFinding.Contains("default 1.0", StringComparison.Ordinal)
            && CinematicScriptMessageAnalyzer.Describe(companionCineMessage with { ExecutionObjectKey = unrelatedOwner.OwnerKey },
                [unrelatedOwner], unrelatedOwner.WorldKey) == null,
            "Message 46 should resolve to gaze/head-speed semantics only when its context owner has the companion cinematic callback");
        var companionStatusOwner = companionOwner with
        {
            CallbackNames = ["companion_co_init"],
            ModelFunctionNames = CinematicScriptReader.CallbacksFor(companionOwner.ScriptModelKey)
        };
        var companionStatusMessage = companionCineMessage with
        {
            ExecutionObjectKey = companionStatusOwner.OwnerKey,
            Gao = 0,
            Gao2 = 0,
            Vector1 = new(10, 20, 30),
            Vector2 = default,
            Float1 = 0,
            Float2 = 0,
            Int1 = 3,
            Int2 = 220,
            Type = 49
        };
        string? companionStatusFinding = CinematicScriptMessageAnalyzer.Describe(companionStatusMessage,
            [companionStatusOwner], companionStatusOwner.WorldKey);
        string? companionDeferredActionFinding = CinematicScriptMessageAnalyzer.Describe(
            companionStatusMessage with { Int1 = 4, Int2 = 200, Vector1 = default, Float2 = 55 },
            [companionStatusOwner], companionStatusOwner.WorldKey);
        Check(companionStatusFinding?.Contains("companion_co_exec_get_message", StringComparison.Ordinal) == true
                && companionStatusFinding.Contains("status 3", StringComparison.Ordinal)
                && companionStatusFinding.Contains("action Int2=220", StringComparison.Ordinal)
                && companionStatusFinding.Contains("collision-start matrix", StringComparison.Ordinal)
                && companionStatusFinding.Contains("sight direction", StringComparison.Ordinal)
                && companionDeferredActionFinding?.Contains("status 4 stores Int2=200 as a deferred companion action", StringComparison.Ordinal) == true
                && companionDeferredActionFinding.Contains("Float2=55 supplies the companion world ID", StringComparison.Ordinal)
                && CinematicScriptMessageAnalyzer.Describe(companionStatusMessage with
                    { ExecutionObjectKey = unrelatedOwner.OwnerKey }, [unrelatedOwner], unrelatedOwner.WorldKey) == null,
            "Companion type-49 messages should expose source-confirmed status/action/placement fields only when the assigned callback graph reaches the receiver");
        var cameraTrackOwner = new CinematicScriptOwner(0x71003F56, 0, 0x71003F55, 0x7100108B,
            uint.MaxValue, uint.MaxValue, "camera track owner",
            ["camera_cam_exec_cine", "camera_cam_1_mainloop"], [], []);
        var mdiskMessage = companionCineMessage with
        {
            ExecutionObjectKey = cameraTrackOwner.OwnerKey,
            Gao = 0xD9005D6E,
            Gao2 = 0xD9005D71,
            Int1 = 0,
            Int2 = -569,
            Type = 55
        };
        Check(CinematicScriptMessageAnalyzer.Describe(mdiskMessage, [cameraTrackOwner], cameraTrackOwner.WorldKey) == null,
            "A type-55 message on the camera EVE execution object must remain raw when its callback family has no recovered type-55 consumer");
        var cameraModeMessage = mdiskMessage with
        {
            Gao = 0x71003F5A,
            Gao2 = 0x1F00000E,
            Vector1 = new(1, 2, 3),
            Vector2 = new(4, 5, 6),
            Float1 = 1.2f,
            Float2 = 0.5f,
            Int1 = 1,
            Type = 60
        };
        string? cameraModeFinding = CinematicScriptMessageAnalyzer.Describe(
            cameraModeMessage, [cameraTrackOwner], cameraTrackOwner.WorldKey);
        Check(cameraModeFinding?.Contains("cinematic-camera mode request", StringComparison.Ordinal) == true
            && cameraModeFinding.Contains("camera destination GAO 71003F5A", StringComparison.Ordinal)
            && cameraModeFinding.Contains("look target GAO 1F00000E", StringComparison.Ordinal)
            && cameraModeFinding.Contains("supplies the transition speed", StringComparison.Ordinal)
            && CinematicScriptMessageAnalyzer.Describe(cameraModeMessage with { Type = 55 },
                [cameraTrackOwner], cameraTrackOwner.WorldKey) == null,
            "Camera type-60 semantics should require its source callback family and leave other message IDs unmapped");
        var companionFacialMessage = companionCineMessage with { Type = 53, Int1 = 9, Vector2 = new(0, 0.75f, 0) };
        string? companionFacialFinding = CinematicScriptMessageAnalyzer.Describe(companionFacialMessage,
            [companionOwner], companionOwner.WorldKey);
        var automaticLipSyncOwner = new CinematicScriptOwner(0x800, 0, 0x900, 0x10,
            uint.MaxValue, uint.MaxValue, "automatic lip-sync owner",
            ["Cine_lipsynch_auto_Lips_lecture_messages"], [], []);
        string? automaticLipSyncFinding = CinematicScriptMessageAnalyzer.Describe(
            companionFacialMessage with { ExecutionObjectKey = automaticLipSyncOwner.OwnerKey },
            [automaticLipSyncOwner], automaticLipSyncOwner.WorldKey);
        string? automaticMouthCycleFinding = CinematicScriptMessageAnalyzer.Describe(
            companionFacialMessage with
            {
                ExecutionObjectKey = automaticLipSyncOwner.OwnerKey,
                Vector2 = new(0, -580, 0)
            }, [automaticLipSyncOwner], automaticLipSyncOwner.WorldKey);
        var sallyExpressionOwner = new CinematicScriptOwner(0xA00, 0, 0xB00, 0x10,
            uint.MaxValue, uint.MaxValue, "expression owner", ["Sally_sa_exec_message_read"], [], [])
        {
            ModelFunctionNames = ["Sally_sa_exec_message_read"]
        };
        var sallyMessageReaders = CinematicScriptReader.ReachableMessageReaders(sallyExpressionOwner, 16);
        var genericSallyMessage = companionCineMessage with
        {
            ExecutionObjectKey = sallyExpressionOwner.OwnerKey,
            Type = 16
        };
        string? genericSallyMessageFinding = CinematicScriptMessageAnalyzer.Describe(genericSallyMessage,
            [sallyExpressionOwner], sallyExpressionOwner.WorldKey);
        Check(sallyMessageReaders.Any(reader => reader.MessageId == 16
                    && reader.FunctionName == "Sally_sa_exec_message_read"
                    && reader.Source == "ai/ai_bge/ai_compiledscripts_sally.c")
                && genericSallyMessageFinding?.Contains("reads message ID 16 from this object's message queue",
                    StringComparison.Ordinal) == true
                && genericSallyMessageFinding.Contains("field interpretation is not classified", StringComparison.Ordinal)
                && CinematicScriptMessageAnalyzer.Describe(genericSallyMessage with { Type = 99 },
                    [sallyExpressionOwner], sallyExpressionOwner.WorldKey) == null,
            "Unknown message IDs should link only to literal MSGGetByID reads reachable from the resolved owner's callbacks");
        var cinematicTextReceiver = new CinematicScriptOwner(0x7200064E, 0, 0x350091B0, 0x71003F43,
            uint.MaxValue, uint.MaxValue, "TXT_Sally.gao", ["Cine_Textes_CiTxt_init"], [], [])
        {
            ModelFunctionNames = ["Cine_Textes_CiTxt_init", "Cine_Textes_CiTxt_reception_messages"]
        };
        var cinematicTextReaders = CinematicScriptReader.ReachableMessageReaders(cinematicTextReceiver);
        var actionReactionSender = new CinematicScriptOwner(0xD0000001, 0, 0xD0000002, 0x71003F43,
            uint.MaxValue, uint.MaxValue, "Action reaction sender", ["action_reaction_ar_exec_send_info"], [], [])
        {
            ModelFunctionNames = ["action_reaction_ar_exec_send_info"]
        };
        var messageRoutes = CinematicScriptMessageFlowResolver.Resolve(actionReactionSender,
            [actionReactionSender, cinematicTextReceiver]);
        string? cinematicTextMessageFinding = CinematicScriptMessageAnalyzer.Describe(
            genericSallyMessage with
            {
                ExecutionObjectKey = cinematicTextReceiver.OwnerKey,
                Type = 30
            }, [cinematicTextReceiver], cinematicTextReceiver.WorldKey);
        Check(cinematicTextReaders.Any(reader => reader.MessageId == 30
                    && reader.ReadApi == "MSGGet"
                    && reader.Source == "ai/ai_bge/ai_compiledscripts_global_managers.c"
                    && reader.Line == 3049),
            "Generic MSGGet reader index should connect Cine_Textes_CiTxt_init dispatch to its ID 30 branch");
        Check(cinematicTextMessageFinding?.Contains("reads message ID 30", StringComparison.Ordinal) == true,
            "Typed message fallback should report literal IDs consumed through MSGGet_C_CURRENT");
        Check(messageRoutes.Any(route => route.MessageId == 30
                    && route.RecipientKey == cinematicTextReceiver.OwnerKey
                    && route.Receiver.Id == cinematicTextReceiver.Id
                    && route.Readers.Any(reader => reader.FunctionName == "Cine_Textes_CiTxt_reception_messages"))
                && CinematicScriptMessageFlowResolver.Resolve(actionReactionSender,
                    [actionReactionSender, cinematicTextReceiver with
                    {
                        ModelFunctionNames = ["Cine_Textes_CiTxt_init"]
                    }]).Length == 0,
            "Static MSGSend_C to TXT_Sally should link type 30 only when the resolved receiver's reachable current-object queue reader tests that literal ID");
        string? sallyExpressionFinding = CinematicScriptMessageAnalyzer.Describe(
            companionFacialMessage with { ExecutionObjectKey = sallyExpressionOwner.OwnerKey, Int2 = -1 },
            [sallyExpressionOwner], sallyExpressionOwner.WorldKey);
        var sallyCinematicModeMessage = companionCineMessage with
        {
            ExecutionObjectKey = sallyExpressionOwner.OwnerKey,
            Gao = 0,
            Gao2 = 0,
            Int1 = 212,
            Int2 = 1,
            Vector1 = new(1, 2, 3),
            Vector2 = new(4, 5, 6),
            Type = 47
        };
        string? sallyCinematicModeEntry = CinematicScriptMessageAnalyzer.Describe(sallyCinematicModeMessage,
            [sallyExpressionOwner], sallyExpressionOwner.WorldKey);
        string? sallyCinematicModeExit = CinematicScriptMessageAnalyzer.Describe(
            sallyCinematicModeMessage with { Int1 = 212, Int2 = 0, Vector1 = default, Vector2 = default },
            [sallyExpressionOwner], sallyExpressionOwner.WorldKey);
        Check(sallyCinematicModeEntry?.Contains("nonzero Int2 as cinematic-mode entry", StringComparison.Ordinal) == true
                && sallyCinematicModeEntry.Contains("Int1=212 is passed to ACTActionSet", StringComparison.Ordinal)
                && sallyCinematicModeEntry.Contains("type-53 expression-control message", StringComparison.Ordinal)
                && sallyCinematicModeExit?.Contains("Int2=0 as a cinematic-mode exit", StringComparison.Ordinal) == true
                && sallyCinematicModeExit.Contains("Sally's 2D handler", StringComparison.Ordinal)
                && CinematicScriptMessageAnalyzer.Describe(sallyCinematicModeMessage with
                    { ExecutionObjectKey = unrelatedOwner.OwnerKey }, [unrelatedOwner], unrelatedOwner.WorldKey) == null,
            "Sally type-47 cinematic-mode transitions should be described only for the matching callback context and distinguish entry/action from exit/reset behavior");
        var nestedSallyOwner = sallyExpressionOwner with
        {
            CallbackNames = ["Sally_sa_init"],
            ModelFunctionNames = ["Sally_sa_init", "Sally_sa_reflex", "Sally_sa_exec_message_read"]
        };
        string? nestedSallyFinding = CinematicScriptMessageAnalyzer.Describe(
            companionFacialMessage with { ExecutionObjectKey = nestedSallyOwner.OwnerKey, Int2 = -1 },
            [nestedSallyOwner], nestedSallyOwner.WorldKey);
        var nestedSallyPaths = CinematicScriptReader.NestedCallbackPaths(nestedSallyOwner);
        var mpegPlayerTrackOwner = new CinematicScriptOwner(0xBF000DA6, 0, 0x4901BEE6, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "MPEG screen fixture",
            ["mpegplayer_init"], [], [])
        {
            ModelFunctionNames = ["mpegplayer_init", "mpegplayer_loop"]
        };
        var mpegPlayerPaths = CinematicScriptReader.NestedCallbackPaths(mpegPlayerTrackOwner);
        var callbackRegistrationOwner = new CinematicScriptOwner(0xB00, 0, 0xC00, 0x10,
            uint.MaxValue, uint.MaxValue, "callback registration fixture",
            ["Garde_Barriere_Entrepot_GBE_init"], [], [])
        {
            ModelFunctionNames = ["Garde_Barriere_Entrepot_GBE_init",
                "Garde_Barriere_Entrepot_GBE_mode_attente"]
        };
        var callbackRegistrationPaths = CinematicScriptReader.NestedCallbackPaths(callbackRegistrationOwner);
        var callbackDispatchSites = CinematicScriptReader.DispatchSitesFor(callbackRegistrationPaths);
        var worldFrameDispatchSites = CinematicScriptReader.EngineDispatchSitesFor(2);
        var animationDispatchSites = CinematicScriptReader.EngineDispatchSitesFor(3);
        Check(worldFrameDispatchSites.Any(site => site.Api == "AI_ExecCallbackAll"
                && site.Source == "engine.c" && site.Call == "AI_ExecCallbackAll(world, 2)")
            && animationDispatchSites.Any(site => site.Api == "AI_ExecCallback"
                && site.Source == "animation.c" && site.CallbackId == 3)
            && CinematicScriptReader.EngineDispatchSitesFor(4).Length == 0,
            "Assigned engine-event callbacks should link to indexed Iris dispatch calls while unsupported event IDs remain unclaimed");
        var callbackLifecycleOwner = new CinematicScriptOwner(0xB01, 0, 0xC01, 0x10,
            uint.MaxValue, uint.MaxValue, "callback lifecycle fixture",
            ["Crochax_3_Cro3_mode_cine"], [], [])
        {
            ModelFunctionNames = ["Crochax_3_Cro3_mode_cine", "Crochax_3_Cro3_exec_do_snake",
                "Crochax_3_Cro3_mode_snap"]
        };
        var callbackLifecyclePaths = CinematicScriptReader.NestedCallbackPaths(callbackLifecycleOwner);
        var incompleteSallyOwner = nestedSallyOwner with
        {
            ModelFunctionNames = ["Sally_sa_init", "Sally_sa_reflex"]
        };
        Check(companionFacialFinding?.Contains("Vector1.X/Y set morph channels 0/1", StringComparison.Ordinal) == true
            && automaticLipSyncFinding?.Contains("EVE track execution object resolves to 'automatic lip-sync owner'", StringComparison.Ordinal) == true
            && automaticLipSyncFinding?.Contains("Int1 supplies the mouth-channel speed (9)", StringComparison.Ordinal) == true
            && automaticLipSyncFinding?.Contains("nonnegative fractional remainder, selects a fixed form and factor, and disables automatic cycling", StringComparison.Ordinal) == true
            && automaticMouthCycleFinding?.Contains("negative fractional remainder and requests the controller's automatic 32-form mouth cycle", StringComparison.Ordinal) == true
            && automaticLipSyncFinding?.Contains("not a phoneme transcript", StringComparison.Ordinal) == true
            && automaticLipSyncFinding?.Contains("routes the lip-sync target to GAO 71003F5A", StringComparison.Ordinal) == true
            && sallyExpressionFinding?.Contains("clears the cinematic-expression mode", StringComparison.Ordinal) == true
            && sallyExpressionFinding?.Contains("reset branch discards the vector fields", StringComparison.Ordinal) == true
            && nestedSallyFinding?.Contains("Sally_sa_init -[track-change:1", StringComparison.Ordinal) == true
            && nestedSallyFinding?.Contains("-> Sally_sa_reflex -[execute", StringComparison.Ordinal) == true
            && nestedSallyFinding?.Contains("Sally_sa_exec_message_read", StringComparison.Ordinal) == true
            && nestedSallyPaths.Any(path => path.FunctionName == "Sally_sa_exec_message_read")
            && mpegPlayerPaths.Any(path => path.FunctionName == "mpegplayer_loop"
                && path.Path.StartsWith("mpegplayer_init -[set-current-track", StringComparison.Ordinal)
                && path.Path.Contains("-> mpegplayer_loop", StringComparison.Ordinal))
            && callbackRegistrationPaths.Any(path => path.FunctionName == "Garde_Barriere_Entrepot_GBE_mode_attente"
                && path.Path.StartsWith("Garde_Barriere_Entrepot_GBE_init -[register-callback:2",
                    StringComparison.Ordinal)
                && path.Path.Contains("-> Garde_Barriere_Entrepot_GBE_mode_attente", StringComparison.Ordinal))
            && callbackDispatchSites.Any(site => site.CallbackId == 2 && site.Api == "AI_ExecCallbackAll"
                && site.Source == "engine.c" && site.Call == "AI_ExecCallbackAll(world, 2)")
            && !callbackDispatchSites.Any(site => site.CallbackId == 3)
            && callbackLifecyclePaths.Any(path => path.FunctionName == "Crochax_3_Cro3_exec_do_snake"
                && path.Path.StartsWith("Crochax_3_Cro3_mode_cine -[register-callback:2",
                    StringComparison.Ordinal)
                && path.Path.Contains("-> Crochax_3_Cro3_exec_do_snake", StringComparison.Ordinal))
            && !callbackLifecyclePaths.Any(path => path.FunctionName == "Crochax_3_Cro3_mode_snap")
            && !CinematicScriptReader.TryDescribeCallbackReachability(incompleteSallyOwner,
                "Sally_sa_exec_message_read", out _)
            && CinematicScriptMessageAnalyzer.Describe(companionFacialMessage with { ExecutionObjectKey = unrelatedOwner.OwnerKey },
                [unrelatedOwner], unrelatedOwner.WorldKey) == null,
            "Message 53 semantics should follow source-mapped nested callbacks from assigned slots and distinguish morph control, lip-sync progression and expression-mode reset");
        string[] schedulerFindings = CinematicScriptSchedulerAnalyzer.Analyze(callbackRegistrationOwner with
        {
            SerializedGaoFlags = 0x20
        });
        string[] unscheduledFindings = CinematicScriptSchedulerAnalyzer.Analyze(callbackRegistrationOwner with
        {
            SerializedGaoFlags = 0
        });
        string[] pausedFindings = CinematicScriptSchedulerAnalyzer.Analyze(callbackRegistrationOwner with
        {
            SerializedGaoFlags = 0x20,
            SerializedStateFlags = 0x40000000
        });
        string[] specialTimeFindings = CinematicScriptSchedulerAnalyzer.Analyze(callbackRegistrationOwner with
        {
            SerializedGaoFlags = 0x20,
            SerializedStateFlags = 0x04000000
        });
        string[] rawActivationBitFindings = CinematicScriptSchedulerAnalyzer.Analyze(callbackRegistrationOwner with
        {
            SerializedStateFlags = 0x00400000
        });
        bool reportsAiCallbackTable = schedulerFindings.Any(finding => finding.Contains("AICallbacksTablePF", StringComparison.Ordinal));
        bool reportsWorldActivation = schedulerFindings.Any(finding => finding.Contains("WOR_ActivateObjectIfFlagsAllow", StringComparison.Ordinal));
        bool retainsRuntimeUncertainty = schedulerFindings.Any(finding => finding.Contains("cannot prove that the owner's callbacks ran", StringComparison.Ordinal));
        bool reportsMissingAiTableFlag = unscheduledFindings.Any(finding => finding.Contains("flag 0x20 is clear", StringComparison.Ordinal));
        bool reportsOrdinaryActivationBlock = pausedFindings.Any(finding => finding.Contains("runtime flags2 0x00400000", StringComparison.Ordinal)
            && finding.Contains("ENG_gf_SpecialTime is zero", StringComparison.Ordinal)
            && finding.Contains("source-confirmed ordinary-activation blocker", StringComparison.Ordinal));
        bool reportsSpecialTimeActivation = specialTimeFindings.Any(finding => finding.Contains("alternate activation branch requires runtime flags2 0x04000000", StringComparison.Ordinal)
            && finding.Contains("mask discards raw 0x04000000", StringComparison.Ordinal))
            && !specialTimeFindings.Any(finding => finding.Contains("survives the initial", StringComparison.Ordinal));
        bool reportsDispatchFilters = schedulerFindings.Any(finding => finding.Contains("extraFlag 0x4", StringComparison.Ordinal)
            && finding.Contains("AIInstance", StringComparison.Ordinal)
            && finding.Contains("AI_RunInstance", StringComparison.Ordinal));
        Check(schedulerFindings.Any(finding => finding.Contains("remaining delay", StringComparison.Ordinal))
            && schedulerFindings.Any(finding => finding.Contains("stops the remaining AI_MainCall table traversal", StringComparison.Ordinal)),
            "AI scheduling must expose callback timing and world traversal abort dependencies.");
        bool rejectsRawActivationBit = rawActivationBitFindings.Any(finding => finding.Contains("0x00400000 bit is discarded", StringComparison.Ordinal))
            && !rawActivationBitFindings.Any(finding => finding.Contains("source-mapped contribution", StringComparison.Ordinal));
        Check(reportsAiCallbackTable && reportsWorldActivation && retainsRuntimeUncertainty
                && reportsMissingAiTableFlag && reportsOrdinaryActivationBlock && reportsSpecialTimeActivation
                && reportsDispatchFilters && rejectsRawActivationBit,
            "World AI scheduling checks failed: "
            + $"AI table {reportsAiCallbackTable}, world activation {reportsWorldActivation}, runtime caveat {retainsRuntimeUncertainty}, "
            + $"missing flag {reportsMissingAiTableFlag}, ordinary block {reportsOrdinaryActivationBlock}, "
            + $"special-time {reportsSpecialTimeActivation}, dispatch filters {reportsDispatchFilters}, raw-bit rejection {rejectsRawActivationBit}.");
        var eventDispatchOwner = callbackRegistrationOwner with
        {
            EventsKey = 0xD9005D68,
            SceneEventsKey = 0xD9005D69,
            SerializedGaoFlags = 0x02000000
        };
        string[] eventDispatchFindings = CinematicEventSchedulerAnalyzer.Analyze(eventDispatchOwner);
        string[] missingEventTableFlagFindings = CinematicEventSchedulerAnalyzer.Analyze(eventDispatchOwner with
        {
            SerializedGaoFlags = 0
        });
        bool reportsEventTableMembership = eventDispatchFindings.Any(finding =>
            finding.Contains("eventGameObjectsTablePF", StringComparison.Ordinal)
            && finding.Contains("0x02000000", StringComparison.Ordinal));
        bool reportsIndependentRuntimeGates = eventDispatchFindings.Any(finding =>
            finding.Contains("EVE_MainCall", StringComparison.Ordinal)
            && finding.Contains("0x00400000", StringComparison.Ordinal)
            && finding.Contains("extraFlag 0x4", StringComparison.Ordinal)
            && finding.Contains("not proven", StringComparison.Ordinal));
        bool reportsClearEventTableFlag = missingEventTableFlagFindings.Any(finding =>
            finding.Contains("flag 0x02000000 is clear", StringComparison.Ordinal));
        bool reportsConditionalInitialActivation = eventDispatchFindings.Any(finding =>
            finding.Contains("Fresh-load activation projection", StringComparison.Ordinal)
            && finding.Contains("EOT categories if activated: EVE tracks", StringComparison.Ordinal)
            && finding.Contains("not observed runtime state", StringComparison.Ordinal));
        string[] identitySkippedEventFindings = CinematicEventSchedulerAnalyzer.Analyze(eventDispatchOwner with
        {
            SerializedGaoFlags = 0x02000001
        });
        bool reportsWorldRebuildIdentitySkip = identitySkippedEventFindings.Any(finding =>
            finding.Contains("identity flag bit 0x1", StringComparison.Ordinal)
            && finding.Contains("WOR_World_ActivateObjects", StringComparison.Ordinal));
        bool ignoresOwnersWithoutSceneEvents = CinematicEventSchedulerAnalyzer.Analyze(
            eventDispatchOwner with { EventsKey = 0xD9005D68, SceneEventsKey = uint.MaxValue }).Length == 0;
        Check(reportsEventTableMembership && reportsIndependentRuntimeGates
                && reportsClearEventTableFlag && reportsConditionalInitialActivation
                && reportsWorldRebuildIdentitySkip && ignoresOwnersWithoutSceneEvents,
            "EVE dispatch analysis should distinguish serialized identity, conditional initial activation, EOT membership and live execution");
        var configuredLipSyncOwner = new CinematicScriptOwner(0x801, 0, 0x902, 0x10,
            uint.MaxValue, 0x903, "lip-sync owner",
            ["Cine_lipsynch_auto_Lips_init", "Cine_lipsynch_auto_Lips_lecture_messages", "Cine_lipsynch_auto_Lips_morphing"],
            [new("des_o_acteur", 0x9E001C75, "instance", "_PJ_Peyj_le_petit_cochon.gao")],
            [new("des_i_type_acteur", PcVariableFormat.IntegerType, "instance", "4")]);
        string[] lipSyncFindings = CinematicScriptBehaviorAnalyzer.Analyze(configuredLipSyncOwner);
        Check(lipSyncFindings.Any(finding => finding.Contains("targets '_PJ_Peyj_le_petit_cochon.gao'", StringComparison.Ordinal)
                && finding.Contains("mouth-channel slot 1", StringComparison.Ordinal)
                && finding.Contains("head-shape channel 3", StringComparison.Ordinal))
            && lipSyncFindings.Any(finding => finding.Contains("consumes queued type 53", StringComparison.Ordinal)
                && finding.Contains("does not inspect dialogue text or audio playback state", StringComparison.Ordinal))
            && lipSyncFindings.Any(finding => finding.Contains("source-coded 32-entry form sequence", StringComparison.Ordinal)
                && finding.Contains("not phoneme decoding", StringComparison.Ordinal))
            && lipSyncFindings.Any(finding => finding.Contains("do not prove that the owner was instantiated", StringComparison.Ordinal)),
            "Automatic lip-sync owners should expose source-derived actor, channel, type-53, morph-cycle and runtime-evidence limits");
        var selfLipSyncOwner = configuredLipSyncOwner with
        {
            Variables = [new("des_o_acteur", PcVariableFormat.ObjectReferenceType, "model", "0x00000000")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(selfLipSyncOwner)
                .Any(finding => finding.Contains("uses the script owner itself because des_o_acteur is null", StringComparison.Ordinal)),
            "A serialized null lip-sync actor reference should resolve to the script owner rather than be reported unavailable");
        string[] companionFindings = CinematicScriptBehaviorAnalyzer.Analyze(companionOwner);
        Check(companionFindings.Any(finding => finding.Contains("co_current_mode=11", StringComparison.Ordinal)
                && finding.Contains("i_interdit_action=15", StringComparison.Ordinal)
                && finding.Contains("does not establish that this action ran", StringComparison.Ordinal))
            && companionFindings.Any(finding => finding.Contains("without processing its dialogue queue", StringComparison.Ordinal)
                && finding.Contains("separate from cinematic type-30", StringComparison.Ordinal)),
            "Companion actor scripts should expose the source-backed cinematic mode/dialogue handoff without claiming runtime activation");
        var mpegPlayerOwner = new CinematicScriptOwner(0xBF000DA6, 0, 0x4901BEE6, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "Cine_ecran_explik_invent.gao", mpegCallbacks,
            [new("des_o_Interrupteur", 0xC100AEF9, "instance", "OBJ_Interrupteur_ordinateur.gao")],
            [new("des_i_SubMat", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_SubMatLevel", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_SubMatLevel", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_i_sf_on_off", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_StopAtEnd", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_StopAtEnd", PcVariableFormat.IntegerType, "instance", "1"),
             new("des_i_sf_modifier_vrai", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_sf_jesuisla", PcVariableFormat.IntegerType, "model", "0"),
             new("des_f_duree_here", PcVariableFormat.FloatType, "model", "4")]);
        string[] mpegPlayerFindings = CinematicScriptBehaviorAnalyzer.Analyze(mpegPlayerOwner);
        Check(mpegPlayerFindings.Any(finding => finding.Contains("OBJ_Interrupteur_ordinateur.gao", StringComparison.Ordinal)
                && finding.Contains("capability 0x100", StringComparison.Ordinal))
            && mpegPlayerFindings.Any(finding => finding.Contains("parameter 10", StringComparison.Ordinal)
                && finding.Contains("sub-index 0, level 1", StringComparison.Ordinal))
            && mpegPlayerFindings.Any(finding => finding.Contains("des_i_StopAtEnd is enabled", StringComparison.Ordinal))
            && mpegPlayerFindings.Any(finding => finding.Contains("capability 0x2000", StringComparison.Ordinal)
                && finding.Contains("capability 0x2", StringComparison.Ordinal)
                && finding.Contains("Bink frames", StringComparison.Ordinal)),
            "MPEG-player analysis should derive the switch, material slot, stop-at-end branch and runtime-state limits from OVA and source callbacks");
        var dialogueOwner = new CinematicScriptOwner(0xBF00012E, 0, 0x350091B0, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "Cine_0002_Dialogues.gao",
            ["Cine_Textes_CiTxt_init", "Cine_Textes_CiTxt_reception_messages"],
            [new("des_ai_perso_localisation_par", 0x9E001C75, "instance", "_PJ_Peyj_le_petit_cochon.gao")],
            [new("des_i_priorite_par_defaut", PcVariableFormat.IntegerType, "model", "0"),
             new("des_at_lignes_texte", 44, "instance", "array (100 entries)"),
             new("des_ai_son_associe", PcVariableFormat.IntegerType, "model", "array (100 entries)"),
             new("des_af_volume_associe", PcVariableFormat.FloatType, "model", "array (100 entries)")],
            [new(0, 0xC1009667, 32, 301, 2, 1, .7f, 4, 30)]);
        string[] dialogueFindings = CinematicScriptBehaviorAnalyzer.Analyze(dialogueOwner);
        Check(dialogueFindings.Any(finding => finding.Contains("default priority 5000", StringComparison.Ordinal))
                && dialogueFindings.Any(finding => finding.Contains("message ID 30", StringComparison.Ordinal)
                    && finding.Contains("Int1=200", StringComparison.Ordinal)
                    && finding.Contains("Int1 mod 100", StringComparison.Ordinal))
                && dialogueFindings.Any(finding => finding.Contains("Decoded 1 populated dialogue slots", StringComparison.Ordinal)
                    && finding.Contains("associated-audio metadata", StringComparison.Ordinal))
                && dialogueFindings.Any(finding => finding.Contains("message GAO2", StringComparison.Ordinal)
                    && finding.Contains("_PJ_Peyj_le_petit_cochon.gao", StringComparison.Ordinal)
                    && finding.Contains("does not itself drive facial morphs", StringComparison.Ordinal)),
            "Dialogue callbacks should report verified message/line selection and OVA table limits without claiming live text or lipsync");
        var cineOwner = new CinematicScriptOwner(0xBF00218D, 0, 0x350091C3, 0xBF000127,
            uint.MaxValue, 0xBF00218E, "CINE_APRES_MDISK_EVT02.gao",
            ["Cine_Events_cine_tests_depart", "Cine_Events_Cine_exec_GoToEvents",
             "Cine_Events_cine_play_eventtrack", "Cine_Events_Cine_exec_EndOfEvents"], [],
            [new("i_tests_a_reussir", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_ripley", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_cine_independante", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2164"),
             new("des_i_SPFlag_associe_vrai", PcVariableFormat.IntegerType, "instance", "2165"),
             new("des_i_cine_type", PcVariableFormat.IntegerType, "instance", "6"),
             new("des_o_actor_zone_exit1", PcVariableFormat.ObjectReferenceType, "instance", "0x00000010"),
             new("des_o_actor_detec_exit1", PcVariableFormat.ObjectReferenceType, "instance", "0x00000011"),
             new("des_i_SPFlag_test_exit1", PcVariableFormat.IntegerType, "instance", "2166"),
             new("des_f_duree_totale", PcVariableFormat.FloatType, "instance", "20"),
             new("i_enLecture", PcVariableFormat.IntegerType, "instance", "1")]);
        string[] triggerFindings = CinematicScriptBehaviorAnalyzer.Analyze(cineOwner);
        Check(triggerFindings.Any(finding => finding.Contains("Start test 1 is active", StringComparison.Ordinal)
                && finding.Contains("2164 must be set", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("derived by cine_init (mask 0x80)", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("Iris runtime scheduling", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("Initialization suppression", StringComparison.Ordinal)
                && finding.Contains("flag 2165 set", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("sets saved special flag 2165", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("shared cinematic priority", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("cine_type=0x6", StringComparison.Ordinal)
                && finding.Contains("camera transition message 60", StringComparison.Ordinal)
                && finding.Contains("board panel", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("Interruption tests derived by cine_init (mask 0x81)", StringComparison.Ordinal)
                && finding.Contains("active-owner bit 0x4000", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("TRAPlayAll", StringComparison.Ordinal)
                && finding.Contains("every embedded track together", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("i_enLecture (1)", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("TRAStopAll", StringComparison.Ordinal)
                && finding.Contains("TRAIsStopAll", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("TRAReinitAll", StringComparison.Ordinal)
                && finding.Contains("resets track cursors", StringComparison.Ordinal))
            && triggerFindings.Any(finding => finding.Contains("des_f_duree_totale=20", StringComparison.Ordinal)),
            "Shared cinematic callbacks should derive EVE run/stop/reset flow, gates, timeout and completion effects from source and configured OVA fields");
        var modelReferencedCineOwner = cineOwner with
        {
            CallbackNames = ["Cine_Events_cine_firstinit"],
            ModelFunctionNames = [
                "Cine_Events_cine_firstinit", "Cine_Events_cine_init",
                "Cine_Events_cine_tests_depart", "Cine_Events_cine_play_eventtrack",
                "Cine_Events_cine_tests_interruption", "Cine_Events_cine_exec_test_priorite",
                "Cine_Events_Cine_exec_GoToEvents", "Cine_Events_Cine_exec_EndOfEvents",
                "Cine_Events_Cine_exec_InitialiseCine"
            ]
        };
        string[] modelReferencedCineFindings = CinematicScriptBehaviorAnalyzer.Analyze(modelReferencedCineOwner);
        Check(modelReferencedCineFindings.Any(finding => finding.Contains("derived by cine_init (mask 0x80)", StringComparison.Ordinal))
                && modelReferencedCineFindings.Any(finding => finding.Contains("TRAPlayAll", StringComparison.Ordinal))
                && modelReferencedCineFindings.Any(finding => finding.Contains("effective owner callback slots are separate data", StringComparison.Ordinal)),
            "Cine_Events model references should expose source-confirmed lifecycle capability without presenting model functions as assigned owner slots");
        var cinematicAudioOwner = modelReferencedCineOwner with
        {
            ModelFunctionNames = [.. modelReferencedCineOwner.ModelFunctionNames,
                "Cine_Events_cine_test_fin_sons", "Cine_Events_Cine_exec_soundtracks_Fade"],
            Variables = [.. modelReferencedCineOwner.Variables,
                new("des_i_SndMdf_Musique", PcVariableFormat.IntegerType, "instance", "2"),
                new("des_i_SndMdf_Ambiance", PcVariableFormat.IntegerType, "model", "-1"),
                new("des_i_SndMdf_Voix", PcVariableFormat.IntegerType, "model", "-10"),
                new("des_f_Fadergroup", PcVariableFormat.FloatType, "instance", "0.35")]
        };
        string[] cinematicAudioFindings = CinematicScriptBehaviorAnalyzer.Analyze(cinematicAudioOwner);
        Check(cinematicAudioFindings.Any(finding => finding.Contains("music 2", StringComparison.Ordinal)
                && finding.Contains("ambience -1 (status query aggregates all attached sound modifiers)", StringComparison.Ordinal)
                && finding.Contains("voice -10 (disabled status-query sentinel)", StringComparison.Ordinal))
            && cinematicAudioFindings.Any(finding => finding.Contains("status mask 0x4", StringComparison.Ordinal)
                && finding.Contains("des_i_ripley=0", StringComparison.Ordinal))
            && cinematicAudioFindings.Any(finding => finding.Contains("mixer tracks 1010, 1020, and 1040", StringComparison.Ordinal)
                && finding.Contains("des_f_Fadergroup (0.35)", StringComparison.Ordinal)),
            "Cine_Events sound callbacks should map owner-local modifier slots, completion status gate, and fade controls from effective OVA values");
        var ownerWithoutPlaybackCallbacks = cineOwner with
        {
            CallbackNames = ["Cine_Events_cine_tests_depart"]
        };
        Check(!CinematicScriptBehaviorAnalyzer.Analyze(ownerWithoutPlaybackCallbacks)
                .Any(finding => finding.Contains("TRAPlayAll", StringComparison.Ordinal)
                    || finding.Contains("TRAReinitAll", StringComparison.Ordinal)),
            "EVE start/reset behavior should only be reported when the corresponding callbacks exist on the model");
        var standaloneTrackCallback = cineOwner with
        {
            CallbackNames = ["Cine_Events_cine_play_eventtrack"]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(standaloneTrackCallback)
                .Any(finding => finding.Contains("i_enLecture (1)", StringComparison.Ordinal))
            && !CinematicScriptBehaviorAnalyzer.Analyze(standaloneTrackCallback)
                .Any(finding => finding.Contains("TRAPlayAll", StringComparison.Ordinal)),
            "A partial callback catalog should report only the EVE behavior proven by callbacks that are present");
        var enabledFlagGate = cineOwner with
        {
            Variables = [.. cineOwner.Variables.Where(variable => variable.Name != "i_tests_a_reussir"),
                new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "128")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(enabledFlagGate)
                .Any(finding => finding.Contains("derived by cine_init (mask 0x80)", StringComparison.Ordinal)),
            "Cine_Events start gates should be derived from OVA fields rather than the serialized transient mask");
        string[] enabledStartGateEvidence = CinematicScriptBehaviorAnalyzer.AnalyzeStartGates(enabledFlagGate);
        var nonCinematicGateOwner = enabledFlagGate with
        {
            CallbackNames = ["Unrelated_init"]
        };
        Check(enabledStartGateEvidence.Any(finding => finding.Contains("derived by cine_init (mask 0x80)", StringComparison.Ordinal))
            && enabledStartGateEvidence.Any(finding => finding.Contains("All enabled tests must pass", StringComparison.Ordinal))
            && enabledStartGateEvidence.All(finding => !finding.Contains("runtime state is known", StringComparison.Ordinal))
            && CinematicScriptBehaviorAnalyzer.AnalyzeStartGates(nonCinematicGateOwner).Length == 0,
            "The reusable Cine_Events gate summary should expose derived gates, retain runtime uncertainty, and ignore unrelated callback owners");
        var capabilityGateActor = new SceneObject
        {
            GaoKey = 0x9E001234,
            Name = "capability gate actor",
            InitialCapabilities = 0x5
        };
        var capabilityGateOwner = cineOwner with
        {
            ObjectReferences = [new("des_o_actor_capa1", capabilityGateActor.GaoKey,
                "instance", capabilityGateActor.Name)],
            Variables = [.. cineOwner.Variables.Where(variable => variable.Name != "i_tests_a_reussir"),
                new("i_tests_a_reussir", PcVariableFormat.IntegerType, "instance", "8"),
                new("des_o_actor_capa1", PcVariableFormat.ObjectReferenceType,
                    "instance", $"0x{capabilityGateActor.GaoKey:X8}"),
                new("des_i_capa_test1", PcVariableFormat.IntegerType, "instance", "5"),
                new("des_i_capa_etat1", PcVariableFormat.IntegerType, "instance", "1")]
        };
        string[] capabilitySnapshotPass = CinematicScriptBehaviorAnalyzer.Analyze(capabilityGateOwner,
            worldObjects: [capabilityGateActor]);
        var capabilitySnapshotFailOwner = capabilityGateOwner with
        {
            Variables = [.. capabilityGateOwner.Variables.Where(variable => variable.Name != "des_i_capa_etat1"),
                new("des_i_capa_etat1", PcVariableFormat.IntegerType, "instance", "0")]
        };
        string[] capabilitySnapshotFail = CinematicScriptBehaviorAnalyzer.Analyze(
            capabilitySnapshotFailOwner, worldObjects: [capabilityGateActor]);
        string[] capabilitySnapshotUnknown = CinematicScriptBehaviorAnalyzer.Analyze(
            capabilityGateOwner, worldObjects: []);
        Check(capabilitySnapshotPass.Any(finding => finding.Contains("snapshot passes", StringComparison.Ordinal)
                && finding.Contains("expected 0x00000005", StringComparison.Ordinal))
            && capabilitySnapshotFail.Any(finding => finding.Contains("snapshot fails", StringComparison.Ordinal)
                && finding.Contains("expected 0x00000000", StringComparison.Ordinal))
            && capabilitySnapshotUnknown.Any(finding => finding.Contains("not loaded in this world", StringComparison.Ordinal)
                && finding.Contains("result is unknown", StringComparison.Ordinal)),
            "Cinematic capability gates should compare Iris's mask/state formula against a uniquely resolved initial actor snapshot and preserve unknown state when unresolved");
        Check(CinematicScriptBehaviorAnalyzer.AnalyzeStartGates(capabilityGateOwner, [])
                .Any(finding => finding.Contains("not loaded in this world", StringComparison.Ordinal)
                    && finding.Contains("result is unknown", StringComparison.Ordinal)),
            "The callback-owner gate summary should preserve unresolved actor capability state in the UI-facing analysis path");
        var zoneGateOwner = cineOwner with
        {
            Variables = [.. cineOwner.Variables.Where(variable => variable.Name != "i_tests_a_reussir"
                    && variable.Name != "des_i_specialFlag_test1"),
                new("des_o_actor_zone1", PcVariableFormat.ObjectReferenceType, "instance", "0x00000001")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(zoneGateOwner)
                .Any(finding => finding.Contains("derived by cine_init (mask 0x1)", StringComparison.Ordinal)
                    && finding.Contains("zone 1 collision", StringComparison.Ordinal)),
            "Object-reference gates should derive from serialized non-null GAO/actor-slot keys");
        var messageGateOwner = cineOwner with
        {
            ObjectReferences = [new("des_o_acteur_msg", 0x9E001C75, "instance", "_PJ_Peyj_le_petit_cochon.gao")],
            Variables = [.. cineOwner.Variables,
                new("des_o_acteur_msg", PcVariableFormat.ObjectReferenceType, "instance", "0x9E001C75"),
                new("des_i_msg_ID", PcVariableFormat.IntegerType, "instance", "46"),
                new("des_i_msg_INT1", PcVariableFormat.IntegerType, "instance", "4")]
        };
        string[] messageGateFindings = CinematicScriptBehaviorAnalyzer.Analyze(messageGateOwner);
        Check(messageGateFindings.Any(finding => finding.Contains("derived by cine_init", StringComparison.Ordinal))
            && messageGateFindings.Any(finding => finding.Contains("queued message ID 46", StringComparison.Ordinal)
                && finding.Contains("Int1 must overlap mask 0x00000004", StringComparison.Ordinal)
                && finding.Contains("first four queued entries", StringComparison.Ordinal)
                && finding.Contains("without consuming them", StringComparison.Ordinal)),
            "Cinematic message start gates should expose their configured queue, message ID, Int1 mask and bounded non-consuming source read");
        var unfilteredMessageGate = messageGateOwner with
        {
            Variables = [.. messageGateOwner.Variables.Where(variable => variable.Name != "des_i_msg_INT1"),
                new("des_i_msg_INT1", PcVariableFormat.IntegerType, "instance", "0")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(unfilteredMessageGate).Any(finding =>
                finding.Contains("queued message ID 46", StringComparison.Ordinal)
                && finding.Contains("Int1 is not filtered because the configured mask is zero", StringComparison.Ordinal)),
            "A zero cinematic message Int1 mask should disable the optional mask comparison");
        var gateQueueOwner = new CinematicScriptOwner(0x500, 0, 0x900, cineOwner.WorldKey,
            uint.MaxValue, uint.MaxValue, "message queue actor", [], [], []);
        var matchingMessageOwner = new CinematicScriptOwner(0x700, 0, 0x901, cineOwner.WorldKey,
            uint.MaxValue, uint.MaxValue, "message event owner", [], [], []);
        var filteredMessageGate = messageGateOwner with
        {
            ObjectReferences = [new("des_o_acteur_msg", gateQueueOwner.OwnerKey, "instance", gateQueueOwner.Name)],
            Variables = [.. messageGateOwner.Variables.Where(variable => variable.Name != "des_o_acteur_msg"),
                new("des_o_acteur_msg", PcVariableFormat.ObjectReferenceType, "instance", $"0x{gateQueueOwner.OwnerKey:X8}")]
        };
        CinematicTimelineMessage CreateGateMessage(uint queueContextKey, int messageType, int int1) =>
            new(1, queueContextKey, 0, 0, default, default, 0, 0, int1, 0, messageType);
        CinematicScriptMessageTrack CreateMessageTrack(uint eventKey, CinematicTimelineMessage message) =>
            new(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, "SceneEvents", eventKey,
                $"message event {eventKey:X8}", [(message.Time, message)]);
        var messageGateTracks = new[]
        {
            CreateMessageTrack(0xA01, CreateGateMessage(gateQueueOwner.OwnerKey, 46, 12)),
            CreateMessageTrack(0xA02, CreateGateMessage(gateQueueOwner.OwnerKey, 46, 2)),
            CreateMessageTrack(0xA03, CreateGateMessage(gateQueueOwner.OwnerKey, 53, 12)),
            CreateMessageTrack(0xA04, CreateGateMessage(0x501, 46, 12)),
            new CinematicScriptMessageTrack(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey,
                "Cinematic", 0xA01, "duplicate cinematic source",
                [(1, CreateGateMessage(gateQueueOwner.OwnerKey, 46, 12))])
        };
        CinematicScriptNamedReference[] messageGateReferences =
        [
            new(gateQueueOwner.WorldKey, gateQueueOwner.OwnerKey, gateQueueOwner.Name, IsObject: true),
            new(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, matchingMessageOwner.Name, IsObject: false),
            new(filteredMessageGate.WorldKey, filteredMessageGate.OwnerKey, filteredMessageGate.Name, IsObject: false),
            new(gateQueueOwner.WorldKey, 0x501, "other queue actor", IsObject: true)
        ];
        var messageGateCandidates = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(
            filteredMessageGate, messageGateTracks, messageGateReferences);
        Check(messageGateCandidates is [{ Track.EventKey: 0xA01 }]
            && messageGateCandidates[0].QueueObject.Key == gateQueueOwner.OwnerKey
            && messageGateCandidates[0].Sources.Length == 2,
            "Message-gate candidates should join MSGSendAll EVE context to the configured queue, filter ID and Int1 overlap, and merge duplicate serialized source links");
        Check(CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA01, 2, messageGateTracks, messageGateReferences) == 1
            && CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA01, .5f, messageGateTracks, messageGateReferences) == null
            && CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA02, 2, messageGateTracks, messageGateReferences) == null,
            "Message-gate preview timing should require the selected clip, a decoded queue match, and an in-range timestamp");
        var nestedGateMessage = CreateGateMessage(gateQueueOwner.OwnerKey, 46, 12);
        var nestedGateTrack = new CinematicScriptMessageTrack(matchingMessageOwner.WorldKey,
            matchingMessageOwner.OwnerKey, "SceneEvents", 0xA10, "nested message event", [(1, nestedGateMessage)], 4);
        var nestedScheduledMessage = new CinematicScheduledMessage(matchingMessageOwner.WorldKey,
            matchingMessageOwner.OwnerKey, 0xA10, 4, CinematicOwnerExecutionRoute.NestedEventCall,
            1, 4.5f, nestedGateMessage with { Time = 4.5f }, null, gateQueueOwner.OwnerKey,
            CinematicMessageQueueOwnerEvidence.IrisEveTrackContext,
            "synthetic nested EVE payload", "runtime delivery not evaluated");
        Check(CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA11, 6, [nestedGateTrack], messageGateReferences,
                [nestedScheduledMessage]) == 4.5f
            && CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA11, 4, [nestedGateTrack], messageGateReferences,
                [nestedScheduledMessage]) == null,
            "Message-gate preview timing should map a matching nested SceneEvents message onto its scheduled cinematic time");
        Check(CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA11, 6, [nestedGateTrack], messageGateReferences,
                [nestedScheduledMessage with { IrisQueueOwnerCandidateKey = 0x501 }]) == null
            && CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA11, 6, [nestedGateTrack], messageGateReferences,
                [nestedScheduledMessage with { QueueOwnerEvidence = CinematicMessageQueueOwnerEvidence.Unresolved }]) == null,
            "Nested message-gate timing must reject schedules with a mismatched or unresolved Iris queue-owner candidate");
        var contextMismatches = CinematicScriptBehaviorAnalyzer.FindMessageGateContextMismatches(
            filteredMessageGate, messageGateTracks, messageGateReferences);
        Check(contextMismatches is [{ Track.EventKey: 0xA04 }]
            && contextMismatches[0].ExecutionContext.Key == 0x501
            && contextMismatches[0].QueueObject.Key == gateQueueOwner.OwnerKey,
            "Messages passing ID and Int1 filters but executing on another resolved object should be surfaced as context mismatches");
        var helperTextMessageGate = filteredMessageGate with
        {
            CallbackNames = ["helper_text_hlptxt_init"],
            ModelFunctionNames =
            [
                "helper_text_hlptxt_init",
                "helper_text_hlptxt_tests_depart",
                "helper_text_hlptxt_tests_wait",
                "helper_text_hlptxt_exec_ask_display"
            ]
        };
        Check(CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(helperTextMessageGate,
                messageGateTracks, messageGateReferences)
                ?.Select(candidate => candidate.Track.EventKey).SequenceEqual([0xA01u]) == true,
            "The generic EVE-to-queue join should also serve helper-text owners using the same source-verified gate predicate");
        var helperTextBehavior = CinematicScriptBehaviorAnalyzer.Analyze(helperTextMessageGate);
        Check(helperTextBehavior.Any(finding => finding.Contains("condition checks [helper_text_hlptxt_init", StringComparison.Ordinal)
                && finding.Contains("helper_text_hlptxt_tests_depart", StringComparison.Ordinal))
            && helperTextBehavior.Any(finding => finding.Contains("message gate watches", StringComparison.Ordinal)
                && finding.Contains(gateQueueOwner.Name, StringComparison.Ordinal))
            && helperTextBehavior.Any(finding => finding.Contains("helper_text_hlptxt_tests_wait", StringComparison.Ordinal)
                && finding.Contains("helper_text_hlptxt_exec_ask_display", StringComparison.Ordinal)
                && finding.Contains("ai_compiledscripts_helper_text.c:2227", StringComparison.Ordinal)
                && finding.Contains("function key 7100E7B3", StringComparison.Ordinal)
                && finding.Contains("ai_bge/ai_compiledscripts_helper_text.c:1175", StringComparison.Ordinal)),
            "Helper-text behavior should verify the indirect callback key assignment and dispatch site on the reachable init-to-display path even when only init is assigned");
        var unfilteredQueueGate = filteredMessageGate with
        {
            Variables = [.. filteredMessageGate.Variables.Where(variable => variable.Name != "des_i_msg_INT1"),
                new("des_i_msg_INT1", PcVariableFormat.IntegerType, "instance", "0")]
        };
        CinematicScriptMessageTrack CreateExtendedMessageTrack(uint eventKey, uint targetKey, uint payload) =>
            new(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, "SceneEvents", eventKey,
                $"MSGSendEx event {eventKey:X8}", [])
            {
                EngineCalls = [(2.5f, new CinematicLiteralCall(targetKey, 0x1F76, [46, payload]))]
            };
        var extendedMessageTracks = new[]
        {
            CreateExtendedMessageTrack(0xA05, gateQueueOwner.OwnerKey, 2),
            CreateExtendedMessageTrack(0xA06, 0x501, 1)
        };
        var extendedMessageCandidates = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(
            unfilteredQueueGate, extendedMessageTracks, messageGateReferences);
        Check(extendedMessageCandidates is [{ Track.EventKey: 0xA05 }]
            && extendedMessageCandidates[0].Call.Arguments[0] == 46
            && extendedMessageCandidates[0].QueueObject.Key == gateQueueOwner.OwnerKey,
            "MSGSendEx should match a message gate only when its queue target and ID match and the gate Int1 mask is zero");
        var extendedMessageMismatches = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateTargetMismatches(
            unfilteredQueueGate, extendedMessageTracks, messageGateReferences);
        Check(extendedMessageMismatches is [{ Track.EventKey: 0xA06 }]
            && extendedMessageMismatches[0].TargetObject.Key == 0x501,
            "Same-ID MSGSendEx calls to a different resolved queue target should remain visible as context mismatches");
        Check(CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(filteredMessageGate,
                extendedMessageTracks, messageGateReferences)?.Length == 0,
            "MSGSendEx must not be considered a message-gate match when the configured Int1 mask is nonzero");
        var extendedMessageInt1Mismatches =
            CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateInt1MaskMismatches(filteredMessageGate,
                extendedMessageTracks, messageGateReferences);
        Check(extendedMessageInt1Mismatches is [{ Track.EventKey: 0xA05 }]
            && extendedMessageInt1Mismatches[0].QueueObject.Key == gateQueueOwner.OwnerKey,
            "Same-target MSGSendEx calls must be surfaced as rejected when their zero Int1 cannot satisfy a nonzero gate mask");
        CinematicScriptMessageTrack CreateMessageAllTrack(uint eventKey, uint executionKey, int messageInt1) =>
            new(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, "SceneEvents", eventKey,
                $"MSGSendAll event {eventKey:X8}", [])
            {
                EngineCalls = [(2.75f, new CinematicLiteralCall(executionKey, 0x1F78,
                    [46, 0x601, 0x602, 0, 0, 0, 0, 0, 0, 0, 0, unchecked((uint)messageInt1), 9]))]
            };
        var messageAllTracks = new[]
        {
            CreateMessageAllTrack(0xA07, gateQueueOwner.OwnerKey, 4),
            CreateMessageAllTrack(0xA08, gateQueueOwner.OwnerKey, 2),
            CreateMessageAllTrack(0xA09, 0x501, 4)
        };
        var cinematicMessageAllTrack = messageAllTracks[0] with { LinkName = "Cinematic" };
        var messageAllCandidates = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(
            filteredMessageGate, messageAllTracks, messageGateReferences);
        Check(messageAllCandidates is [{ Track.EventKey: 0xA07 }]
            && messageAllCandidates[0].QueueObject.Key == gateQueueOwner.OwnerKey,
            "MSGSendAll should match the queue context and message ID only when its serialized Int1 overlaps the gate mask");
        Check(CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA07, 3, [cinematicMessageAllTrack], messageGateReferences) == 2.75f
            && CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(filteredMessageGate,
                matchingMessageOwner.WorldKey, 0xA07, 3, [messageAllTracks[0]], messageGateReferences) == null,
            "Message-gate preview timing should also recognize a matching literal MSGSendAll command");
        var messageAllContextMismatches = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateTargetMismatches(
            filteredMessageGate, messageAllTracks, messageGateReferences);
        Check(messageAllContextMismatches is [{ Track.EventKey: 0xA09 }]
            && messageAllContextMismatches[0].TargetObject.Key == 0x501,
            "MSGSendAll messages on another resolved execution object's queue should be shown as context mismatches");
        var messageAllInt1Mismatches = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateInt1MaskMismatches(
            filteredMessageGate, messageAllTracks, messageGateReferences);
        Check(messageAllInt1Mismatches is [{ Track.EventKey: 0xA08 }],
            "MSGSendAll with matching ID and queue context but non-overlapping Int1 must be exposed as a rejected candidate");
        CinematicScriptMessageTrack CreateMessageAllOwnerTrack(uint eventKey, uint senderKey,
            int messageId, int messageInt1) =>
            new(matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, "SceneEvents", eventKey,
                $"MSGSendAllOwner event {eventKey:X8}", [], 4)
            {
                EngineCalls = [(3.25f, new CinematicLiteralCall(senderKey, 0x1F7D,
                    [unchecked((uint)messageId), 0x701, 0, 0, 0, 0, 0, 0, 0, 0,
                        unchecked((uint)messageInt1), 17]))]
            };
        var messageAllOwnerTracks = new[]
        {
            CreateMessageAllOwnerTrack(0xA0A, 0x501, 46, 4),
            CreateMessageAllOwnerTrack(0xA0B, gateQueueOwner.OwnerKey, 47, 4),
            CreateMessageAllOwnerTrack(0xA0C, gateQueueOwner.OwnerKey, 46, 2)
        };
        var ownerQueueRegistration = new CinematicScriptMessageTrack(
            matchingMessageOwner.WorldKey, matchingMessageOwner.OwnerKey, "SceneEvents", 0xA0A,
            "Owner queue registration", [], 4)
        {
            EngineCalls = [(2.0f, new CinematicLiteralCall(gateQueueOwner.OwnerKey, 0x1F7E, []))]
        };
        var ownerQueueRegistrationOtherChannel = ownerQueueRegistration with
        {
            ChannelIndex = 5,
            EngineCalls = [(1.0f, new CinematicLiteralCall(0x501, 0x1F7E, []))]
        };
        var ownerQueueRegistrationAfterSend = ownerQueueRegistration with
        {
            EngineCalls = [(3.5f, new CinematicLiteralCall(0x501, 0x1F7E, []))]
        };
        var ownerQueueTracksWithRegistrations = messageAllOwnerTracks
            .Append(ownerQueueRegistration)
            .Append(ownerQueueRegistrationOtherChannel)
            .Append(ownerQueueRegistrationAfterSend)
            .ToArray();
        var ownerQueueCandidates = CinematicScriptBehaviorAnalyzer.FindOwnerQueueMessageCandidates(
            filteredMessageGate, ownerQueueTracksWithRegistrations, messageGateReferences);
        Check(ownerQueueCandidates is [{ Track.EventKey: 0xA0A }]
            && ownerQueueCandidates[0].GateQueueObject.Key == gateQueueOwner.OwnerKey
            && ownerQueueCandidates[0].Call.Target == 0x501
            && ownerQueueCandidates[0].Call.Arguments[1] == 0x701
            && ownerQueueCandidates[0].PriorSameChannelRegistrations is
                [{ Time: 2.0f, Call.Target: var registeredOwnerKey }]
            && registeredOwnerKey == gateQueueOwner.OwnerKey,
            "MSGSendAllOwner should expose the latest earlier same-channel MSGRegOwner as a candidate without resolving the live queue");
        Check(CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(filteredMessageGate,
                ownerQueueTracksWithRegistrations, messageGateReferences)?.Length == 0,
            "MSGSendAllOwner must never be promoted to a resolved per-object queue match");
        var implicitZoneOwner = zoneGateOwner with
        {
            Variables = [.. zoneGateOwner.Variables.Where(variable => variable.Name != "des_o_actor_zone1"),
                new("des_i_actor_type1", PcVariableFormat.IntegerType, "instance", "1")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(implicitZoneOwner)
                .Any(finding => finding.Contains("derived by cine_init (mask 0x1)", StringComparison.Ordinal)),
            "A configured actor type should cause cine_init to bind the missing first zone to its owner");
        var nonBinaryModes = cineOwner with
        {
            Variables = [.. cineOwner.Variables.Where(variable => variable.Name != "des_i_ripley"
                    && variable.Name != "des_i_cine_independante"),
                new("des_i_ripley", PcVariableFormat.IntegerType, "instance", "2"),
                new("des_i_cine_independante", PcVariableFormat.IntegerType, "instance", "2")]
        };
        string[] nonBinaryFindings = CinematicScriptBehaviorAnalyzer.Analyze(nonBinaryModes);
        Check(nonBinaryFindings.Any(finding => finding.Contains("marked independent", StringComparison.Ordinal))
            && !nonBinaryFindings.Any(finding => finding.Contains("Initialization suppression", StringComparison.Ordinal)),
            "Nonzero independent/Ripley modes should follow source truthiness and bypass shared checks");
        var invalidFlagGate = enabledFlagGate with
        {
            Variables = [.. enabledFlagGate.Variables.Where(variable => variable.Name != "des_i_specialFlag_test1"),
                new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "0")]
        };
        string[] invalidFlagFindings = CinematicScriptBehaviorAnalyzer.Analyze(invalidFlagGate);
        Check(invalidFlagFindings.Any(finding => finding.Contains("No optional cine_tests_depart gates", StringComparison.Ordinal))
            && !invalidFlagFindings.Any(finding => finding.Contains("Start test 1 is active", StringComparison.Ordinal)),
            "A zero special-flag OVA value should not contribute a gate when cine_init rebuilds the mask");
        var monitorOwner = new CinematicScriptOwner(0xBF000DA6, 0, 0x4901BEE6, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "Cine_ecran_explik_invent.gao",
            ["mpegplayer_init", "mpegplayer_loop"],
            [new("des_o_Interrupteur", 0xC100AEF9, "instance", "OBJ_Interrupteur_ordinateur.gao")], []);
        string[] monitorFindings = CinematicScriptBehaviorAnalyzer.Analyze(monitorOwner);
        Check(monitorFindings.Any(finding => finding.Contains("capability 0x100", StringComparison.Ordinal)
                && finding.Contains("OBJ_Interrupteur_ordinateur.gao", StringComparison.Ordinal)
                )
            && monitorFindings.Any(finding => finding.Contains("procedural material", StringComparison.Ordinal)),
            "Shared MPEG-player callbacks should explain their generic switch and material relationship");
        var zoneOwner = new CinematicScriptOwner(0x0200483D, 0, 0x35008316, 0xD9005EE0,
            uint.MaxValue, uint.MaxValue, "Detect_Peyj_ultratag_2777.gao",
            ["Detect_o_Zone_doz_init", "Detect_o_Zone_doz_detec_o_BV", "Detect_o_Zone_doz_detec_o_ZDE"],
            [new("des_acteur_detecte1", 0x9E001C75, "instance", "_PJ_Peyj_le_petit_cochon.gao")],
            [new("des_i_SPFlag_set01", PcVariableFormat.IntegerType, "model", "-1"),
             new("des_i_SPFlag_set01", PcVariableFormat.IntegerType, "instance", "2777"),
             new("des_i_SPFlag_reset01", PcVariableFormat.IntegerType, "instance", "-1"),
             new("des_i_SPFlag_invert01", PcVariableFormat.IntegerType, "instance", "2794"),
             new("des_f_minuterie", PcVariableFormat.FloatType, "model", "0")]);
        string[] zoneFindings = CinematicScriptBehaviorAnalyzer.Analyze(zoneOwner);
        Check(zoneFindings.Any(finding => finding.Contains("identity flag 0x400", StringComparison.Ordinal)
                && finding.Contains("pivot index 2", StringComparison.Ordinal))
            && zoneFindings.Any(finding => finding.Contains("sets owner capability 0x100", StringComparison.Ordinal)
                && finding.Contains("clears that capability", StringComparison.Ordinal))
            && zoneFindings.Any(finding => finding.Contains("_PJ_Peyj_le_petit_cochon.gao", StringComparison.Ordinal))
            && zoneFindings.Any(finding => finding.Contains("sets saved flag 2777", StringComparison.Ordinal)
                && finding.Contains("sets raw saved-flag index 2794", StringComparison.Ordinal))
            && zoneFindings.Any(finding => finding.Contains("raw saved-flag index 2794 is cleared on trigger exit", StringComparison.Ordinal)
                && finding.Contains("set/reset flag changes persist", StringComparison.Ordinal))
            && zoneFindings.Any(finding => finding.Contains("not proof that collision occurred", StringComparison.Ordinal)),
            "Detect_o_Zone analysis should derive generic collision mode, actor link, saved-flag effects and runtime limits from callbacks/OVA");
        string[] bvZoneFindings = CinematicScriptBehaviorAnalyzer.Analyze(zoneOwner with
        {
            SerializedGaoFlags = 0
        });
        string[] pivotZoneFindings = CinematicScriptBehaviorAnalyzer.Analyze(zoneOwner with
        {
            SerializedGaoFlags = 0x400
        });
        Check(bvZoneFindings.Any(finding => finding.Contains("identity flag 0x400 clear", StringComparison.Ordinal)
                && finding.Contains("selects bounding-volume overlap", StringComparison.Ordinal))
            && pivotZoneFindings.Any(finding => finding.Contains("identity flag 0x400 set", StringComparison.Ordinal)
                && finding.Contains("selects ZDE pivot collision at pivot index 2", StringComparison.Ordinal)),
            "Detect_o_Zone should report the actual initial BV/ZDE route from each owner's serialized GAO flags");
        var cineFlagReader = new CinematicScriptOwner(0xBF00012E, 0, 0x350091C3, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "cinematic event reader",
            ["Cine_Events_cine_firstinit", "Cine_Events_cine_tests_depart", "Cine_Events_Cine_exec_EndOfEvents"], [],
            [new("des_i_ripley", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "2777"),
             new("des_i_SPFlag_associe_vrai", PcVariableFormat.IntegerType, "instance", "2794")]);
        var genericCallbackFlagOwner = new CinematicScriptOwner(0xBF000130, 0, 0x71000001, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "generic global-flag reader",
            ["Activateur_avec_SF_Acs_init"], [],
            [new("des_i_SpFlag", PcVariableFormat.IntegerType, "instance", "2777"),
             new("des_i_SpFlag_etat", PcVariableFormat.IntegerType, "model", "1")])
        {
            ModelFunctionNames = ["Activateur_avec_SF_Acs_init"]
        };
        var genericCallbackFlagAccess = CinematicScriptFlagFlowAnalyzer.GetAccesses(genericCallbackFlagOwner)
            .FirstOrDefault(access => access.Flag == 2777 && access.Access == "reads"
                && access.Description.Contains("ARRGlobalIntFlagTest", StringComparison.Ordinal));
        Check(genericCallbackFlagAccess?.RequiredSet == true
            && genericCallbackFlagAccess.Description.Contains("Activateur_avec_SF_Acs_init", StringComparison.Ordinal)
            && genericCallbackFlagAccess.Description.Contains("ai_compiledscripts_activateur_avec_sf.c:27", StringComparison.Ordinal)
            && genericCallbackFlagAccess.Description.Contains("configured comparison requires the flag set", StringComparison.Ordinal),
            "Saved-flag reads should resolve source calls, OVA flag IDs and required polarity without callback-name rules");
        var localFlagAliasOwner = new CinematicScriptOwner(0xBF000131, 0, 0x71000001,
            cineFlagReader.WorldKey, uint.MaxValue, uint.MaxValue, "local flag alias fixture", [], [], []);
        var localFlagAliasCall = sourceArgumentCall with
        {
            Arguments = ["flagAlias"],
            ArgumentAssignments =
            [
                new("flagAlias", "2777", 10, 0),
                new("flagAlias", "2778", 30, 0)
            ],
            Line = 20
        };
        bool resolvesOnlyPriorFlagAssignment = CinematicScriptFlagFlowAnalyzer.TryResolveGlobalFlagArgument(
            localFlagAliasOwner, localFlagAliasCall, "flagAlias", out int priorFlagValue);
        bool rejectsAmbiguousFlagAssignment = CinematicScriptFlagFlowAnalyzer.TryResolveGlobalFlagArgument(
            localFlagAliasOwner, localFlagAliasCall with
            {
                ArgumentAssignments = [new("flagAlias", "2777", 10, 0, IsUnique: false)]
            }, "flagAlias", out _);
        Check(resolvesOnlyPriorFlagAssignment && priorFlagValue == 2777 && !rejectsAmbiguousFlagAssignment,
            "Saved-flag aliases must use a unique assignment at or before the call and must ignore later writes");
        var matchingFlagWriter = zoneOwner with
        {
            OwnerKey = 0x0200483E,
            Variables = [new("des_i_SPFlag_set01", PcVariableFormat.IntegerType, "instance", "2777")]
        };
        var mismatchedFlagWriter = matchingFlagWriter with
        {
            OwnerKey = 0x0200483F,
            Variables = [new("des_i_SPFlag_set01", PcVariableFormat.IntegerType, "instance", "2778")]
        };
        var flagLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(cineFlagReader,
            [cineFlagReader, matchingFlagWriter, mismatchedFlagWriter]);
        Check(flagLinks.Length == 1 && flagLinks[0].RelatedOwner.OwnerKey == matchingFlagWriter.OwnerKey
            && flagLinks[0].Flag == 2777 && flagLinks[0].Direction.Contains("related owner writes des_i_SPFlag_set01", StringComparison.Ordinal)
            && flagLinks[0].Direction.Contains("this owner reads des_i_specialFlag_test1", StringComparison.Ordinal),
            "Cinematic gate owners should link to matching trigger writers by decoded flag value, while excluding a different flag");
        var eventFlagWriter = new CinematicScriptOwner(0xD0002E30, 0, 0x350091C3, 0xBF000127,
            uint.MaxValue, uint.MaxValue, "event flag writer", [], [], []);
        var eventFlagTrack = new CinematicScriptEventTrack(eventFlagWriter.WorldKey, eventFlagWriter.OwnerKey,
            "SceneEvents", 0xBF000123, "event flag fixture", 2,
            [(1.25f, new CinematicLiteralCall(eventFlagWriter.OwnerKey, 0x2916, [2777u]))], []);
        var eventFlagLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(eventFlagWriter,
            [cineFlagReader, eventFlagWriter], [eventFlagTrack]);
        Check(eventFlagLinks.Length == 1 && eventFlagLinks[0].RelatedOwner.OwnerKey == cineFlagReader.OwnerKey
            && eventFlagLinks[0].Flag == 2777
            && eventFlagLinks[0].Direction.Contains("ARRGlobalIntFlagSet", StringComparison.Ordinal)
            && eventFlagLinks[0].Direction.Contains("BF000123 at 1.25s", StringComparison.Ordinal)
            && eventFlagLinks[0].Direction.Contains("requires set", StringComparison.Ordinal)
            && eventFlagLinks[0].Direction.Contains("configured write matches the reader predicate", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(eventFlagTrack.Calls[0].Call)
                == "set global saved flag 2777",
            "Literal event flag writes should form source-described links to compatible Cine_Events start gates");
        var eventFlagReadTrack = new CinematicScriptEventTrack(cineFlagReader.WorldKey, cineFlagReader.OwnerKey,
            "SceneEvents", 0xBF000125, "event flag reader fixture", 2,
            [(1.75f, new CinematicLiteralCall(cineFlagReader.OwnerKey, 0x2918, [2777u]))], []);
        var eventFlagReadLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(matchingFlagWriter,
            [cineFlagReader, matchingFlagWriter], [eventFlagReadTrack]);
        Check(eventFlagReadLinks.Any(link => link.RelatedOwner.OwnerKey == cineFlagReader.OwnerKey
                && link.Flag == 2777
                && link.Direction.Contains("related owner reads ARRGlobalIntFlagTest", StringComparison.Ordinal)
                && link.Direction.Contains("SceneEvents BF000125 at 1.75s tests this global flag", StringComparison.Ordinal))
            && CinematicEngineFunctionCatalog.Describe(eventFlagReadTrack.Calls[0].Call)
                == "test whether global saved flag 2777 is set",
            "Literal ARRGlobalIntFlagTest calls should add event-track readers to the shared saved-flag graph");
        var clearFlagReader = cineFlagReader with
        {
            OwnerKey = 0xBF000140,
            Name = "clear-gated cinematic",
            Variables = [new("des_i_specialFlag_test1", PcVariableFormat.IntegerType, "instance", "12777")]
        };
        var clearEventTrack = eventFlagTrack with
        {
            EventKey = 0xBF000124,
            Calls = [(1.5f, new CinematicLiteralCall(eventFlagWriter.OwnerKey, 0x2917, [2777u]))]
        };
        var clearEventLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(eventFlagWriter,
            [clearFlagReader, eventFlagWriter], [clearEventTrack]);
        Check(clearEventLinks.Length == 1
            && clearEventLinks[0].Direction.Contains("ARRGlobalIntFlagClear", StringComparison.Ordinal)
            && clearEventLinks[0].Direction.Contains("clears this global flag", StringComparison.Ordinal)
            && clearEventLinks[0].Direction.Contains("requires clear", StringComparison.Ordinal)
            && clearEventLinks[0].Direction.Contains("configured write matches the reader predicate", StringComparison.Ordinal)
            && CinematicEngineFunctionCatalog.Describe(clearEventTrack.Calls[0].Call)
                == "clear global saved flag 2777",
            "Literal event flag clears should preserve the clear-polarity gate semantics in their graph links");
        var opposingEventTrack = clearEventTrack with
        {
            EventKey = 0xBF000126,
            Calls = [(1.5f, new CinematicLiteralCall(eventFlagWriter.OwnerKey, 0x2916, [2777u]))]
        };
        var opposingFlagLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(eventFlagWriter,
            [clearFlagReader, eventFlagWriter], [opposingEventTrack]);
        Check(opposingFlagLinks.Length == 1
            && opposingFlagLinks[0].Direction.Contains("configured write opposes the reader predicate", StringComparison.Ordinal),
            "Shared-flag links should distinguish a known write value that opposes the reader's configured state");
        var selectedFlagLinkedScope = CinematicScriptScope.ForClip([cineFlagReader, matchingFlagWriter],
            cineFlagReader.WorldKey, [], [], selectedOwnerId: matchingFlagWriter.Id);
        Check(selectedFlagLinkedScope.Any(owner => owner.OwnerKey == matchingFlagWriter.OwnerKey),
            "An owner reached through the saved-flag graph should remain selectable outside the initial cinematic scope");
        var reverseFlagLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(matchingFlagWriter,
            [cineFlagReader, matchingFlagWriter, mismatchedFlagWriter]);
        Check(reverseFlagLinks.Length == 1 && reverseFlagLinks[0].RelatedOwner.OwnerKey == cineFlagReader.OwnerKey,
            "Shared special-flag links should be navigable from trigger writers as well as cinematic readers");
        var unrelatedFlagCallbacks = zoneOwner with
        {
            CallbackNames = [],
            OwnerKey = 0x0200483F
        };
        Check(CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(unrelatedFlagCallbacks,
                [cineFlagReader, unrelatedFlagCallbacks]).Length == 0,
            "Flag-like OVA fields without a recognized source callback family should not create semantic links");
        var clearFlagDetector = new CinematicScriptOwner(0xD0002E2C, 0, 0x7100E398, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "clear-flag detector",
            ["Detect_o_4SpecialFlags_D4sf_wait"], [],
            [new("SF_1", PcVariableFormat.IntegerType, "instance", "12777")]);
        Check(CinematicScriptFlagFlowAnalyzer.GetAccesses(clearFlagDetector)
                .Any(access => access.Flag == 2777 && access.Access == "reads"
                    && access.VariableName == "SF_1"
                    && access.Description.Contains("requires clear", StringComparison.Ordinal)),
            "Encoded four-flag values above 10000 should link to the decoded flag index with clear polarity");
        var untimedZoneAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(zoneOwner);
        Check(untimedZoneAccesses.Any(access => access.Flag == 2794 && access.VariableName == "des_i_SPFlag_invert01"
                && access.WrittenSet == true && access.Description.Contains("collision entry", StringComparison.Ordinal))
            && untimedZoneAccesses.Any(access => access.Flag == 2794 && access.VariableName == "des_i_SPFlag_invert01"
                && access.WrittenSet == false && access.Description.Contains("collision exit", StringComparison.Ordinal)),
            "Untimed Detect_o_Zone invert flags should expose raw-index set-on-entry and clear-on-exit writes");
        var encodedInvertWriter = zoneOwner with
        {
            Variables = [new("des_i_SPFlag_invert01", PcVariableFormat.IntegerType, "instance", "12794"),
                new("des_f_minuterie", PcVariableFormat.FloatType, "instance", "2.5")]
        };
        var timedInvertAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(encodedInvertWriter);
        Check(timedInvertAccesses.Any(access => access.Flag == 12794 && access.WrittenSet == true
                && access.Description.Contains("raw saved-flag index", StringComparison.Ordinal))
            && timedInvertAccesses.Any(access => access.Flag == 2794 && access.WrittenSet == null
                && access.Description.Contains("modulo 10000", StringComparison.Ordinal))
            && !timedInvertAccesses.Any(access => access.Flag == 12794 && access.WrittenSet == false),
            "Timed Detect_o_Zone invert flags should retain the raw entry set but modulo-normalize only the timed toggle");
        var unknownTimerZone = encodedInvertWriter with
        {
            Variables = [new("des_i_SPFlag_invert01", PcVariableFormat.IntegerType, "instance", "12794")]
        };
        Check(CinematicScriptFlagFlowAnalyzer.GetAccesses(unknownTimerZone)
                .Any(access => access.Flag == 12794 && access.WrittenSet == true)
            && !CinematicScriptFlagFlowAnalyzer.GetAccesses(unknownTimerZone)
                .Any(access => access.Flag == 2794),
            "Detect_o_Zone should not invent timed modulo writes when the effective timer is unavailable");
        var singleFlagDetector = new CinematicScriptOwner(0xD0002E2D, 0, 0x350095C9, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "special flag detector",
            ["Detect_o_SpecialFlags_dosf_init", "Detect_o_SpecialFlags_dosf_tests"], [],
            [new("des_i_specialFlag_vrai1", PcVariableFormat.IntegerType, "instance", "2777"),
             new("des_i_specialFlag_faux1", PcVariableFormat.IntegerType, "instance", "12778"),
             new("des_i_nbre_conditions", PcVariableFormat.IntegerType, "instance", "2"),
             new("f_frequence_test", PcVariableFormat.FloatType, "instance", "0.5")])
        {
            ModelFunctionNames = ["Detect_o_SpecialFlags_dosf_init", "Detect_o_SpecialFlags_dosf_tests"]
        };
        var singleFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(singleFlagDetector);
        string[] singleFlagFindings = CinematicScriptBehaviorAnalyzer.Analyze(singleFlagDetector);
        Check(singleFlagAccesses.Any(access => access.Flag == 2777 && access.VariableName == "des_i_specialFlag_vrai1"
                && access.Description == "requires set")
            && singleFlagAccesses.Any(access => access.Flag == 12778 && access.VariableName == "des_i_specialFlag_faux1"
                && access.Description == "requires clear")
            && singleFlagAccesses.Any(access => access.Flag == 2777 && access.VariableName == "ARRBitTest"
                && access.RequiredSet == true
                && access.Description.Contains("Flag_speciaux_sauve", StringComparison.Ordinal))
            && singleFlagAccesses.Any(access => access.Flag == 12778 && access.VariableName == "ARRBitTest"
                && access.RequiredSet == false
                && access.Description.Contains("Flag_speciaux_sauve", StringComparison.Ordinal))
            && singleFlagFindings.Any(finding => finding.Contains("counts passing predicates", StringComparison.Ordinal)
                && finding.Contains("at least 2 pass", StringComparison.Ordinal))
            && singleFlagFindings.Any(finding => finding.Contains("f_frequence_test=0.5 seconds", StringComparison.Ordinal)),
            "The single-special-flag detector should expose source-derived bit-test polarity, threshold and polling interval");
        var actionReactionFlagWriter = new CinematicScriptOwner(0xD0002E2F, 0, 0, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "action reaction saved-flag writer",
            ["action_reaction_ar_action"], [],
            [new("des_SF_je_suis_active", PcVariableFormat.IntegerType, "instance", "2777")])
        {
            ModelFunctionNames = ["action_reaction_ar_action"]
        };
        Check(CinematicScriptFlagFlowAnalyzer.GetAccesses(actionReactionFlagWriter)
                .Any(access => access.Flag == 2777 && access.VariableName == "ARRBitSet"
                    && access.WrittenSet == true
                    && access.Description.Contains("Flag_speciaux_sauve", StringComparison.Ordinal)),
            "ARRBitSet calls should resolve source-backed saved-flag writes and their OVA argument");
        var savedFlagRangeOwner = new CinematicScriptOwner(0xD0002E31, 0, 0xD0002E24, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "saved-flag range fixture", [], [], []);
        var setFlagRangeCall = new CinematicScriptReachableEngineCall("range_fixture", "ARRBitSetRange", "",
            ["(int*)Detect_o_Zone_univVars_Flag_speciaux_sauve + 4", "2777", "2780"], [], [], [], [],
            "fixture.c", 17, "range_fixture");
        var setFlagRangeAccesses = CinematicScriptFlagFlowAnalyzer.ResolveSavedFlagRangeAccesses(
            savedFlagRangeOwner, setFlagRangeCall);
        var clearFlagRangeAccesses = CinematicScriptFlagFlowAnalyzer.ResolveSavedFlagRangeAccesses(
            savedFlagRangeOwner, setFlagRangeCall with { Api = "ARRBitClearRange" });
        Check(setFlagRangeAccesses.Select(access => access.Flag).SequenceEqual([2777, 2778, 2779, 2780])
                && setFlagRangeAccesses.All(access => access.WrittenSet == true
                    && access.Description.Contains("inclusive range 2777..2780", StringComparison.Ordinal))
                && clearFlagRangeAccesses.Length == 4 && clearFlagRangeAccesses.All(access => access.WrittenSet == false),
            "Iris saved-flag range operations should expand inclusive valid ranges into set/clear accesses with provenance");
        Check(CinematicScriptFlagFlowAnalyzer.ResolveSavedFlagRangeAccesses(savedFlagRangeOwner,
                setFlagRangeCall with { Arguments = ["unrelated_buffer", "2777", "2780"] }).Length == 0
            && CinematicScriptFlagFlowAnalyzer.ResolveSavedFlagRangeAccesses(savedFlagRangeOwner,
                setFlagRangeCall with { Arguments = ["Flag_speciaux_sauve", "2780", "2777"] }).Length == 0
            && CinematicScriptFlagFlowAnalyzer.ResolveSavedFlagRangeAccesses(savedFlagRangeOwner,
                setFlagRangeCall with { Arguments = ["Flag_speciaux_sauve", "1", "35872"] }).Length == 0,
            "Saved-flag range analysis should reject unrelated buffers, reversed bounds and indices outside Iris-resolved storage");
        var nonSavedBitsetOwner = new CinematicScriptOwner(0xD0002E30, 0, 0, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "non-saved bitset user",
            ["Garde_Barriere_Mine_GBM_attaque"], [],
            [new("des_i_sf_mort", PcVariableFormat.IntegerType, "instance", "2777")])
        {
            ModelFunctionNames = ["Garde_Barriere_Mine_GBM_attaque"]
        };
        Check(!CinematicScriptFlagFlowAnalyzer.GetAccesses(nonSavedBitsetOwner)
                .Any(access => access.VariableName is "ARRBitTest" or "ARRBitSet" or "ARRBitClear"
                    or "ARRBitSetRange" or "ARRBitClearRange"),
            "ARRBit accesses on Planetes_CurHour or other non-save stores must not be mislabeled as global saved flags");
        var capabilityDetector = new CinematicScriptOwner(0xD0002E2E, 0, 0x35009479, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "capability detector",
            ["Detect_o_Capa_doc_init", "Detect_o_Capa_doc_toutes_a_vrai"],
            [new("des_o_actor_capa", 0x9E001C75, "instance", "_PJ_Peyj_le_petit_cochon.gao"),
             new("des_o_actor_capa", 0x71003F5A, "instance", "_PJ_Sally.gao")],
            [new("des_i_capa_test", PcVariableFormat.IntegerType, "instance", "256"),
             new("des_i_type_test", PcVariableFormat.IntegerType, "instance", "0"),
             new("des_i_SFlag", PcVariableFormat.IntegerType, "instance", "0")]);
        var caseInsensitiveOverlayOwner = capabilityDetector with
        {
            Variables = [
                new("DES_I_CAPA_TEST", PcVariableFormat.IntegerType, "model", "512"),
                new("des_i_capa_test", PcVariableFormat.IntegerType, "instance", "256")]
        };
        Check(caseInsensitiveOverlayOwner.EffectiveVariable("Des_I_Capa_Test")?.Value == "256",
            "Effective scalar OVA lookup should apply instance overrides without case-sensitive name matching");
        string[] capabilityFindings = CinematicScriptBehaviorAnalyzer.Analyze(capabilityDetector);
        Check(capabilityFindings.Any(finding => finding.Contains("capability mask 0x00000100", StringComparison.Ordinal)
                && finding.Contains("all linked objects must have the mask", StringComparison.Ordinal)
                && finding.Contains("_PJ_Peyj_le_petit_cochon.gao [instance]", StringComparison.Ordinal))
            && capabilityFindings.Any(finding => finding.Contains("effective slot's source", StringComparison.Ordinal)),
            "The capability detector should expose source-backed predicates, array slots and the effective model/instance overlay");
        var boundingVolumeCapabilityOwner = new CinematicScriptOwner(0x0200718C, 0x0200718E, 0x32000064,
            0xD90057F0, uint.MaxValue, uint.MaxValue, "LD_OBJ_gyro_Active_BV_Active_detect.gao",
            ["Detect_o_BV_CapaSet_detect_go"],
            [new("o_perso", 0x71003F5A, "model", "_PJ_Sally.gao"),
             new("o_perso", 0x71003F3E, "instance", "_PJ_Enterprise.gao"),
             new("o_perso_capa", 0x0200718C, "instance", "LD_OBJ_gyro_Active_BV_Active_detect.gao")],
            [new("o_perso", PcVariableFormat.ObjectReferenceType, "model", "0x71003F5A"),
             new("o_perso_capa", PcVariableFormat.ObjectReferenceType, "model", "0x00000000"),
             new("i_capa", PcVariableFormat.IntegerType, "model", "256"),
             new("des_i_test_si_actif", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_met_SF", PcVariableFormat.IntegerType, "model", "0"),
             new("des_capa_a_faux", PcVariableFormat.IntegerType, "model", "0"),
             new("o_perso", PcVariableFormat.ObjectReferenceType, "instance", "0x71003F3E"),
             new("o_perso_capa", PcVariableFormat.ObjectReferenceType, "instance", "0x0200718C"),
             new("des_i_test_si_actif", PcVariableFormat.IntegerType, "instance", "1")]);
        string[] boundingVolumeFindings = CinematicScriptBehaviorAnalyzer.Analyze(boundingVolumeCapabilityOwner);
        var boundingVolumeFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(boundingVolumeCapabilityOwner);
        Check(boundingVolumeFindings.Any(finding => finding.Contains("and '_PJ_Enterprise.gao' (71003F3E)", StringComparison.Ordinal))
            && boundingVolumeFindings.Any(finding => finding.Contains("on 'LD_OBJ_gyro_Active_BV_Active_detect.gao' (0200718C)", StringComparison.Ordinal))
            && boundingVolumeFindings.Any(finding => finding.Contains("sets capability mask 0x00000100", StringComparison.Ordinal))
            && boundingVolumeFindings.Any(finding => finding.Contains("control mask 0x400000", StringComparison.Ordinal))
            && boundingVolumeFindings.Any(finding => finding.Contains("does not define a corresponding exit reset", StringComparison.Ordinal))
            && boundingVolumeFlagAccesses.Length == 0,
            "The BV capability setter should apply instance-over-model object links and report collision-scoped capability effects without inventing exit behavior");
        var flagSettingBoundingVolumeOwner = boundingVolumeCapabilityOwner with
        {
            Variables = [.. boundingVolumeCapabilityOwner.Variables,
                new("des_i_met_SF", PcVariableFormat.IntegerType, "instance", "2162")]
        };
        Check(CinematicScriptFlagFlowAnalyzer.GetAccesses(flagSettingBoundingVolumeOwner)
                .Any(access => access.Flag == 2162 && access.Access == "writes"
                    && access.Description.Contains("BV-pivot collision", StringComparison.Ordinal))
            && CinematicScriptBehaviorAnalyzer.Analyze(flagSettingBoundingVolumeOwner)
                .Any(finding => finding.Contains("sets saved special flag 2162", StringComparison.Ordinal)),
            "BV capability setters should contribute a source-verified saved-flag write to the shared owner graph");
        var nullTargetZone = zoneOwner with
        {
            ObjectReferences = [],
            Variables = [.. zoneOwner.Variables,
                new("des_acteur_detecte1", PcVariableFormat.ObjectReferenceType, "instance", "0x00000000")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(nullTargetZone).Any(finding =>
                finding.Contains("collision predicates return false for a null actor", StringComparison.Ordinal)),
            "Detect_o_Zone should report the source null-target behavior instead of assuming a fallback actor");
        var timedZone = zoneOwner with
        {
            Variables = [.. zoneOwner.Variables,
                new("des_f_minuterie", PcVariableFormat.FloatType, "instance", "2.5")]
        };
        string[] timedZoneFindings = CinematicScriptBehaviorAnalyzer.Analyze(timedZone);
        Check(timedZoneFindings.Any(finding => finding.Contains("toggles saved flag 2794", StringComparison.Ordinal))
            && timedZoneFindings.Any(finding => finding.Contains("clears saved flag 2777", StringComparison.Ordinal)),
            "Timed Detect_o_Zone semantics should distinguish inverted-flag toggles from set/reset reversals");
        var negativeTimerZone = zoneOwner with
        {
            Variables = [.. zoneOwner.Variables.Where(variable => variable.Name != "des_f_minuterie"),
                new("des_f_minuterie", PcVariableFormat.FloatType, "instance", "-0.5")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(negativeTimerZone).Any(finding =>
                finding.Contains("nonzero negative timer -0.5", StringComparison.Ordinal)
                && finding.Contains("already-expired threshold", StringComparison.Ordinal)
                && finding.Contains("clears saved flag 2777", StringComparison.Ordinal)),
            "Detect_o_Zone should follow its source !=0 timer test and TIMEElapsed comparison for negative durations");
        var fourFlagOwner = new CinematicScriptOwner(0xD0002E2C, 0, 0x7100E398, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "WP_detect_dial_SF128.gao",
            ["Detect_o_4SpecialFlags_D4sf_init", "Detect_o_4SpecialFlags_D4sf_wait", "Detect_o_4SpecialFlags_D4sf_go"],
            [],
            [new("SF_1", PcVariableFormat.IntegerType, "model", "0"),
             new("SF_2", PcVariableFormat.IntegerType, "model", "0"),
             new("SF_3", PcVariableFormat.IntegerType, "model", "0"),
             new("SF_4", PcVariableFormat.IntegerType, "model", "0"),
             new("des_i_only_vrai", PcVariableFormat.IntegerType, "model", "0"),
             new("SF_result_a_1", PcVariableFormat.IntegerType, "model", "0"),
             new("SF_result_a_0", PcVariableFormat.IntegerType, "model", "0"),
             new("f_every", PcVariableFormat.FloatType, "model", "2"),
             new("SF_1", PcVariableFormat.IntegerType, "instance", "2667"),
             new("SF_2", PcVariableFormat.IntegerType, "instance", "10120"),
             new("SF_3", PcVariableFormat.IntegerType, "instance", "10930"),
             new("SF_result_a_1", PcVariableFormat.IntegerType, "instance", "128"),
             new("f_every", PcVariableFormat.FloatType, "instance", "-1")]);
        string[] fourFlagFindings = CinematicScriptBehaviorAnalyzer.Analyze(fourFlagOwner);
        Check(fourFlagFindings.Any(finding => finding.Contains("all must pass", StringComparison.Ordinal)
                && finding.Contains("flag 2667 set", StringComparison.Ordinal)
                && finding.Contains("flag 120 clear", StringComparison.Ordinal)
                && finding.Contains("flag 930 clear", StringComparison.Ordinal))
            && fourFlagFindings.Any(finding => finding.Contains("sets saved flag 128", StringComparison.Ordinal)
                && finding.Contains("enable capability 0x100", StringComparison.Ordinal))
            && fourFlagFindings.Any(finding => finding.Contains("D4sf_go reverses configured results", StringComparison.Ordinal))
            && fourFlagFindings.Any(finding => finding.Contains("f_every=-1 selects D4sf_wait", StringComparison.Ordinal)
                && finding.Contains("ao_optim_dialogue", StringComparison.Ordinal)),
            "Detect_o_4SpecialFlags analysis should decode required-set/clear gates, results and wait-mode scheduling from OVA");
        var onlyTrueFourFlagOwner = fourFlagOwner with
        {
            Variables = [.. fourFlagOwner.Variables,
                new("des_i_only_vrai", PcVariableFormat.IntegerType, "instance", "1")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(onlyTrueFourFlagOwner).Any(finding =>
                finding.Contains("des_i_only_vrai is nonzero", StringComparison.Ordinal)
                && finding.Contains("leaves prior results/capability unchanged", StringComparison.Ordinal)),
            "Nonzero Detect_o_4SpecialFlags only-vrai mode should preserve previous state on failed predicates");
        var emptyFourFlagOwner = fourFlagOwner with
        {
            Variables = [.. fourFlagOwner.Variables,
                new("SF_1", PcVariableFormat.IntegerType, "instance", "0"),
                new("SF_2", PcVariableFormat.IntegerType, "instance", "0"),
                new("SF_3", PcVariableFormat.IntegerType, "instance", "0"),
                new("SF_4", PcVariableFormat.IntegerType, "instance", "0")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(emptyFourFlagOwner).Any(finding =>
                finding.Contains("All four special-flag slots are zero", StringComparison.Ordinal)),
            "All-zero Detect_o_4SpecialFlags slots should be reported as an unconditional passing test");
        var superFlagOwner = new CinematicScriptOwner(0xD0002E40, 0, 0x7100C170, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "WP_detect_dial_SF_fixture.gao",
            ["Detect_o_SuperSpecialFlags_ssf_init", "Detect_o_SuperSpecialFlags_ssf_ET_test",
             "Detect_o_SuperSpecialFlags_ssf_OU_test", "Detect_o_SuperSpecialFlags_ssf_ET_dialogue",
             "Detect_o_SuperSpecialFlags_ssf_OU_dialogue"], [],
            [new("Mode_ET", PcVariableFormat.IntegerType, "model", "0"),
             new("Mode_ET", PcVariableFormat.IntegerType, "instance", "1"),
             new("SF_Et_1", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("Et_1_etatSF", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("SF_Et_2", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("Et_2_etatSF", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("SF_Et_3", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("Et_3_etatSF", PcVariableFormat.IntegerType, "model", "[0, 0, 0, 0, 0] (5 entries)", [0, 0, 0, 0, 0], 5),
             new("SF_Et_1", PcVariableFormat.IntegerType, "instance", "[2054, 2109, 0, 0, 0] (5 entries)", [2054, 2109, 0, 0, 0], 5),
             new("Et_1_etatSF", PcVariableFormat.IntegerType, "instance", "[1, 0, 0, 0, 0] (5 entries)", [1, 0, 0, 0, 0], 5),
             new("SF_result_a_1", PcVariableFormat.IntegerType, "instance", "202"),
             new("f_every", PcVariableFormat.FloatType, "instance", "-1")]);
        string[] superFlagFindings = CinematicScriptBehaviorAnalyzer.Analyze(superFlagOwner);
        var superFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(superFlagOwner);
        Check(superFlagFindings.Any(finding => finding.Contains("Mode_ET=1", StringComparison.Ordinal)
                && finding.Contains("AND", StringComparison.Ordinal)
                && finding.Contains("any one passing group is sufficient", StringComparison.Ordinal)
                && finding.Contains("flag 2054 set", StringComparison.Ordinal)
                && finding.Contains("flag 2109 clear", StringComparison.Ordinal))
            && superFlagFindings.Any(finding => finding.Contains("optimized dialogue path", StringComparison.Ordinal)
                && finding.Contains("ao_optim_dialogue", StringComparison.Ordinal))
            && superFlagFindings.Any(finding => finding.Contains("Dialogue callbacks apply only the success branch", StringComparison.Ordinal))
            && superFlagAccesses.Any(access => access.Flag == 2054 && access.Access == "reads"
                && access.Description.Contains("requires set", StringComparison.Ordinal))
            && superFlagAccesses.Any(access => access.Flag == 2109 && access.Access == "reads"
                && access.Description.Contains("requires clear", StringComparison.Ordinal))
            && superFlagAccesses.Any(access => access.Flag == 202 && access.Access == "writes"),
            $"Super-special flag analysis mismatch: findings='{string.Join(" | ", superFlagFindings)}'; accesses='{string.Join(" | ", superFlagAccesses.Select(access => $"{access.Flag}:{access.Access}:{access.Description}"))}'");
        var superFlagFirstSlotZero = superFlagOwner with
        {
            Variables = [.. superFlagOwner.Variables,
                new("SF_Et_1", PcVariableFormat.IntegerType, "instance", "[0, 2054, 0, 0, 0] (5 entries)", [0, 2054, 0, 0, 0], 5)]
        };
        var firstSlotZeroFindings = CinematicScriptBehaviorAnalyzer.Analyze(superFlagFirstSlotZero);
        var firstSlotZeroAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(superFlagFirstSlotZero);
        Check(firstSlotZeroFindings.Any(finding => finding.Contains("group 1 skipped because slot 0 is zero", StringComparison.Ordinal)
                && finding.Contains("ET mode treats the group as failing", StringComparison.Ordinal))
            && !firstSlotZeroAccesses.Any(access => access.Flag == 2054 && access.Access == "reads"),
            "A nonzero later slot must not activate or emit a flag read for a source group whose slot 0 is zero");
        var superFlagFirstSlotZeroOu = superFlagFirstSlotZero with
        {
            Variables = [.. superFlagFirstSlotZero.Variables,
                new("Mode_ET", PcVariableFormat.IntegerType, "instance", "0")]
        };
        Check(CinematicScriptBehaviorAnalyzer.Analyze(superFlagFirstSlotZeroOu).Any(finding =>
                finding.Contains("group 1 skipped because slot 0 is zero", StringComparison.Ordinal)
                && finding.Contains("OU mode treats the group as passing", StringComparison.Ordinal)),
            "An inactive source group must pass the OU branch when its slot-0 activation gate is zero");
        var superFlagModeOu = superFlagOwner with
        {
            Variables = [.. superFlagOwner.Variables,
                new("Mode_ET", PcVariableFormat.IntegerType, "instance", "0"),
                new("f_every", PcVariableFormat.FloatType, "instance", "0.5")]
        };
        string[] superFlagOuFindings = CinematicScriptBehaviorAnalyzer.Analyze(superFlagModeOu);
        Check(superFlagOuFindings.Any(finding => finding.Contains("Mode_ET=0", StringComparison.Ordinal)
                && finding.Contains("OR", StringComparison.Ordinal)
                && finding.Contains("every group must pass", StringComparison.Ordinal))
            && superFlagOuFindings.Any(finding => finding.Contains("polling path", StringComparison.Ordinal)
                && finding.Contains("0.5", StringComparison.Ordinal))
            && superFlagOuFindings.Any(finding => finding.Contains("Polling test callbacks reverse", StringComparison.Ordinal)),
            "OU-mode super-special detectors should expose OR-within/AND-across groups and distinguish polling failure resets");
        var superFlagWriter = new CinematicScriptOwner(0xD0002E41, 0, 0x350095C0, 0xD0002E24,
            uint.MaxValue, uint.MaxValue, "trigger setting SF 2054", ["Detect_o_Zone_doz_init"], [],
            [new("des_i_SPFlag_set01", PcVariableFormat.IntegerType, "instance", "2054")]);
        var superFlagLinks = CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(superFlagOwner,
            [superFlagOwner, superFlagWriter]);
        var superFlagGate = CinematicTimelineFlagAnalyzer.Analyze(superFlagOwner,
            [(1.0f, new CinematicLiteralCall(superFlagOwner.OwnerKey, 0x2916, [2054]))], 2.0f)
            .Single(gate => gate.Flag == 2054);
        Check(superFlagLinks.Any(link => link.RelatedOwner.OwnerKey == superFlagWriter.OwnerKey
                && link.Flag == 2054)
            && superFlagGate is { RequiredSet: true, CurrentSet: true, Satisfied: true },
            "Super-special predicates should join trigger writers and expose selected-track saved-flag state at the playhead");
        Console.WriteLine("PASS: script-owner resources, track callbacks, engine-hook callbacks and source-mapped function catalogs");
    }

    private static byte[] CreateObjectReferenceVariables(string name, uint key)
        => CreateScalarVariables(name, PcVariableFormat.ObjectReferenceType, BitConverter.GetBytes(key));

    private static byte[] CreateObjectReferenceArrayVariables(string name, uint[] keys)
    {
        int descriptorLength = PcVariableFormat.DescriptorSize;
        int namesLength = PcVariableFormat.NameSize;
        int storageLength = sizeof(uint) + keys.Length * sizeof(uint);
        byte[] bytes = new byte[PcVariableFormat.MinimumPayloadSize + descriptorLength + namesLength + storageLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, descriptorLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + sizeof(uint)),
            0x40000000u | (uint)keys.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + sizeof(uint) * 2),
            PcVariableFormat.ObjectReferenceType);
        int namesStart = PcVariableFormat.BlockLengthSize + descriptorLength + PcVariableFormat.BlockLengthSize;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + descriptorLength), namesLength);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes.AsSpan(namesStart, PcVariableFormat.NameSize));
        int storageHeader = namesStart + namesLength;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(storageHeader), storageLength);
        int storageStart = storageHeader + PcVariableFormat.BlockLengthSize;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(storageStart), (uint)keys.Length);
        for (int index = 0; index < keys.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(storageStart + sizeof(uint) * (index + 1)), keys[index]);
        return bytes;
    }

    private static byte[] CreateIntegerArrayVariables(string name, int[] values)
    {
        int descriptorLength = PcVariableFormat.DescriptorSize;
        int namesLength = PcVariableFormat.NameSize;
        int storageLength = sizeof(uint) + values.Length * sizeof(uint);
        byte[] bytes = new byte[PcVariableFormat.MinimumPayloadSize + descriptorLength + namesLength + storageLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, descriptorLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + sizeof(uint)),
            0x40000000u | (uint)values.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + sizeof(uint) * 2),
            PcVariableFormat.IntegerType);
        int namesStart = PcVariableFormat.BlockLengthSize + descriptorLength + PcVariableFormat.BlockLengthSize;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + descriptorLength), namesLength);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes.AsSpan(namesStart, PcVariableFormat.NameSize));
        int storageHeader = namesStart + namesLength;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(storageHeader), storageLength);
        int storageStart = storageHeader + PcVariableFormat.BlockLengthSize;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(storageStart), (uint)values.Length);
        for (int index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(storageStart + sizeof(uint) * (index + 1)), values[index]);
        return bytes;
    }

    private static byte[] CreateScalarVariables(string name, ushort type, byte[] value)
    {
        byte[] bytes = new byte[PcVariableFormat.MinimumPayloadSize + PcVariableFormat.DescriptorSize
            + PcVariableFormat.NameSize + value.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, PcVariableFormat.DescriptorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), PcVariableFormat.ScalarLength);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), PcVariableFormat.NameSize);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes.AsSpan(20, PcVariableFormat.NameSize));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(50), value.Length);
        value.CopyTo(bytes.AsSpan(54));
        return bytes;
    }

    private static byte[] CreateTextReferenceArray(string name, (uint Bank, int Id)[] entries)
    {
        int dataLength = entries.Length * sizeof(uint) * 2;
        byte[] bytes = new byte[PcVariableFormat.MinimumPayloadSize + PcVariableFormat.DescriptorSize
            + PcVariableFormat.NameSize + sizeof(uint) + dataLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, PcVariableFormat.DescriptorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)entries.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 44);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), PcVariableFormat.NameSize);
        Encoding.Latin1.GetBytes(name).CopyTo(bytes.AsSpan(20, PcVariableFormat.NameSize));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(50), sizeof(uint) + dataLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(54), 0);
        for (int index = 0; index < entries.Length; index++)
        {
            int offset = 54 + sizeof(uint) + index * sizeof(uint) * 2;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), entries[index].Bank);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + sizeof(uint)), entries[index].Id);
        }
        return bytes;
    }

    private static byte[] CreateDialogueArrays(params (string Name, ushort Type, uint Count, uint[] Values)[] arrays)
    {
        int descriptorLength = arrays.Length * PcVariableFormat.DescriptorSize;
        int namesLength = arrays.Length * PcVariableFormat.NameSize;
        int storageLength = arrays.Sum(array => sizeof(uint) + array.Values.Length * sizeof(uint));
        byte[] bytes = new byte[PcVariableFormat.MinimumPayloadSize + descriptorLength + namesLength + storageLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, descriptorLength);
        int namesStart = PcVariableFormat.BlockLengthSize + descriptorLength + PcVariableFormat.BlockLengthSize;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(PcVariableFormat.BlockLengthSize + descriptorLength), namesLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(namesStart + namesLength), storageLength);
        int storageStart = namesStart + namesLength + PcVariableFormat.BlockLengthSize;
        int storageOffset = 0;
        for (int arrayIndex = 0; arrayIndex < arrays.Length; arrayIndex++)
        {
            var array = arrays[arrayIndex];
            int descriptorOffset = PcVariableFormat.BlockLengthSize + arrayIndex * PcVariableFormat.DescriptorSize;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(descriptorOffset), (uint)storageOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(descriptorOffset + sizeof(uint)), array.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(descriptorOffset + sizeof(uint) * 2), array.Type);
            Encoding.Latin1.GetBytes(array.Name).CopyTo(bytes.AsSpan(namesStart + arrayIndex * PcVariableFormat.NameSize,
                PcVariableFormat.NameSize));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(storageStart + storageOffset), array.Count);
            int valuesStart = storageStart + storageOffset + sizeof(uint);
            for (int valueIndex = 0; valueIndex < array.Values.Length; valueIndex++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(valuesStart + valueIndex * sizeof(uint)),
                    array.Values[valueIndex]);
            storageOffset += sizeof(uint) + array.Values.Length * sizeof(uint);
        }
        return bytes;
    }

    private static AnimationOpaquePayload CreateCallPayload(uint selector,
        params (uint Value, uint Tag)[] arguments)
    {
        var rows = new List<(uint Value, uint Tag)>
        {
            (0, 0x01000028),
            (0, 0x02000806)
        };
        rows.AddRange(arguments);
        rows.Add((0, 0x03000000 | selector));
        rows.Add((0, 0x1F000000));
        byte[] payload = new byte[sizeof(uint) + rows.Count * sizeof(uint) * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)rows.Count);
        for (int index = 0; index < rows.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(sizeof(uint) + index * 8), rows[index].Value);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(sizeof(uint) + index * 8 + sizeof(uint)), rows[index].Tag);
        }
        return new("word-pair-list", 0, payload);
    }

    private static AnimationOpaquePayload CreateTypedMessagePayload(int messageType, int int1, int int2,
        uint gao, uint gao2)
    {
        var rows = new (uint Value, uint Tag)[]
        {
            (0, 0x01000028),
            (0, 0x02000806),
            (unchecked((uint)messageType), 0x01000021),
            (gao, 0x01000028),
            (gao2, 0x01000028),
            (0, 0x01000022),
            (0, 0x01000022),
            (0, 0x01000022),
            (0, 0x03001770),
            (0, 0x01000022),
            (0, 0x01000022),
            (0, 0x01000022),
            (0, 0x03001770),
            (0, 0x01000022),
            (0, 0x01000022),
            (unchecked((uint)int1), 0x01000021),
            (unchecked((uint)int2), 0x01000021),
            (0, 0x03001F78),
            (0, 0x1F000000)
        };
        byte[] payload = new byte[sizeof(uint) + rows.Length * sizeof(uint) * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)rows.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            int offset = sizeof(uint) + index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), rows[index].Value);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + sizeof(uint)), rows[index].Tag);
        }
        return new("word-pair-list", 0, payload);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
