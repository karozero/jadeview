using JadeView;

internal static class PcAiFunctionCallBindingChecks
{
    internal static void Verify()
    {
        var function = new PcSerializedAiFunction(0,
        [
            Node(0, 24, 0x21, 0x14, "Global variable"),
            Node(1, 20, 0x21, 0x14, "Global variable"),
            Node(2, 0, 0x1F46, 0x03, "Function", "WORLoadList"),
            Node(3, 0, 0x0898, 0x02, "Keyword", "Affect"),
            Node(4, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding binding = PcAiFunctionCallBindingAnalyzer.Analyze(function,
            node => node.Value switch { 24 => "world_loaded_id", 20 => "world_key", _ => null })
            .Single();
        Require(binding == new PcAiFunctionCallBinding(2, "WORLoadList", 1, "world_key",
                0, "world_loaded_id", "LinearOperandPattern",
                "external/irisEng/ai/ai_worldfunctions.c:100-120; external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:213",
                ResultAssignmentNodeIndex: 3),
            "The serialized single-argument/result assignment pattern was not reconstructed.");

        var literalArgumentFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 24, 0x21, 0x14, "Global variable"),
            Node(1, unchecked((int)0xFF005833), 0x21, 0x01, "Type"),
            Node(2, 0, 0x1F46, 0x03, "Function", "WORLoadList"),
            Node(3, 0, 0x0898, 0x02, "Keyword", "Affect"),
            Node(4, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding literalBinding = PcAiFunctionCallBindingAnalyzer.Analyze(
            literalArgumentFunction, _ => "world_loaded_id").Single();
        Require(literalBinding.ArgumentDescription == $"integer literal {unchecked((int)0xFF005833)}"
                && literalBinding.Status == "LinearOperandPattern",
            "Typed int literals must be distinguished from their Type category in serialized operands.");

        var incompleteFunction = function with { Nodes = function.Nodes[..3] };
        PcAiFunctionCallBinding incomplete = PcAiFunctionCallBindingAnalyzer.Analyze(incompleteFunction,
            _ => null).Single();
        Require(incomplete.Status == "UnresolvedStackBinding" && incomplete.ResultTargetNodeIndex == null,
            "A call without an adjacent result assignment must remain unresolved.");

        var activationFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 24, 0x21, 0x14, "Global variable"),
            Node(1, 0, 0x1F41, 0x03, "Function", "WORActive"),
            Node(2, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding activation = PcAiFunctionCallBindingAnalyzer.Analyze(activationFunction,
            node => node.Value == 24 ? "world_loaded_id" : null).Single();
        Require(activation.ArgumentDescription == "world_loaded_id"
                && activation.ResultTargetDescription == null
                && activation.Status == "LinearOperandPattern",
            "WORActive must be recognized as a one-argument, no-result activation call.");

        var loadedWorldFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 24, 0x21, 0x14, "Global variable"),
            Node(1, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            Node(2, 1, 0x21, 0x01, "Type"),
            Node(3, 0, 0x08A7, 0x02, "Keyword", "Minus"),
            Node(4, 0, 0x0898, 0x02, "Keyword", "Affect"),
            Node(5, 24, 0x21, 0x14, "Global variable"),
            Node(6, 0, 0x1F41, 0x03, "Function", "WORActive"),
            Node(7, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding[] loadedWorldBindings = PcAiFunctionCallBindingAnalyzer.Analyze(
            loadedWorldFunction,
            node => node.Value == 24 ? "world_loaded_id" : null);
        PcAiFunctionCallBinding loadedWorldResult = loadedWorldBindings.Single(binding => binding.Operation == "WORLoaded");
        PcAiFunctionCallBinding loadedWorldActivation = loadedWorldBindings.Single(binding => binding.Operation == "WORActive");
        Require(loadedWorldResult.Status == "ResolvedConstantExpression"
                && loadedWorldResult.ResultTargetDescription == "world_loaded_id"
                && loadedWorldResult.ResolvedIntegerValue == 0
                && loadedWorldResult.ResultAssignmentNodeIndex == 4
                && loadedWorldActivation.ArgumentDescription == "world_loaded_id"
                && loadedWorldActivation.ResolvedArgumentValue == 0
                && loadedWorldActivation.ArgumentValueAssignmentNodeIndex == 4,
            "The source-confirmed WORLoaded() result must flow through serialized subtraction to WORActive.");

        var unsupportedExpression = loadedWorldFunction with
        {
            Nodes = loadedWorldFunction.Nodes.Select(node => node.Index == 3
                ? node with { SymbolName = "UnknownOperator" } : node).ToArray()
        };
        PcAiFunctionCallBinding unresolvedLoadedWorld = PcAiFunctionCallBindingAnalyzer.Analyze(
            unsupportedExpression, _ => "world_loaded_id").Single(binding => binding.Operation == "WORLoaded");
        Require(unresolvedLoadedWorld.Status == "UnresolvedStackBinding"
                && unresolvedLoadedWorld.ResolvedIntegerValue == null,
            "An unknown integer operator must not be folded into a runtime slot value.");

        var constantOperations = new (string Name, ushort Key, int Right, int Expected)[]
        {
            ("Equal", 0x08A1, 1, 1),
            ("GreatEq", 0x08A2, 1, 1),
            ("LessEq", 0x08A3, 1, 1),
            ("Great", 0x08A4, 0, 1),
            ("Less", 0x08A5, 2, 1),
            ("Different", 0x08A6, 0, 1),
            ("OrOp", 0x08AE, 2, 3),
            ("AndOp", 0x08AF, 1, 1),
            ("XorOp", 0x08B0, 2, 3)
        };
        foreach ((string operationName, ushort operationKey, int rightOperand, int expectedValue)
            in constantOperations)
        {
            var expressionFunction = new PcSerializedAiFunction(0,
            [
                Node(0, 24, 0x21, 0x14, "Global variable"),
                Node(1, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
                Node(2, rightOperand, 0x21, 0x01, "Type"),
                Node(3, 0, operationKey, 0x02, "Keyword", operationName),
                Node(4, 0, 0x0898, 0x02, "Keyword", "Affect"),
                Node(5, 0, 0, 0, "End")
            ], [], [], [], []);
            PcAiFunctionCallBinding expressionBinding = PcAiFunctionCallBindingAnalyzer.Analyze(
                expressionFunction, _ => "result").Single();
            Require(expressionBinding.Status == "ResolvedConstantExpression"
                    && expressionBinding.ResolvedIntegerValue == expectedValue,
                $"Source-confirmed integer operation {operationName} did not fold its constant operands.");
        }

        var readinessCheck = new PcSerializedAiFunction(0,
        [
            Node(0, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            Node(1, 0, 0x089F, 0x02, "Keyword", "Not"),
            Node(2, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding readiness = PcAiFunctionCallBindingAnalyzer.Analyze(readinessCheck,
            _ => null).Single();
        Require(readiness.Status == "UnresolvedStackBinding"
                && readiness.ResolvedIntegerValue == null,
            "A WORLoaded predicate must not be misreported as an assignment or readiness wait.");

        var bypassableAssignment = new PcSerializedAiFunction(0,
        [
            new(0, 7, 0x08FC, 0, 0x02, "Keyword", "Jump", 7,
                "jump -> node 7"),
            Node(1, 24, 0x21, 0x14, "Global variable"),
            Node(2, 0, 0x1F51, 0x03, "Function", "WORLoaded"),
            Node(3, 1, 0x21, 0x01, "Type"),
            Node(4, 0, 0x08A7, 0x02, "Keyword", "Minus"),
            Node(5, 0, 0x0898, 0x02, "Keyword", "Affect"),
            Node(6, 24, 0x21, 0x14, "Global variable"),
            Node(7, 0, 0x1F41, 0x03, "Function", "WORActive"),
            Node(8, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiFunctionCallBinding bypassableActivation = PcAiFunctionCallBindingAnalyzer.Analyze(
            bypassableAssignment, node => node.Value == 24 ? "world_loaded_id" : null)
            .Single(binding => binding.Operation == "WORActive");
        Require(bypassableActivation.ResolvedArgumentValue == null,
            "A branch that can jump around the constant assignment must prevent argument propagation.");

        Console.WriteLine("PASS: source-backed AI call binding resolves integer arguments, arithmetic/comparison/bitwise constants, and activation operands without evaluating execution");
    }

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category,
        string categoryName, string? symbol = null) =>
        new(index, value, value2, 0, category, categoryName, symbol, null, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
