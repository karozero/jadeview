namespace JadeView;

/// <summary>
/// Finds repeated constant writes to two global integer arrays in serialized AI node streams.
/// This is a structural candidate detector; it does not execute stack expressions or branches.
/// </summary>
internal static class PcAiConstantArrayTableAnalyzer
{
    private const int MinimumPairs = 4;

    internal static PcAiConstantArrayTable[] Analyze(PcSerializedAiFunction function)
    {
        PcAiInstruction[] nodes = function.Nodes;
        Dictionary<int, ArrayWrite> writes = FindConstantArrayWrites(nodes);
        ArrayWrite[] orderedWrites = writes.Values.OrderBy(write => write.NodeIndex).ToArray();
        var candidates = new List<PcAiConstantArrayTable>();

        foreach (ArrayWrite firstWrite in orderedWrites)
        {
            ArrayWrite[] followingEntries = orderedWrites.Where(write =>
                    write.NodeIndex > firstWrite.NodeIndex && write.ArrayOffset == firstWrite.ArrayOffset)
                .Take(1).ToArray();
            if (followingEntries.Length == 0) continue;
            ArrayWrite nextWrite = followingEntries[0];

            int nodeStride = nextWrite.NodeIndex - firstWrite.NodeIndex;
            if (nodeStride <= 0) continue;

            foreach (ArrayWrite secondWrite in orderedWrites.Where(write =>
                         write.NodeIndex > firstWrite.NodeIndex
                         && write.NodeIndex < nextWrite.NodeIndex
                         && write.ArrayOffset != firstWrite.ArrayOffset))
            {
                int? incrementNode = FindIncrementNode(nodes, secondWrite.NodeIndex, nextWrite.NodeIndex);
                if (incrementNode is not { } incrementIndex) continue;
                int blockLength = incrementIndex - firstWrite.NodeIndex + 1;
                if (!HasRepeatedInitializerShape(nodes, firstWrite.NodeIndex,
                        nextWrite.NodeIndex, blockLength))
                    continue;

                PcAiConstantArrayTable? table = BuildRepeatedTable(nodes, writes,
                    firstWrite, secondWrite, nodeStride, blockLength);
                if (table is not null) candidates.Add(table);
            }
        }

        PcAiConstantArrayTable[] distinctCandidates = candidates
            .GroupBy(table => (table.FirstArrayOffset, table.SecondArrayOffset,
                table.FirstNodeIndex, table.NodeStride))
            .Select(group => group.First())
            .OrderByDescending(table => table.Pairs.Length)
            .ThenBy(table => table.FirstNodeIndex)
            .ToArray();
        return distinctCandidates
            .Where(table => !distinctCandidates.Any(other =>
                other.FirstArrayOffset == table.FirstArrayOffset
                && other.SecondArrayOffset == table.SecondArrayOffset
                && other.NodeStride == table.NodeStride
                && other.Pairs.Length > table.Pairs.Length
                && other.FirstNodeIndex <= table.FirstNodeIndex
                && TableEndNode(other) >= TableEndNode(table)))
            .ToArray();
    }

    private static Dictionary<int, ArrayWrite> FindConstantArrayWrites(PcAiInstruction[] nodes)
    {
        var writes = new Dictionary<int, ArrayWrite>();
        for (int nodeIndex = 0; nodeIndex < nodes.Length - 2; nodeIndex++)
        {
            PcAiInstruction array = nodes[nodeIndex];
            PcAiInstruction value = nodes[nodeIndex + 1];
            PcAiInstruction affect = nodes[nodeIndex + 2];
            bool isConstantIntegerWrite = array.CategoryName == "Global array"
                && value.CategoryName == "Type"
                && value.EncodedValue2 == 0x21
                && affect.SymbolName == "Affect";
            if (isConstantIntegerWrite)
                writes[nodeIndex] = new(array.Value, value.Value, nodeIndex);
        }
        return writes;
    }

    private static PcAiConstantArrayTable? BuildRepeatedTable(PcAiInstruction[] nodes,
        IReadOnlyDictionary<int, ArrayWrite> writes, ArrayWrite firstWrite,
        ArrayWrite secondWrite, int nodeStride, int blockLength)
    {
        int pairOffset = secondWrite.NodeIndex - firstWrite.NodeIndex;
        var pairs = new List<PcAiConstantArrayPair>();
        int repetition = 0;
        while (true)
        {
            int firstIndex = firstWrite.NodeIndex + repetition * nodeStride;
            int secondIndex = firstIndex + pairOffset;
            if (!writes.TryGetValue(firstIndex, out ArrayWrite currentFirst)
                || !writes.TryGetValue(secondIndex, out ArrayWrite currentSecond)
                || currentFirst.ArrayOffset != firstWrite.ArrayOffset
                || currentSecond.ArrayOffset != secondWrite.ArrayOffset
                || !HasRepeatedInitializerShape(nodes, firstWrite.NodeIndex, firstIndex, blockLength))
                break;

            int nextFirstIndex = firstIndex + nodeStride;
            int? incrementNode = FindIncrementNode(nodes, secondIndex, nextFirstIndex);
            if (incrementNode is not { } incrementIndex
                || incrementIndex - firstIndex + 1 != blockLength)
                break;
            pairs.Add(new(currentFirst.ConstantValue, unchecked((uint)currentSecond.ConstantValue)));
            repetition++;
        }

        return pairs.Count >= MinimumPairs
            ? new(firstWrite.ArrayOffset, secondWrite.ArrayOffset,
                firstWrite.NodeIndex, nodeStride, pairs.ToArray())
            : null;
    }

    private static bool HasRepeatedInitializerShape(PcAiInstruction[] nodes, int firstStart,
        int secondStart, int blockLength)
    {
        if (firstStart == secondStart) return true;
        if (firstStart < 0 || secondStart < 0 || blockLength <= 0
            || firstStart + blockLength > nodes.Length
            || secondStart + blockLength > nodes.Length)
            return false;

        for (int offset = 0; offset < blockLength; offset++)
        {
            PcAiInstruction first = nodes[firstStart + offset];
            PcAiInstruction second = nodes[secondStart + offset];
            if (first.CategoryName != second.CategoryName
                || first.EncodedCategory != second.EncodedCategory
                || first.EncodedValue2 != second.EncodedValue2
                || first.Flags != second.Flags
                || first.SymbolName != second.SymbolName)
                return false;

            bool firstIsWrittenLiteral = IsArrayWriteLiteral(nodes, firstStart + offset);
            bool secondIsWrittenLiteral = IsArrayWriteLiteral(nodes, secondStart + offset);
            if (firstIsWrittenLiteral != secondIsWrittenLiteral
                || (!firstIsWrittenLiteral && first.Value != second.Value))
                return false;
        }
        return true;
    }

    private static bool IsArrayWriteLiteral(PcAiInstruction[] nodes, int nodeIndex) =>
        nodeIndex > 0 && nodeIndex + 1 < nodes.Length
        && nodes[nodeIndex - 1].CategoryName == "Global array"
        && nodes[nodeIndex].CategoryName == "Type"
        && nodes[nodeIndex].EncodedValue2 == 0x21
        && nodes[nodeIndex + 1].SymbolName == "Affect";

    private static int? FindIncrementNode(PcAiInstruction[] nodes, int afterAssignment, int beforeNextEntry)
    {
        int start = Math.Clamp(afterAssignment + 3, 0, nodes.Length);
        int end = Math.Clamp(beforeNextEntry, 0, nodes.Length);
        for (int nodeIndex = start; nodeIndex < end; nodeIndex++)
            if (nodes[nodeIndex].SymbolName == "IncAffect") return nodeIndex;
        return null;
    }

    private static int TableEndNode(PcAiConstantArrayTable table) =>
        table.FirstNodeIndex + table.Pairs.Length * table.NodeStride;

    private readonly record struct ArrayWrite(int ArrayOffset, int ConstantValue, int NodeIndex);
}
