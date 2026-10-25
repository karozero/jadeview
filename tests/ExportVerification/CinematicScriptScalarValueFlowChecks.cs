using JadeView;

internal static class CinematicScriptScalarValueFlowChecks
{
    internal static void Verify()
    {
        VerifyPackedColorBlend();

        const string functionName = "shield_appearance";
        const string cineCondition = "controller->i_cine";
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0, 0, "scalar-flow fixture", [], [],
            [new("i_cine", 0, "instance", "1")]);
        var ownerObject = new SceneObject { GaoKey = owner.OwnerKey };
        CinematicScriptBranchConditionSource cineBranch = new("if", cineCondition, 10);
        CinematicScriptBranchConditionSource gameplayBranch = new("else", cineCondition, 10);
        CinematicScriptRliValueFlowAssignment[] assignments =
        [
            Assignment("a3a", "AI_EvalFunc_OBJ_GetChannelProg_C(AI_gpst_CurrentGameObject, 0)", 11, cineBranch),
            Assignment("f_transparency_coef", "1.29f - a3a", 12, cineBranch),
            Assignment("v16", "1.0f - f_transparency_coef", 13, cineBranch),
            Assignment("v16", "v16 + v16", 14, cineBranch),
            Assignment("f_transparency_coef", "1.0f - v16", 15, cineBranch),
            Assignment("f_transparency_coef", "1.29f - a3", 20, gameplayBranch)
        ];
        CinematicScriptReachableEngineCall getter = new(functionName,
            "OBJ_GetChannelProg", "C", ["AI_gpst_CurrentGameObject", "0"],
            [], [], [], [cineBranch], "script.c", 11, functionName,
            ResultVariable: "a3a");

        bool resolved = CinematicScriptScalarValueFlowEvaluator.TryEvaluateField(owner,
            ownerObject, functionName, "f_transparency_coef", assignments, [getter],
            0, [], _ => .75f, null, out float factor);
        Check(resolved && Math.Abs(factor - .08f) < .0001f,
            "A branch-resolved channel getter and arithmetic assignments should evaluate the active OVA scalar field.");

        var unknownOwner = owner with
        {
            Variables = [new("i_cine", 0, "instance", "not known")]
        };
        bool guessed = CinematicScriptScalarValueFlowEvaluator.TryEvaluateField(unknownOwner,
            ownerObject, functionName, "f_transparency_coef", assignments, [getter],
            0, [], _ => .75f, null, out _);
        Check(!guessed,
            "An unresolved if/else mode must not select either scalar assignment branch.");

        const uint shieldOwnerKey = 0x10004AE8;
        const string messageReader = "sfx_bouclier_phare_sbp_msg";
        var messageOwner = owner with
        {
            OwnerKey = shieldOwnerKey,
            Name = "shield message fixture",
            CallbackNames = [functionName, messageReader],
            Variables = [new("i_cine", 0, "instance", "0")],
            ModelFunctionNames = [functionName, messageReader]
        };
        var messageTarget = new SceneObject { GaoKey = shieldOwnerKey };
        uint[] messageArguments = new uint[13];
        messageArguments[0] = 1;
        CinematicLiteralCall cinematicModeMessage = new(shieldOwnerKey, 0x1F78,
            messageArguments);
        bool messageDriven = CinematicScriptScalarValueFlowEvaluator.TryEvaluateField(
            messageOwner, messageTarget, functionName, "f_transparency_coef", assignments,
            [getter], 50.65f, [(50.65f, cinematicModeMessage)], _ => .75f, null,
            out float messageDrivenFactor);
        Check(messageDriven && Math.Abs(messageDrivenFactor - .08f) < .0001f,
            "Timeline MSGSendAll must flow through the reachable generic message reader and OVA i_cine write before selecting the cinematic scalar branch.");

        Console.WriteLine("PASS: callback scalar value flow resolves channel getters and preserves unknown if/else state");
    }

    private static void VerifyPackedColorBlend()
    {
        const string functionName = "blend_fixture";
        const string outputField = "resultColor";
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0, 0, "packed color fixture", [], [],
            [new("startColor", 0, "instance", "0xFF0000FF"),
             new("endColor", 0, "instance", "0xFFFFFFFF")]);
        var ownerObject = new SceneObject { GaoKey = owner.OwnerKey };
        var blendCall = new CinematicScriptReachableEngineCall(functionName,
            "COLORBlend", "C", ["startColor", "endColor", "0.5f"],
            [], [], [], [], "fixture.c", 42, functionName,
            ResultVariable: outputField);
        CinematicScriptRliValueFlowAssignment[] assignments =
        [new(functionName, outputField,
            "AI_EvalFunc_COLORBlend_C(startColor, endColor, factor)", [],
            "fixture.c", 42, functionName)];

        bool resolved = CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(owner,
            ownerObject, functionName, assignments, [blendCall], 0, [], _ => null, null,
            null, out Dictionary<string, double?> fields);
        double? resultColor = fields.GetValueOrDefault(outputField);
        Check(resolved && resultColor == 0xF77F7FF7u,
            $"COLORBlend's indexed engine-call result should resolve from owner values and preserve packed color bits (resolved={resolved}, value={resultColor}).");
        Check(CinematicPackedColorBlend.Evaluate(0x12345678u, 0xAABBCCDDu, .02f)
                == 0x12345678u
              && CinematicPackedColorBlend.Evaluate(0x12345678u, 0xAABBCCDDu, .98000002f)
                == 0xAABBCCDDu,
            "Iris COLORBlend must preserve its source-defined endpoint thresholds.");

        const string timedFunction = "timed_blend_fixture";
        var timedOwner = owner with
        {
            Name = "timed packed color fixture",
            Variables = [new("blendStart", 0, "instance", "0"),
                new("blendDuration", 0, "instance", "2")]
        };
        CinematicScriptReachableEngineCall timedBlendCall = blendCall with
        {
            FunctionName = timedFunction,
            Arguments = ["0xFFFFFF", "0x444444", "blendFactor"],
            Source = "fixture.c",
            Line = 3,
            CallbackPath = timedFunction,
            ResultVariable = "blendColor"
        };
        CinematicScriptRliValueFlowAssignment[] timedAssignments =
        [
            new(timedFunction, "elapsed", "TIM_gf_MainClock - TIM_gf_LockTime", [],
                "fixture.c", 1, timedFunction),
            new(timedFunction, "blendFactor", "(elapsed - blendStart) / blendDuration", [],
                "fixture.c", 2, timedFunction),
            new(timedFunction, "blendColor",
                "AI_EvalFunc_COLORBlend_C(0xFFFFFF, 0x444444, blendFactor)", [],
                "fixture.c", 3, timedFunction)
        ];
        bool timedResolved = CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(
            timedOwner, ownerObject, timedFunction, timedAssignments, [timedBlendCall], 1, [],
            _ => null, null, null,
            new Dictionary<string, double?>
            {
                ["TIM_gf_MainClock"] = 1,
                ["TIM_gf_LockTime"] = 0
            }, out Dictionary<string, double?> timedFields);
        Check(timedResolved && timedFields.GetValueOrDefault("blendColor")
                == CinematicPackedColorBlend.Evaluate(0xFFFFFF, 0x444444, .5f),
            "Callback value flow should resolve a packed COLORBlend from explicit scene-clock inputs.");
        Console.WriteLine("PASS: Iris COLORBlend packed-color interpolation resolves through callback value flow");
    }

    private static CinematicScriptRliValueFlowAssignment Assignment(string variable,
        string expression, int line, CinematicScriptBranchConditionSource branch) =>
        new("shield_appearance", variable, expression, [branch], "script.c", line,
            "init -> appearance");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
