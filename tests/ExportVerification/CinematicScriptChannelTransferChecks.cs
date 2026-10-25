using JadeView;

internal static class CinematicScriptChannelTransferChecks
{
    internal static void Verify()
    {
        var owner = new CinematicScriptOwner(0x1000, 0x2000, 0x3000, 0x4000,
            0x5000, 0x6000, "controller", [],
            [new("linkedMorph", 0x7000, "model", "linked target")], []);
        var gate = new CinematicScriptBranchConditionSource("if", "owner->active", 90);
        var getter = new CinematicScriptReachableEngineCall("update", "OBJ_GetChannelProg", "C",
            ["AI_gpst_CurrentGameObject", "2"], [], [], [], [gate], "script.c", 100, "init -> update");
        var setter = new CinematicScriptReachableEngineCall("update", "OBJ_SetChannelProg", "C",
            ["targetAlias", "2", "progress"],
            [new("progress", "AI_EvalFunc_OBJ_GetChannelProg_C(AI_gpst_CurrentGameObject, 2)", 100, 0),
             new("targetAlias", "owner->linkedMorph", 101, 0)],
            [new("linkedMorph", "targetAlias", 101)], [], [gate], "script.c", 102,
            "init -> update");
        var clearInvisible = new CinematicScriptReachableEngineCall("update", "OBJ_FlagInvisibleSet", "C",
            ["targetAlias", "0"], [new("targetAlias", "owner->linkedMorph", 101, 0)],
            [new("linkedMorph", "targetAlias", 101)], [], [gate], "script.c", 103,
            "init -> update");

        CinematicScriptChannelTransfer[] transfers = CinematicScriptChannelTransferAnalyzer.Analyze(
            owner, [getter, setter, clearInvisible], []);
        Check(transfers.Length == 1, "A unique getter-to-linked-setter channel handoff was not found.");
        Check(transfers[0] is
            {
                OwnerKey: 0x1000,
                TargetKey: 0x7000,
                TargetName: "linked target",
                OvaField: "linkedMorph",
                SourceChannel: 2,
                TargetChannel: 2,
                GetterLine: 100,
                SetterLine: 102,
                SourcePathResolved: true,
                ClearsInvisibleFlag: true,
                InvisibleFlagLine: 103
            } && transfers[0].BranchConditions.SequenceEqual([gate]),
            "A channel handoff lost its OVA target, source provenance, guard, or paired visibility change.");
        VerifyTransferDiagnostics();

        var mismatchedGate = new CinematicScriptBranchConditionSource("if", "different_state", 91);
        var mismatchedGetter = getter with { BranchConditions = [mismatchedGate] };
        Check(CinematicScriptChannelTransferAnalyzer.Analyze(owner,
                [mismatchedGetter, setter, clearInvisible], []).Length == 0,
            "The resolver joined a getter and setter from incompatible source branches.");

        var ambiguousOwner = owner with
        {
            ObjectReferences =
            [
                new("linkedMorph", 0x7000, "model", "first target"),
                new("linkedMorph", 0x8000, "instance", "second target")
            ]
        };
        CinematicScriptChannelTransfer[] overridden = CinematicScriptChannelTransferAnalyzer.Analyze(
            ambiguousOwner, [getter, setter, clearInvisible], []);
        Check(overridden.Length == 1 && overridden[0].TargetKey == 0x8000
            && overridden[0].TargetName == "second target",
            "The resolver did not apply the instance OVA reference over the model default.");

        var unresolvedPath = setter with
        {
            CallbackPath = "configured model function; source callback path unresolved"
        };
        CinematicScriptChannelTransfer[] unresolved = CinematicScriptChannelTransferAnalyzer.Analyze(
            owner, [getter, unresolvedPath, clearInvisible], []);
        Check(unresolved.Length == 1 && !unresolved[0].SourcePathResolved,
            "Model membership without a recovered callback path was incorrectly presented as reachable.");

        VerifyLighthouseCallbackPathAndTransfer();

        Console.WriteLine("PASS: linked morph-channel handoffs preserve dataflow, callback gates, and effective instance OVA references");
    }

    private static void VerifyTransferDiagnostics()
    {
        var channelWithoutGroups = new FaceMorphData([], [new(0, [], [])],
            [new(BitConverter.SingleToUInt32Bits(1), BitConverter.SingleToUInt32Bits(1), [])]);
        string noDeformation = CinematicScriptCallbackPlayback.DescribeTransfer(
            "controller", "linked dome", 0, 0, channelWithoutGroups);
        Check(noDeformation.Contains("no valid morph-group references", StringComparison.Ordinal),
            "A channel handoff with no target group references was not identified as non-deforming.");

        var channelWithGroups = channelWithoutGroups with
        {
            Channels = [channelWithoutGroups.Channels[0] with { GroupReferences = [0] }]
        };
        string hasDeformationPath = CinematicScriptCallbackPlayback.DescribeTransfer(
            "controller", "linked mesh", 0, 0, channelWithGroups);
        Check(hasDeformationPath.Contains("has valid morph-group references", StringComparison.Ordinal),
            "A channel handoff with target group references was incorrectly marked non-deforming.");

        var invalidReferences = channelWithGroups with
        {
            Channels = [channelWithGroups.Channels[0] with { GroupReferences = [1] }]
        };
        Check(!CinematicScriptCallbackPlayback.HasValidMorphGroupReference(invalidReferences, 0)
            && !CinematicScriptCallbackPlayback.HasValidMorphGroupReference(channelWithGroups, 1),
            "Invalid group indices or channel indices were treated as a deformation path.");
    }

    private static void VerifyLighthouseCallbackPathAndTransfer()
    {
        const string firstInit = "sfx_bouclier_phare_sbp_firstinit";
        const string init = "sfx_bouclier_phare_sbp_init";
        const string firstAppearance = "sfx_bouclier_phare_sbp_apparition0";
        const string appearance = "sfx_bouclier_phare_sbp_apparition";
        const string secondAppearance = "sfx_bouclier_phare_sbp_apparition1";
        const string domeAppearance = "sfx_bouclier_phare_abp_apparition2";
        string[] modelFunctions = [firstInit, init, firstAppearance, appearance,
            secondAppearance, domeAppearance, "sfx_bouclier_phare_sbp_exec_pos"];
        var owner = new CinematicScriptOwner(0x10004AE8, 0, 0, 0xFF0045A2,
            0, 0, "shield controller", [firstInit],
            [new("des_o_dome", 0x3D002D7D, "model", "SFX_Dome_Phare@5ea4c30.gao")], [])
        {
            ModelFunctionNames = modelFunctions
        };

        CinematicScriptFunctionPath path = CinematicScriptReader.NestedCallbackPaths(owner)
            .Single(candidate => candidate.FunctionName == domeAppearance);
        Check(path.Path.Contains($"{firstAppearance}", StringComparison.Ordinal)
            && path.Path.Contains($"{appearance}", StringComparison.Ordinal)
            && path.Path.Contains($"{secondAppearance}", StringComparison.Ordinal),
            "The source call graph did not retain the full sequential track-replacement path to the dome callback.");
        Check(path.Steps.Length == 5 && path.Steps.All(step => step.DispatchConditionsResolved)
            && path.Steps.Any(step => step.FromFunction == init
                && step.BranchConditions.Any(branch => branch.Expression.Contains("2452", StringComparison.Ordinal)))
            && path.Steps.Any(step => step.FromFunction == appearance
                && step.BranchConditions.Any(branch => branch.Expression.Contains("CapaTest_C_CURRENT(1)", StringComparison.Ordinal))),
            "The callback route did not retain its source dispatch gates for save state and capability transitions.");

        CinematicScriptChannelTransfer transfer = CinematicScriptChannelTransferAnalyzer.Analyze(owner)
            .Single(candidate => candidate.FunctionName == domeAppearance
                && candidate.SetterLine == 8160);
        Check(transfer.TargetKey == 0x3D002D7D && transfer.SourceChannel == 0
            && transfer.TargetChannel == 0 && transfer.SourcePathResolved
            && transfer.ClearsInvisibleFlag && transfer.CallbackPathSteps.Length == path.Steps.Length
            && transfer.ReceiverGuardAliases.Contains("v7", StringComparer.Ordinal),
            "The lighthouse source graph did not connect the reachable channel handoff to its linked dome.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
