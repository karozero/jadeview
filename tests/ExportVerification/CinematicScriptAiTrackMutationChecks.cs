using JadeView;

internal static class CinematicScriptAiTrackMutationChecks
{
    internal static void Verify()
    {
        var branch = new CinematicScriptBranchConditionSource("if", "has_target", 40);
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "AI track owner", [], [], []);
        var calls = new[]
        {
            Call("AITrackChange", ["4", "fct_owner_run"], [branch], 50),
            Call("AITrackStop", ["(int)2"], [], 60),
            Call("AITrackCurChange", ["fct_owner_turn"], [], 70),
            Call("AITrackCurChangeNow", ["fct_owner_idle"], [], 80),
            Call("AITrackCurStop", [], [], 90),
            Call("AITrackChange", ["track_index", "function_pointer"], [], 100),
            Call("AITrackStop", ["5"], [], 110),
            Call("OBJSetPos", ["1", "2", "3"], [], 120)
        };
        var references = new[]
        {
            FunctionReference("fct_owner_run", 0xD9000010),
            FunctionReference("fct_owner_turn", 0xD9000020),
            FunctionReference("fct_owner_idle", 0xD9000030),
            FunctionReference("fct_owner_idle", 0xD9000040)
        };
        var trackSlots = new Dictionary<string, CinematicScriptAiTrackSlotProvenance>(StringComparer.Ordinal)
        {
            ["owner_init"] = new([2], HasUnknownPath: false)
        };

        CinematicScriptAiTrackMutationCandidate[] candidates =
            CinematicScriptAiTrackMutationAnalyzer.Analyze(owner, calls, references, trackSlots);

        Check(candidates.Length == 7,
            "Only reachable AI track mutation APIs should become candidates.");
        Check(candidates[0].Api == "AITrackChange" && candidates[0].TrackIndex == 4
            && candidates[0].FunctionKey == 0xD9000010
            && candidates[0].BranchConditions.SequenceEqual(["if (has_target) at line 40"]),
            $"A numbered track change should bind its literal slot, unique function key, and branch path: {candidates[0]}");
        Check(candidates[1].Api == "AITrackStop" && candidates[1].TrackIndex == null
            && candidates[1].TrackDescription.Contains("(int)2", StringComparison.Ordinal),
            "Unsupported C expressions should remain unresolved rather than being guessed as a literal index.");
        Check(candidates[2].Api == "AITrackCurChange" && candidates[2].TrackIndex == null
            && candidates[2].FunctionKey == 0xD9000020
            && candidates[2].TrackDescription.Contains("slot(s) 2", StringComparison.Ordinal)
            && candidates[2].CurrentTrackSlotCandidates.SequenceEqual([2])
            && !candidates[2].HasUnknownCurrentTrackPath,
            "Current-track changes should use source-path slot candidates without calling them live state.");
        Check(candidates[3].Api == "AITrackCurChangeNow" && candidates[3].FunctionKey == null
            && candidates[3].FunctionDescription == "unresolved function target"
            && candidates[3].Effect.Contains("immediate same-slot re-entry", StringComparison.Ordinal),
            "Ambiguous function references should remain unresolved and immediate-reentry semantics should be explicit.");
        Check(candidates[4].Api == "AITrackCurStop"
            && candidates[4].TrackDescription.Contains("slot(s) 2", StringComparison.Ordinal)
            && candidates[4].FunctionDescription == "not applicable",
            "Current-track stop candidates should not require a function target.");
        Check(candidates[5].TrackIndex == null && candidates[5].FunctionKey == null
            && candidates[5].TrackDescription.Contains("track_index", StringComparison.Ordinal),
            "Dynamic track slots and callback pointers should remain unresolved.");
        Check(candidates[6].TrackIndex == 5
            && candidates[6].TrackDescription.Contains("outside 0–4", StringComparison.Ordinal),
            "Out-of-range literal slots should be reported, not silently clamped.");

        var slots = new[]
        {
            new CinematicScriptCallbackSlot(0, 0x100, "root_a"),
            new CinematicScriptCallbackSlot(2, 0x200, "root_b")
        };
        var functionCalls = new Dictionary<string, CinematicScriptFunctionCall[]>(StringComparer.Ordinal)
        {
            ["root_a"] =
            [
                new("shared", "execute", null),
                new("track_four", "track-change", 4),
                new("dynamic_track", "track-change", null),
                new("callback_target", "register-callback:1", null)
            ],
            ["root_b"] = [new("shared", "execute", null)],
            ["shared"] = [new("shared_child", "replace-current-track", null)],
            ["track_four"] = [new("track_four_child", "set-current-track", null)],
            ["dynamic_track"] = [new("dynamic_child", "set-current-track", null)],
            ["callback_target"] = [new("callback_child", "set-current-track", null)]
        };
        var modelFunctions = functionCalls.Keys.Concat(functionCalls.Values.SelectMany(edges => edges)
            .Select(edge => edge.To)).ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, CinematicScriptAiTrackSlotProvenance> provenance =
            CinematicScriptAiTrackSlotProvenanceAnalyzer.Analyze(slots, functionCalls, modelFunctions);
        Check(provenance["shared"].CandidateSlots.SequenceEqual([0, 2])
            && !provenance["shared"].HasUnknownPath
            && provenance["shared_child"].CandidateSlots.SequenceEqual([0, 2]),
            "AIExecute and current-track dispatch should retain every possible serialized root slot.");
        Check(provenance["track_four"].CandidateSlots.SequenceEqual([4])
            && provenance["track_four_child"].CandidateSlots.SequenceEqual([4]),
            "A numbered AITrackChange edge should switch provenance to its destination slot.");
        Check(provenance["dynamic_track"].CandidateSlots.Length == 0
            && provenance["dynamic_track"].HasUnknownPath
            && provenance["dynamic_child"].HasUnknownPath
            && provenance["callback_target"].HasUnknownPath
            && provenance["callback_child"].HasUnknownPath,
            "Dynamic slot changes and direct engine callback dispatch must preserve unknown slot provenance.");

        Console.WriteLine("PASS: AI track mutation candidates preserve source targets, slot provenance, branches, and unknown runtime state");

        CinematicScriptReachableEngineCall Call(string api, string[] arguments,
            CinematicScriptBranchConditionSource[] branches, int line) => new(
            "owner_init", api, "C", arguments, [], [], [], branches,
            "ai_track_fixture.c", line, $"owner_init -> {api}");

        CinematicScriptReachableSourceReference FunctionReference(string symbol, uint key) =>
            new("owner_init", "Function", symbol, key,
                "ai_track_fixture.c", 25, "owner_init");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
