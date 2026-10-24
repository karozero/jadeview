using JadeView;

internal static class CinematicScriptHierarchyBindingChecks
{
    internal static void Verify()
    {
        var endpoints = new Dictionary<uint, int>
        {
            [1] = 1,
            [2] = 1,
            [3] = 1,
            [10] = 2,
            [9] = 2
        };
        var initialParents = new Dictionary<uint, uint?> { [1] = 2 };
        var commands = new[]
        {
            Decode(1, 1, 0x1fdc, 3),
            Decode(2, 1, 0x1fdc, 3),
            Decode(2, 1, 0x1fdd),
            Decode(3, 1, 0x1fdc, 0),
            Decode(4, 1, 0x1fdc, 1),
            Decode(5, 1, 0x1fdc, 9),
            Decode(6, 1, 0x1fdc, 77)
        };

        CinematicScriptHierarchyBinding atStart = Evaluate(.5f).Single().Value;
        Check(atStart.ParentKey == 2 && atStart.Status == CinematicScriptHierarchyBindingStatus.Bound,
            "Initial serialized parent binding was not retained before timeline commands.");

        CinematicScriptHierarchyBinding afterAttach = Evaluate(1).Single().Value;
        Check(afterAttach.ParentKey == 3 && afterAttach.Status == CinematicScriptHierarchyBindingStatus.Bound,
            "HierarchySet did not bind the timeline child to its literal parent.");

        CinematicScriptHierarchyBinding afterReset = Evaluate(2).Single().Value;
        Check(afterReset.ParentKey == null && afterReset.Status == CinematicScriptHierarchyBindingStatus.Detached,
            "HierarchyReset did not detach the current binding.");

        CinematicScriptHierarchyBinding nullParent = Evaluate(3).Single().Value;
        Check(nullParent.ParentKey == null && nullParent.Status == CinematicScriptHierarchyBindingStatus.Detached,
            "HierarchySet with a null parent should have the wrapper's reset-before-set result.");

        CinematicScriptHierarchyBinding selfParent = Evaluate(4).Single().Value;
        Check(selfParent.ParentKey == null && selfParent.Status == CinematicScriptHierarchyBindingStatus.Detached,
            "HierarchySet with a self-parent should have the wrapper's reset-before-set result.");

        CinematicScriptHierarchyBinding ambiguousParent = Evaluate(5).Single().Value;
        Check(ambiguousParent.ParentKey == 9
            && ambiguousParent.Status == CinematicScriptHierarchyBindingStatus.ParentAmbiguous,
            "Ambiguous parent endpoint was not retained as an explicit unresolved binding.");

        CinematicScriptHierarchyBinding missingParent = Evaluate(6).Single().Value;
        Check(missingParent.ParentKey == 77
            && missingParent.Status == CinematicScriptHierarchyBindingStatus.ParentMissing,
            "Missing parent endpoint was silently discarded.");

        CinematicScriptHierarchyCommand[] unresolvedChildCommands =
        [
            Decode(1, 77, 0x1fdc, 3),
            Decode(2, 10, 0x1fdc, 3),
            Decode(3, 88, 0x1fdd)
        ];
        var unresolvedChildren = CinematicScriptHierarchyBindingReducer.Evaluate(unresolvedChildCommands,
            new Dictionary<uint, uint?>(), endpoints, 3);
        Check(unresolvedChildren[77].ChildEndpointCount == 0
            && unresolvedChildren[77].Status == CinematicScriptHierarchyBindingStatus.ChildMissing,
            "Missing child endpoint was incorrectly reported as a valid binding.");
        Check(unresolvedChildren[10].ChildEndpointCount == 2
            && unresolvedChildren[10].Status == CinematicScriptHierarchyBindingStatus.ChildAmbiguous,
            "Ambiguous child endpoint was incorrectly reported as a valid binding.");
        Check(unresolvedChildren[88].ChildEndpointCount == 0
            && unresolvedChildren[88].Status == CinematicScriptHierarchyBindingStatus.ChildMissing,
            "HierarchyReset with a missing child endpoint was not diagnosed.");
        Check(ambiguousParent.ParentEndpointCount == 2 && missingParent.ParentEndpointCount == 0,
            "Parent endpoint resolution counts were not preserved in the diagnostic model.");

        CinematicScriptHierarchyCommand[] cycleCommands =
        [
            Decode(0, 4, 0x1fdc, 5),
            Decode(0, 5, 0x1fdc, 4)
        ];
        var cycleEndpoints = new Dictionary<uint, int> { [4] = 1, [5] = 1 };
        var cycleState = CinematicScriptHierarchyBindingReducer.Evaluate(cycleCommands,
            new Dictionary<uint, uint?>(), cycleEndpoints, 0);
        Check(cycleState.Values.All(binding => binding.Status == CinematicScriptHierarchyBindingStatus.Cycle),
            "Cyclic parent bindings were not marked as unsupported for pose evaluation.");

        CinematicScriptHierarchyCommand? decodedSet = CinematicScriptHierarchyCommand.Decode(
            float.NaN, new(1, 0x1fdc, [2]));
        CinematicScriptHierarchyCommand? malformedSet = CinematicScriptHierarchyCommand.Decode(
            0, new(1, 0x1fdc, [2, 3]));
        CinematicScriptHierarchyCommand? malformedReset = CinematicScriptHierarchyCommand.Decode(
            0, new(1, 0x1fdd, [2]));
        Check(decodedSet is null && malformedSet is null && malformedReset is null,
            "Hierarchy command decoder accepted a non-finite time or incorrect arity.");
        Check(afterAttach.PoseStatus == CinematicScriptHierarchyPoseStatus.NotEvaluated
            && afterReset.PoseStatus == CinematicScriptHierarchyPoseStatus.NotEvaluated,
            "Binding-only evaluation incorrectly claimed a hierarchy pose was reconstructed.");

        Console.WriteLine("PASS: literal cinematic hierarchy binding reducer preserves parent-state provenance and unresolved pose status");

        IReadOnlyDictionary<uint, CinematicScriptHierarchyBinding> Evaluate(float time) =>
            CinematicScriptHierarchyBindingReducer.Evaluate(commands, initialParents, endpoints, time);

        static CinematicScriptHierarchyCommand Decode(float time, uint child, int selector,
            params uint[] arguments) => CinematicScriptHierarchyCommand.Decode(time,
                new(child, selector, arguments))
                ?? throw new InvalidDataException("Expected literal hierarchy command did not decode.");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
