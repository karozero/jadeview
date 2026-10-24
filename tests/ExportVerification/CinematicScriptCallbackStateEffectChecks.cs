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
            Call("OBJDestroy", [], [], 120)
        };
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "owner", [], [], []);
        var sourceReferences = new[]
        {
            new CinematicScriptReachableSourceReference("test_callback", "Object", "target",
                0x12345678, "test_script.c", 80, "callback[2] -> test_callback")
        };

        CinematicScriptCallbackStateEffect[] effects =
            CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner, calls, sourceReferences);

        Check(effects.Length == 8, "The callback effect projection included unrelated engine APIs or omitted a supported state mutation.");
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
        var conditionalReceiverCall = new CinematicScriptReachableEngineCall("test_callback",
            "OBJ_FlagInvisibleSet", "C", ["receiver"], [], [], [], [], "test_script.c", 118,
            "callback[2] -> test_callback",
            ArgumentAssignmentAlternatives: [new("receiver", "AI_gpst_CurrentGameObject", 118, [branch])]);
        Check(!CinematicScriptArgumentBindingResolver.FirstArgumentUsesCurrentGameObject(conditionalReceiverCall)
            && CinematicScriptArgumentBindingResolver.ResolveFirstArgumentObjectReferences(
                conditionalReceiverCall, sourceReferences).Length == 0,
            "A branch-dependent receiver alternative must remain unresolved instead of being attributed to the owner.");

        Console.WriteLine("PASS: callback state-effect evidence retains owner, source arguments and guards without inventing execution");

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
