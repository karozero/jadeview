using JadeView;

internal static class PcAiScalarConstantAssignmentChecks
{
    internal static void Verify()
    {
        var function = new PcSerializedAiFunction(0,
        [
            Node(0, 0, 0x21, 0x14, "Global variable"),
            Node(1, 1, 0x21, 0x01, "Type"),
            Node(2, 0, 0x0898, 0x02, "Keyword", "Affect"),
            Node(3, 0, 0, 0x00, "End")
        ], [], [], [], []);
        PcAiScalarConstantAssignment assignment = PcAiScalarConstantAssignmentAnalyzer.Analyze(function,
            node => node.Index == 0 ? "load_world" : null).Single();
        Require(assignment == new PcAiScalarConstantAssignment(0, "load_world", 1, 1, 2),
            "The serialized scalar integer assignment target, literal, and Affect node were not retained.");

        var unsupportedTarget = function with
        {
            Nodes = function.Nodes.Select(node => node.Index == 0
                ? node with { EncodedCategory = 0x15, CategoryName = "Global array" } : node).ToArray()
        };
        Require(PcAiScalarConstantAssignmentAnalyzer.Analyze(unsupportedTarget, _ => null).Length == 0,
            "Array writes must not be reported as scalar assignments.");

        var unsupportedValue = function with
        {
            Nodes = function.Nodes.Select(node => node.Index == 1
                ? node with { EncodedValue2 = 0x22 } : node).ToArray()
        };
        Require(PcAiScalarConstantAssignmentAnalyzer.Analyze(unsupportedValue, _ => null).Length == 0,
            "Non-primary int encodings must remain unresolved until their literal semantics are verified.");

        Console.WriteLine("PASS: serialized scalar int literal Affect patterns and conservative unsupported cases");
    }

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category,
        string categoryName, string? symbol = null) =>
        new(index, value, value2, 0, category, categoryName, symbol, null, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
