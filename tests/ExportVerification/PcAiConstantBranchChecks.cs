using JadeView;

internal static class PcAiConstantBranchChecks
{
    internal static void Verify()
    {
        var falseBranchFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            Node(1, 0, 0x089F, 0x02, "Keyword", "Not"),
            new(2, 4, 0x0900, 0, 0x02, "Keyword", "JumpFalseSP", 4,
                "false branch -> node 4; true branch -> node 3"),
            Node(3, 0, 0x1F00, 0x03, "Function", "GFXAddWorld"),
            Node(4, 0, 0, 0, "End")
        ], [], [], [], []);
        PcAiConstantBranch falseBranch = PcAiConstantBranchAnalyzer.Analyze(falseBranchFunction).Single();
        Require(falseBranch.ConditionDescription == "WORLoaded() = 1; Not => false"
                && !falseBranch.ConditionIsTrue
                && falseBranch.BranchTargetNodeIndex == 4
                && falseBranch.FallthroughNodeIndex == 3
                && falseBranch.ChosenNodeIndex == 4
                && falseBranch.ChosenEdge == "branch target",
            "A source-confirmed WORLoaded/Not condition must select the serialized false-branch target.");

        var trueBranchFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            new(1, 3, 0x0901, 0, 0x02, "Keyword", "JumpTrueSP", 3,
                "true branch -> node 3; false branch -> node 2"),
            Node(2, 0, 0, 0, "End"),
            Node(3, 0, 0, 0, "End")
        ], [], [], [], []);
        PcAiConstantBranch trueBranch = PcAiConstantBranchAnalyzer.Analyze(trueBranchFunction).Single();
        Require(trueBranch.ConditionIsTrue && trueBranch.ChosenNodeIndex == 3
                && trueBranch.ChosenEdge == "branch target",
            "A nonzero source-confirmed return must take JumpTrueSP's relative branch.");

        var comparisonBranchFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            Node(1, 1, 0x21, 0x01, "Type"),
            Node(2, 0, 0x08A1, 0x02, "Keyword", "Equal"),
            new(3, 5, 0x0901, 0, 0x02, "Keyword", "JumpTrueSP", 5,
                "true branch -> node 5; false branch -> node 4"),
            Node(4, 0, 0, 0, "End"),
            Node(5, 0, 0, 0, "End")
        ], [], [], [], []);
        PcAiConstantBranch comparisonBranch = PcAiConstantBranchAnalyzer.Analyze(
            comparisonBranchFunction).Single();
        Require(comparisonBranch.ConditionDescription == "WORLoaded() = 1; 1 == 1 = 1"
                && comparisonBranch.ConditionIsTrue && comparisonBranch.ChosenNodeIndex == 5,
            "A source-confirmed integer comparison must resolve before a conditional branch.");

        var dynamicBranchFunction = falseBranchFunction with
        {
            Nodes = falseBranchFunction.Nodes.Select(node => node.Index == 0
                ? node with { SymbolName = "UnknownFunction" } : node).ToArray()
        };
        Require(PcAiConstantBranchAnalyzer.Analyze(dynamicBranchFunction).Length == 0,
            "A branch without a source-confirmed constant return must remain unevaluated.");

        var malformedTargetFunction = falseBranchFunction with
        {
            Nodes = falseBranchFunction.Nodes.Select(node => node.Index == 2
                ? node with { RelativeBranchTarget = 90 } : node).ToArray()
        };
        Require(PcAiConstantBranchAnalyzer.Analyze(malformedTargetFunction).Length == 0,
            "An out-of-range branch target must remain unresolved.");

        Console.WriteLine("PASS: source-backed constant AI return, integer-comparison branches, logical Not, conditional jump edges, and unresolved fallbacks");
    }

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category,
        string categoryName, string? symbol = null) =>
        new(index, value, value2, 0, category, categoryName, symbol, null, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
