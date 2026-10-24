using JadeView;

internal static class PcAiConstantArrayTableChecks
{
    internal static void Verify()
    {
        PcSerializedAiFunction function = BuildInitializer(pairCount: 4);
        PcAiConstantArrayTable[] tables = PcAiConstantArrayTableAnalyzer.Analyze(function);
        if (tables.Length != 1)
            throw new InvalidDataException($"Expected one parallel-array candidate, found {tables.Length}.");

        PcAiConstantArrayTable table = tables[0];
        if (table.FirstArrayOffset != 100 || table.SecondArrayOffset != 200
            || table.FirstNodeIndex != 13 || table.NodeStride != 20
            || !table.Pairs.SequenceEqual([
                new PcAiConstantArrayPair(20, 0xA9003274),
                new PcAiConstantArrayPair(21, 0x1000187B),
                new PcAiConstantArrayPair(22, 0x7100F4E2),
                new PcAiConstantArrayPair(23, 0x7100F4F4)]))
            throw new InvalidDataException("Parallel-array candidate values or source layout were decoded incorrectly.");

        if (PcAiConstantArrayTableAnalyzer.Analyze(BuildInitializer(pairCount: 3)).Length != 0)
            throw new InvalidDataException("A short repeated-write sequence must not be promoted to a table candidate.");

        PcSerializedAiFunction brokenPattern = BuildInitializer(pairCount: 4);
        brokenPattern.Nodes[36] = brokenPattern.Nodes[36] with { SymbolName = "Jump" };
        if (PcAiConstantArrayTableAnalyzer.Analyze(brokenPattern).Length != 0)
            throw new InvalidDataException("A structurally inconsistent sequence must not be promoted to a table candidate.");

        Console.WriteLine("PASS: repeated global integer-array initializer detection and conservative structural bounds");
    }

    private static PcSerializedAiFunction BuildInitializer(int pairCount)
    {
        int nodeCount = pairCount * 20 + 13;
        PcAiInstruction[] nodes = Enumerable.Range(0, nodeCount)
            .Select(index => new PcAiInstruction(index, 0, 0, 0, 0x02,
                "Keyword", "Nop", null, null))
            .ToArray();

        for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            int pairStart = 13 + pairIndex * 20;
            SetArrayWrite(nodes, pairStart, 100, 20 + pairIndex);
            SetArrayWrite(nodes, pairStart + 8, 200, TestKeys[pairIndex]);
            nodes[pairStart + 14] = Instruction(pairStart + 14, 300, 0x21,
                0x01, "Global variable", null);
            nodes[pairStart + 15] = Instruction(pairStart + 15, 0, 0x0806,
                0x02, "Keyword", "Ultra");
            nodes[pairStart + 16] = Instruction(pairStart + 16, 300, 0x21,
                0x01, "Global variable", null);
            nodes[pairStart + 17] = Instruction(pairStart + 17, 0, 0x089B,
                0x02, "Keyword", "IncAffect");
        }

        return new(0, nodes, [], [], [], []);
    }

    private static readonly int[] TestKeys =
    [
        unchecked((int)0xA9003274),
        0x1000187B,
        0x7100F4E2,
        0x7100F4F4
    ];

    private static void SetArrayWrite(PcAiInstruction[] nodes, int nodeIndex, int arrayOffset, int value)
    {
        nodes[nodeIndex] = Instruction(nodeIndex, arrayOffset, 0x21,
            0x16, "Global array", null);
        nodes[nodeIndex + 1] = Instruction(nodeIndex + 1, value, 0x21,
            0x01, "Type", null);
        nodes[nodeIndex + 2] = Instruction(nodeIndex + 2, 0, 0x0898,
            0x02, "Keyword", "Affect");
    }

    private static PcAiInstruction Instruction(int index, int value, ushort encodedValue2,
        byte encodedCategory, string category, string? symbol) =>
        new(index, value, encodedValue2, 0, encodedCategory, category, symbol, null, null);
}
