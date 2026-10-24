using JadeView;

internal static class PcAiFunctionCallGraphChecks
{
    internal static void Verify()
    {
        var functions = new Dictionary<uint, PcSerializedAiFunction>
        {
            [0x1] = Function((0x2, true), (0xFE, false)),
            [0x2] = Function((0x3, true), (0xDEADBEEF, true)),
            [0x3] = Function((0x1, true)),
            [0x4] = TrackChangeFunction(0x2),
            [0x5] = TrackChangeFunction(0x5)
        };
        var names = new Dictionary<uint, string>
        {
            [0x1] = "root.ofc",
            [0x2] = "middle.ofc",
            [0x3] = "leaf.ofc",
            [0x4] = "switch.ofc",
            [0x5] = "same.ofc"
        };
        PcAiFunctionCallPath[] paths = PcAiFunctionCallGraphAnalyzer.Analyze(0x1, "root.ofc",
            functions[0x1], names, key => functions.GetValueOrDefault(key));

        Require(paths.Any(path => path.Status == "Linked"
                && path.FunctionKeys.SequenceEqual([0x1u, 0x2u, 0x3u])
                && path.CallNodeIndexes.SequenceEqual([1, 1])
                && path.TransferKinds.SequenceEqual(["AIExecute", "AIExecute"])),
            "A transitive literal AIExecute chain was not retained.");
        Require(paths.Any(path => path.Status == "Cycle"
                && path.FunctionKeys.SequenceEqual([0x1u, 0x2u, 0x3u, 0x1u])),
            "A recursive AIExecute cycle was not bounded and reported.");
        Require(paths.Any(path => path.Status == "PointerRefNotLinkedByModel"
                && path.FunctionKeys.SequenceEqual([0x1u, 0x2u, 0xDEADBEEFu])),
            "An unlinked function reference was incorrectly followed.");
        Require(paths.Any(path => path.Status == "IndirectOrNonLiteralArgument"
                && path.FunctionKeys.SequenceEqual([0x1u])),
            "An indirect AIExecute argument was not retained as an unresolved leaf.");

        PcAiFunctionCallPath transition = PcAiFunctionCallGraphAnalyzer.Analyze(0x4, "switch.ofc",
            functions[0x4], names, key => functions.GetValueOrDefault(key))
            .Single(path => path.Status == "TrackTransition");
        Require(transition.FunctionKeys.SequenceEqual([0x4u, 0x2u])
                && transition.TransferKinds.SequenceEqual(["AITrackCurChangeNow"]),
            "AITrackCurChangeNow was not represented as a distinct serialized track transition.");

        PcAiFunctionCallPath selfTransition = PcAiFunctionCallGraphAnalyzer.Analyze(0x5, "same.ofc",
            functions[0x5], names, key => functions.GetValueOrDefault(key)).Single();
        Require(selfTransition.Status == "SelfTrackNoOp",
            "A current-track change to the same function must not be reported as recursion.");

        Console.WriteLine("PASS: bounded AI function flow distinguishes nested calls, track transitions, indirect, unlinked and cyclic edges");
    }

    private static PcSerializedAiFunction TrackChangeFunction(uint target) => new(0,
    [
        Node(0, unchecked((int)target), 0x27, 0x01, "Type"),
        Node(1, 0, 0x1789, 0x03, "Function", "AITrackCurChangeNow")
    ], [], [], [], []);

    private static PcSerializedAiFunction Function(params (uint Target, bool Linked)[] targets)
    {
        var nodes = new List<PcAiInstruction>();
        foreach ((uint target, bool linked) in targets)
        {
            if (target != 0xFE || linked)
                nodes.Add(Node(nodes.Count, unchecked((int)target), linked ? (ushort)0x27 : (ushort)0x21,
                    linked ? (byte)0x01 : (byte)0x14, linked ? "Type" : "Global variable"));
            nodes.Add(Node(nodes.Count, 0, 0x1784, 0x03, "Function", "AIExecute"));
        }
        return new(0, nodes.ToArray(), [], [], [], []);
    }

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category,
        string categoryName, string? symbol = null) =>
        new(index, value, value2, 0, category, categoryName, symbol, null, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
