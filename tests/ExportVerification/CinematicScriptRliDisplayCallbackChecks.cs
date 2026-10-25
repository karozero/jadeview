using JadeView;

internal static class CinematicScriptRliDisplayCallbackChecks
{
    internal static void Verify()
    {
        const uint targetKey = 0xF0000101;
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "RLI callback fixture", [], [], [])
        {
            ObjectReferences = [new("des_o_dome", targetKey, "instance", "linked target")]
        };
        var calls = new[]
        {
            Call("Rli_init", "AICBAdd", ["AI_gpst_CurrentGameObject", "destination",
                "0", "fct_Rli_init_sfx_bouclier_phare_sbp_bdisplay"], 10,
                [new("destination", "(tGameObjectMain*)v1->des_o_dome", 9, 0)]),
            Call("Rli_init", "AICBAdd", ["AI_gpst_CurrentGameObject", "destination",
                "1", "fct_Rli_init_sfx_bouclier_phare_sbp_adisplay"], 11,
                [new("destination", "(tGameObjectMain*)v1->des_o_dome", 9, 0)]),
            Call("sfx_bouclier_phare_sbp_bdisplay", "LightRLIFactorUsed", ["1"], 20),
            Call("sfx_bouclier_phare_sbp_bdisplay", "LightSetRLIFactor", ["0.4f"], 21),
            Call("sfx_bouclier_phare_sbp_bdisplay", "LightSetRLIDestColor", ["-1"], 22),
            Call("sfx_bouclier_phare_sbp_adisplay", "LightRLIFactorUsed", ["0"], 30),
            Call("sfx_bouclier_phare_sbp_adisplay", "LightSetRLIFactor", ["old_factor"], 31),
            Call("sfx_bouclier_phare_sbp_adisplay", "LightSetRLIDestColor", ["old_color"], 32)
        };
        var references = new[]
        {
            Reference("Function", "sfx_bouclier_phare_sbp_bdisplay", 0x3D002D82),
            Reference("Function", "sfx_bouclier_phare_sbp_adisplay", 0x3D002D83),
        };

        CinematicScriptRliDisplayCallbackCandidate[] candidates =
            CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner, calls, references);
        Check(candidates.Length == 2,
            "Both registered display phases should be discovered from a uniquely resolved callback tuple.");
        var before = candidates.Single(candidate => candidate.CallbackType == 0);
        Check(before.ContextKey == owner.OwnerKey && before.TargetKey == targetKey
            && before.FunctionName == "sfx_bouclier_phare_sbp_bdisplay"
            && before.FactorExpression == "0.4f"
            && before.DestinationColorExpression == "-1"
            && before.FactorIsUsed == true && before.HasPairedCallback,
            "Before Display analysis should preserve its per-object target and source RLI setters.");
        var after = candidates.Single(candidate => candidate.CallbackType == 1);
        Check(after.FunctionName == "sfx_bouclier_phare_sbp_adisplay" && after.FactorIsUsed == false
            && after.HasPairedCallback,
            "After Display analysis should retain the paired restoration callback without executing it.");

        bool literalOverrideResolved = CinematicRliDisplayCallbackResolver
            .TryResolvePairedLiteralOverride(candidates, out CinematicRliDrawState literalOverride);
        Check(literalOverrideResolved && literalOverride.FactorIsUsed
            && Math.Abs(literalOverride.Factor - .4f) < .0001f
            && literalOverride.DestinationColor == System.Numerics.Vector4.One,
            "A uniquely paired, unconditional literal draw hook should resolve to a per-object RLI override.");
        CinematicScriptRliDisplayCallbackCandidate[] packedColorCallbacks =
        [before with { DestinationColorExpression = "0x80402010" }, after];
        Check(CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                packedColorCallbacks, out CinematicRliDrawState packedColorOverride)
            && Math.Abs(packedColorOverride.DestinationColor.X - 16 / 255f) < .0001f
            && Math.Abs(packedColorOverride.DestinationColor.Y - 32 / 255f) < .0001f
            && Math.Abs(packedColorOverride.DestinationColor.Z - 64 / 255f) < .0001f
            && Math.Abs(packedColorOverride.DestinationColor.W - 128 / 255f) < .0001f,
            "Packed RLI colors must preserve Jade's low-byte RGBA order, including alpha.");

        CinematicScriptRliDisplayCallbackCandidate dynamicBefore = before with
        {
            FactorExpression = "v1->f_transparency_coef"
        };
        Check(!CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                [dynamicBefore, after], out _),
            "Dynamic OVA fields must remain unresolved until their callback state is modeled.");
        Check(!CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                [before with { BranchConditions = ["saved flag gate"] }, after], out _),
            "Conditional hook registration must not be assumed without an explicit runtime gate resolution.");
        CinematicScriptReachableEngineCall[] conditionallySetRli = calls.Select(call =>
            call.FunctionName == "sfx_bouclier_phare_sbp_bdisplay"
                && call.Api == "LightSetRLIFactor"
                    ? call with
                    {
                        BranchConditions = [new("if", "runtime condition", 21)]
                    }
                    : call).ToArray();
        CinematicScriptRliDisplayCallbackCandidate conditionalEffect =
            CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner, conditionallySetRli, references)
                .Single(candidate => candidate.CallbackType == 0);
        Check(conditionalEffect.EffectBranchConditions.Length == 1
            && !CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                [conditionalEffect, after], out _),
            "Conditional RLI setters must remain unresolved even when their registration is unconditional.");
        Check(!CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                [before with { HasMatchingRemovalSite = true }, after], out _),
            "Hooks with a source-matched removal site must not be treated as permanently registered.");
        Check(!CinematicRliDisplayCallbackResolver.TryResolvePairedLiteralOverride(
                [before with { CallbackPath = "source callback path unresolved" }, after], out _),
            "Registration without a resolved source path must not become an executable draw hook.");

        const string appearanceFunction = "shield_appearance";
        const string factorField = "f_transparency_coef";
        var dynamicOwner = owner with
        {
            Variables = [new(factorField, 0, "instance", "0")]
        };
        CinematicScriptReachableEngineCall[] dynamicCalls =
        [
            Call("Rli_init", "AICBAdd", ["AI_gpst_CurrentGameObject", "destination",
                "0", "callback"], 10),
            new(appearanceFunction, "OBJ_GetChannelProg", "C",
                ["AI_gpst_CurrentGameObject", "0"], [], [], [], [],
                "rli_fixture.c", 30, appearanceFunction, ResultVariable: "progression")
        ];
        CinematicScriptRliValueFlowAssignment[] dynamicAssignments =
        [
            new(appearanceFunction, "progression",
                "AI_EvalFunc_OBJ_GetChannelProg_C(AI_gpst_CurrentGameObject, 0)",
                [], "rli_fixture.c", 30, "init → appearance"),
            new(appearanceFunction, factorField, "1.58f - 2.0f * progression",
                [], "rli_fixture.c", 31, "init → appearance")
        ];
        var unboundedAppearancePath = new CinematicScriptFunctionPath(
            appearanceFunction, "init → appearance", []);
        var dynamicCallbacks = new[]
        {
            before with
            {
                Source = "rli_fixture.c",
                Line = 10,
                FactorExpression = "v1->f_transparency_coef"
            },
            after
        };
        bool scalarFieldResolved = CinematicScriptScalarValueFlowEvaluator.TryEvaluateField(
            dynamicOwner, new SceneObject { GaoKey = dynamicOwner.OwnerKey }, appearanceFunction,
            factorField, dynamicAssignments, dynamicCalls, .5f, [], _ => .75f, null,
            out float scalarFieldValue);
        Check(scalarFieldResolved && Math.Abs(scalarFieldValue - .08f) < .0001f,
            $"The dynamic field fixture should evaluate before it is routed to a display callback (resolved={scalarFieldResolved}, value={scalarFieldValue}).");
        var dynamicOwnerObject = new SceneObject { GaoKey = dynamicOwner.OwnerKey };
        Check(CinematicRliDisplayCallbackResolver.IsRegistrationActive(dynamicOwner,
                dynamicOwnerObject, dynamicCallbacks[0], dynamicCalls, [], null),
            "The literal AICBAdd registration should be active in the dynamic callback fixture.");
        Check(CinematicRliDisplayCallbackResolver.TryEvaluateLatestField(dynamicOwner,
                dynamicOwnerObject, factorField, dynamicAssignments, dynamicCalls,
                new Dictionary<string, CinematicScriptFunctionPath>(StringComparer.Ordinal)
                {
                    [appearanceFunction] = unboundedAppearancePath
                }, [], null, .5f, (_, _) => .75f, out float callbackFieldValue, out _)
            && Math.Abs(callbackFieldValue - .08f) < .0001f,
            $"The active callback should supply the dynamic OVA field (resolved value={callbackFieldValue}).");

        var entryTimeOwner = dynamicOwner with
        {
            Variables = [new(factorField, 0, "instance", "0"),
                new("f_time", 0, "instance", "0"),
                new("des_f_time1", 0, "instance", "0.5")]
        };
        const int capabilitySetSelector = 0x1FC5;
        const int trackChangeLine = 30;
        var entryGate = new CinematicScriptCallbackPathStep("init", appearanceFunction,
            "replace-current-track", 0, null, trackChangeLine, "nextFunction", null,
            [new("if", "AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x1)", 20)], true);
        var entryPath = new CinematicScriptFunctionPath(appearanceFunction,
            $"init → {appearanceFunction}", [entryGate]);
        var capabilityCall = new CinematicLiteralCall(entryTimeOwner.OwnerKey,
            capabilitySetSelector, [1, 0]);
        CinematicScriptReachableEngineCall[] entryTimeCalls =
        [
            Call("init", "AITrackCurChangeNow", ["nextFunction"], trackChangeLine,
                [new("nextFunction", "fct_shield_appearance", trackChangeLine - 1, 0)])
        ];
        CinematicScriptReachableOvaFieldAccess[] entryTimeAccesses =
        [
            new("f_time", "read", "v1->f_time", "v1", "v1->f_time", [],
                "rli_fixture.c", 40, appearanceFunction, entryPath.Path),
            new("f_time", "write", "v1->f_time = TIM_gf_MainClock - TIM_gf_LockTime",
                "v1", "v1->f_time = TIM_gf_MainClock - TIM_gf_LockTime", [], "rli_fixture.c", trackChangeLine - 2,
                "init", "init")
        ];
        CinematicScriptRliValueFlowAssignment[] entryTimeAssignments =
        [new(appearanceFunction, factorField,
            "2.0f * (TIM_gf_MainClock - f_time)", [], "rli_fixture.c", 41, entryPath.Path)];
        Check(CinematicRliDisplayCallbackResolver.TryEvaluateLatestField(entryTimeOwner,
                new SceneObject { GaoKey = entryTimeOwner.OwnerKey }, factorField,
                entryTimeAssignments, entryTimeCalls,
                new Dictionary<string, CinematicScriptFunctionPath>(StringComparer.Ordinal)
                {
                    [appearanceFunction] = entryPath
                }, [(0.5f, capabilityCall)], null, .75f,
                (_, _) => null, out float entryTimeFactor, out _, entryTimeAccesses)
            && Math.Abs(entryTimeFactor - .5f) < .0001f,
            $"A callback factor should use the elapsed-clock field set by its uniquely resolved track transition (value={entryTimeFactor}).");

        bool dynamicOverrideResolved = CinematicRliDisplayCallbackResolver.TryResolvePairedOverride(
            dynamicOwner, new SceneObject { GaoKey = dynamicOwner.OwnerKey }, dynamicCallbacks,
            dynamicCalls, dynamicAssignments,
            new Dictionary<string, CinematicScriptFunctionPath>(StringComparer.Ordinal)
            {
                [appearanceFunction] = unboundedAppearancePath
            }, [], null, .5f, (_, _) => .75f, out CinematicRliDrawState dynamicOverride);
        Check(dynamicOverrideResolved && Math.Abs(dynamicOverride.Factor - .08f) < .0001f
            && dynamicOverride.FactorIsUsed
            && dynamicOverride.DestinationColor == System.Numerics.Vector4.One,
            $"A paired draw hook should resolve a dynamic OVA factor through a uniquely active callback and current morph channel state (resolved={dynamicOverrideResolved}, factor={dynamicOverride.Factor}).");

        var persistentOwner = dynamicOwner with
        {
            Variables = [new(factorField, 0, "instance", "0"), new("i_cine", 1, "instance", "1")]
        };
        CinematicScriptRliValueFlowAssignment[] firstCallbackAssignments =
        [new("appearance", factorField, "0.37f", [], "rli_fixture.c", 40, "init → appearance")];
        CinematicScriptRliValueFlowAssignment[] laterCallbackAssignments =
        [new("disappearance", factorField, "0.91f",
            [new("if", "i_cine == 0", 50)], "rli_fixture.c", 51, "appearance → disappearance")];
        Check(CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(persistentOwner,
                new SceneObject { GaoKey = persistentOwner.OwnerKey }, "appearance",
                firstCallbackAssignments, [], .5f, [], _ => null, null, null,
                out Dictionary<string, double?> appearanceState)
            && CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(persistentOwner,
                new SceneObject { GaoKey = persistentOwner.OwnerKey }, "disappearance",
                laterCallbackAssignments, [], .75f, [], _ => null, null, appearanceState,
                out Dictionary<string, double?> disappearanceState)
            && disappearanceState.GetValueOrDefault(factorField) is { } persistentFactor
            && Math.Abs(persistentFactor - .37) < .0001,
            "A later callback with a false write guard must preserve the OVA value written by the previous callback.");

        CinematicScriptReachableEngineCall[] unknownRegistrationCalls = dynamicCalls.Select(call =>
            call.Api == "AICBAdd"
                ? call with { BranchConditions = [new("if", "unknown runtime gate", 10)] }
                : call).ToArray();
        Check(!CinematicRliDisplayCallbackResolver.TryResolvePairedOverride(dynamicOwner,
                new SceneObject { GaoKey = dynamicOwner.OwnerKey }, dynamicCallbacks,
                unknownRegistrationCalls, dynamicAssignments,
                new Dictionary<string, CinematicScriptFunctionPath>(StringComparer.Ordinal)
                {
                    [appearanceFunction] = unboundedAppearancePath
                }, [], null, .5f, (_, _) => .75f, out _),
            "A dynamic draw hook with an unresolved registration gate must stay unapplied.");

        CinematicScriptRliDisplayCallbackCandidate[] unresolved =
            CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner,
                [Call("Rli_init", "AICBAdd", ["context", "target", "phase", "callback"], 50)], []);
        Check(unresolved.Length == 0,
            "Unresolved callback IDs and function pointers must not be presented as executable RLI hooks.");

        Console.WriteLine("PASS: RLI display-hook registration joins preserve per-object targets and unresolved callbacks");
    }

    private static CinematicScriptReachableEngineCall Call(string function, string api,
        string[] arguments, int line,
        CinematicScriptArgumentAssignmentSource[]? assignments = null) => new(function, api,
            "C", arguments, assignments ?? [], [], [], [], "rli_fixture.c", line, function);

    private static CinematicScriptReachableSourceReference Reference(string kind, string symbol,
        uint key) => new("Rli_init", kind, symbol, key, "rli_fixture.c", 1, "Rli_init");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
