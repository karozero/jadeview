using JadeView;

internal static class CinematicScriptCallbackRegistrationChecks
{
    internal static void Verify()
    {
        var guard = new CinematicScriptBranchConditionSource("if", "has_target", 40);
        var serializedFlagGuard = new CinematicScriptBranchConditionSource("if",
            "AI_EvalFunc_OBJ_FlagsIdentityTest_C(AI_gpst_CurrentGameObject, 0x20000)", 40);
        var calls = new[]
        {
            new CinematicScriptReachableEngineCall("Garde_init", "AICBAdd", "C",
                ["AI_gpst_CurrentGameObject", "destination", "2",
                    "fct_Garde_init_Garde_on_callback"],
                [new("destination", "AI_MainActor[index]", 39, 0, IsUnique: false)],
                [], [], [guard, serializedFlagGuard], "ai_fixture.c", 41, "Garde_init -> register-callback:2",
                ArgumentAssignmentAlternatives:
                [new("destination", "AI_MainActor[index]", 39, [guard])]),
            new CinematicScriptReachableEngineCall("Garde_init", "AICBDel", "C",
                ["AI_gpst_CurrentGameObject", "0", "2", "fct_Garde_init_Garde_on_callback"],
                [], [], [], [], "ai_fixture.c", 46, "Garde_init -> unregister-callback:2"),
            new CinematicScriptReachableEngineCall("Garde_init", "OBJSetPos", "C",
                ["1", "2", "3"], [], [], [], [], "ai_fixture.c", 51, "Garde_init")
        };
        var references = new[]
        {
            new CinematicScriptReachableSourceReference("Garde_init", "Function", "Garde_on_callback",
                0xD9001234, "ai_fixture.c", 29, "Garde_init -> Garde_on_callback")
        };
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "callback owner", [], [], [])
        {
            SerializedGaoFlags = 0x20000
        };

        CinematicScriptCallbackRegistrationCandidate[] candidates =
            CinematicScriptCallbackRegistrationAnalyzer.Analyze(owner, calls, references);
        Check(candidates.Length == 2 && candidates[0].Operation == "Register candidate"
            && candidates[1].Operation == "Remove candidate",
            "The callback registration analyzer should include add/delete sites and ignore unrelated APIs.");
        Check(candidates[0].CallbackType == 2 && candidates[0].FunctionKey == 0xD9001234
            && candidates[0].FunctionName == "function D9001234"
            && candidates[0].FunctionOperand == "fct_Garde_init_Garde_on_callback",
            "The callback candidate should keep the literal ID, source-resolved function key, and raw function operand.");
        Check(candidates[0].CallbackSourceContext == "AI_gpst_CurrentGameObject"
            && candidates[0].DestinationInstance.Contains("ambiguous assignment", StringComparison.Ordinal)
            && candidates[0].DestinationInstance.Contains("alternative", StringComparison.Ordinal)
            && candidates[0].DestinationInstance.Contains("if (has_target)", StringComparison.Ordinal)
            && candidates[0].BranchConditions.Length == 2
            && candidates[0].SerializedBranchFindings.Length == 1
            && candidates[0].SerializedBranchFindings[0].Contains("if branch true", StringComparison.Ordinal),
            "The candidate should preserve uncertain destination assignments and branch guards instead of choosing a runtime target.");
        Check(CinematicScriptReader.EngineDispatchSitesFor(2).Length > 0
            && CinematicScriptReader.EngineDispatchSitesFor(999).Length == 0,
            "Literal callback IDs should join only dispatch sites indexed from the Iris engine source.");
        CinematicScriptCallbackDispatchSite[] callbackTwoDispatches =
            CinematicScriptReader.EngineDispatchSitesFor(2);
        CinematicScriptCallbackDispatchSite[] callbackThreeDispatches =
            CinematicScriptReader.EngineDispatchSitesFor(3);
        CinematicScriptCallbackDispatchSite[] callbackZeroDispatches =
            CinematicScriptReader.EngineDispatchSitesFor(0);
        CinematicScriptCallbackDispatchSite[] callbackOneDispatches =
            CinematicScriptReader.EngineDispatchSitesFor(1);
        CinematicScriptCallbackDispatchSite[] indexedDispatchSites = Enumerable.Range(0, 4)
            .SelectMany(CinematicScriptReader.EngineDispatchSitesFor).ToArray();
        Check(indexedDispatchSites.Length == 10
            && indexedDispatchSites.All(site => !string.IsNullOrWhiteSpace(site.EnclosingFunction)
                && !string.IsNullOrWhiteSpace(site.ExecutionContext)
                && !string.IsNullOrWhiteSpace(site.DispatcherEligibility)
                && !string.IsNullOrWhiteSpace(site.CallerEligibility)
                && !string.IsNullOrWhiteSpace(site.TargetResolution)),
            "Every indexed retail callback site should retain caller, dispatcher, target, and source context metadata.");
        Check(callbackTwoDispatches.Length == 1
            && callbackTwoDispatches[0].EnclosingFunction == "ENG_OneWorldEngineCall"
            && callbackTwoDispatches[0].ExecutionContext.Contains("after REC", StringComparison.Ordinal)
            && callbackTwoDispatches[0].DispatcherEligibility.Contains("AICallbacksTablePF", StringComparison.Ordinal)
            && callbackTwoDispatches[0].TargetResolution.Contains("world AI-callback EOT", StringComparison.Ordinal),
            "World callback ID 2 should retain its post-REC phase, world-table dispatcher gate, and target scope.");
        Check(callbackThreeDispatches.Any(site => site.EnclosingFunction == "ANI_BeforeRender")
            && callbackThreeDispatches.Any(site => site.EnclosingFunction == "ANI_Render")
            && callbackThreeDispatches.All(site => !string.IsNullOrWhiteSpace(site.ContextEvidence)
                && !string.IsNullOrWhiteSpace(site.DispatcherEligibility))
            && callbackThreeDispatches.Any(site => site.CallerEligibility.Contains("skeleton group", StringComparison.Ordinal))
            && callbackThreeDispatches.Any(site => site.CallerEligibility.Contains("animPtr[0]", StringComparison.Ordinal)),
            "Animation callback ID 3 should expose separate source caller gates and the shared Iris dispatcher gate.");
        Check(callbackZeroDispatches.Length == 3
            && callbackZeroDispatches.All(site => site.EnclosingFunction is "GEO_Render" or "STR_Render")
            && callbackZeroDispatches.All(site => site.ExecutionContext.Contains("before", StringComparison.Ordinal))
            && callbackZeroDispatches.Any(site => site.TargetResolution.Contains("bone GAO", StringComparison.Ordinal))
            && callbackZeroDispatches.Any(site => site.CallerEligibility.Contains("material/draw-list", StringComparison.Ordinal))
            && callbackOneDispatches.Length == 3
            && callbackOneDispatches.All(site => site.EnclosingFunction is "GEO_Render" or "STR_Render")
            && callbackOneDispatches.All(site => site.ExecutionContext.Contains("after", StringComparison.Ordinal))
            && callbackOneDispatches.Any(site => site.CallerEligibility.Contains("valid-vertices", StringComparison.Ordinal))
            && callbackOneDispatches.Any(site => site.TargetResolution.Contains("string GAO", StringComparison.Ordinal)),
            "Render callback IDs 0/1 should retain per-site geometry/string gates and bone-target routing.");
        CinematicScriptCallbackRegistrationCandidate unresolved =
            CinematicScriptCallbackRegistrationAnalyzer.Analyze(
                owner, [new CinematicScriptReachableEngineCall("Garde_init", "AICBAdd", "C",
                    ["context", "destination", "phaseVariable", "callbackPointer"],
                    [], [], [], [], "ai_fixture.c", 60, "Garde_init")], [])[0];
        Check(unresolved.CallbackType == null && unresolved.FunctionKey == null
            && unresolved.FunctionName == "unresolved callback function"
            && unresolved.FunctionOperand == "callbackPointer",
            "Dynamic callback IDs and function pointers should remain explicitly unresolved while preserving raw operands.");

        var matchingReferences = new[]
        {
            new CinematicScriptReachableSourceReference("Garde_init", "Function", "Garde_on_callback",
                0xD9001234, "ai_fixture.c", 29, "Garde_init -> Garde_on_callback"),
            new CinematicScriptReachableSourceReference("Garde_init", "Function", "Garde_other_callback",
                0xD9005678, "ai_fixture.c", 30, "Garde_init -> Garde_other_callback"),
            new CinematicScriptReachableSourceReference("Garde_init", "Object", "Destination",
                0xF0000101, "ai_fixture.c", 31, "Garde_init"),
            new CinematicScriptReachableSourceReference("Garde_init", "Object", "OtherDestination",
                0xF0000102, "ai_fixture.c", 32, "Garde_init"),
            new CinematicScriptReachableSourceReference("Garde_init", "Object", "OtherContext",
                0xF0000103, "ai_fixture.c", 33, "Garde_init")
        };
        var exactTupleCalls = new[]
        {
            CallbackCall("AICBAdd", "AI_gpst_CurrentGameObject", "fct_Garde_init_Destination",
                "2", "fct_Garde_init_Garde_on_callback", 70),
            CallbackCall("AICBDel", "AI_gpst_CurrentGameObject", "fct_Garde_init_Destination",
                "2", "fct_Garde_init_Garde_on_callback", 80),
            CallbackCall("AICBDel", "AI_gpst_CurrentGameObject", "fct_Garde_init_OtherDestination",
                "2", "fct_Garde_init_Garde_on_callback", 81),
            CallbackCall("AICBDel", "fct_Garde_init_OtherContext", "fct_Garde_init_Destination",
                "2", "fct_Garde_init_Garde_on_callback", 82),
            CallbackCall("AICBDel", "AI_gpst_CurrentGameObject", "fct_Garde_init_Destination",
                "3", "fct_Garde_init_Garde_on_callback", 83),
            CallbackCall("AICBDel", "AI_gpst_CurrentGameObject", "fct_Garde_init_Destination",
                "2", "fct_Garde_init_Garde_other_callback", 84),
            CallbackCall("AICBDel", "AI_gpst_CurrentGameObject", "AI_MainActor[slot]",
                "2", "fct_Garde_init_Garde_on_callback", 85)
        };
        CinematicScriptCallbackRegistrationCandidate[] exactTupleCandidates =
            CinematicScriptCallbackRegistrationAnalyzer.Analyze(owner, exactTupleCalls, matchingReferences);
        Check(exactTupleCandidates[0].MatchingRemovalSites.SequenceEqual(["ai_fixture.c:80"]),
            "Only a deletion with the same uniquely resolved context, destination, literal callback ID, and function key should match a registration.");
        Check(exactTupleCandidates.Skip(2).All(candidate => candidate.MatchingRemovalSites.Length == 0),
            "Mismatched context, destination, ID, function, and dynamic actor slots must not be associated with a registration.");
        var resetCall = new CinematicScriptReachableEngineCall("Garde_init", "AIReinitVars", "C",
            [], [], [], [], [guard], "ai_fixture.c", 90, "Garde_init -> possible-reset",
            ArgumentAssignmentAlternatives: []);
        CinematicScriptCallbackResetCandidate[] resets =
            CinematicScriptCallbackRegistrationAnalyzer.AnalyzeResetSites(owner,
                [resetCall, exactTupleCalls[0]]);
        Check(resets.Length == 1 && resets[0].Source == "ai_fixture.c"
            && resets[0].Line == 90 && resets[0].BranchConditions.Length == 1,
            "AIReinitVars should be surfaced as a distinct possible current-instance reset site with its source guards.");

        Console.WriteLine("PASS: dynamic AI callback registrations retain tuple operands, branch alternatives, and source dispatch evidence");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static CinematicScriptReachableEngineCall CallbackCall(string api, string context,
        string destination, string callbackId, string function, int line) =>
        new("Garde_init", api, "C", [context, destination, callbackId, function],
            [], [], [], [], "ai_fixture.c", line, $"Garde_init -> {api}:{callbackId}");
}
