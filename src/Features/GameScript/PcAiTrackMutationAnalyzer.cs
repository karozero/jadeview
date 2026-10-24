namespace JadeView;

/// <summary>
/// Finds literal AI-track mutation operands in serialized AI functions without evaluating branches,
/// the VM stack, or runtime dispatch.
/// </summary>
internal static class PcAiTrackMutationAnalyzer
{
    private const byte TypedValueCategory = 0x01;
    private const ushort IntegerLiteralType = 0x21;
    private const ushort PointerReferenceType = 0x27;
    private const int TrackCount = 5;

    internal static PcAiTrackMutation[] Analyze(PcSerializedAiFunction function,
        IReadOnlyDictionary<uint, string> linkedFunctionNames)
    {
        var mutations = new List<PcAiTrackMutation>();
        foreach (PcAiInstruction operationNode in function.Nodes.Where(IsTrackMutation))
        {
            switch (operationNode.SymbolName)
            {
                case "AITrackChange":
                    mutations.Add(AnalyzeTrackChange(function, operationNode, linkedFunctionNames));
                    break;
                case "AITrackStop":
                    mutations.Add(AnalyzeTrackStop(function, operationNode));
                    break;
                case "AITrackCurChange":
                case "AITrackCurChangeNow":
                    mutations.Add(AnalyzeCurrentTrackChange(function, operationNode, linkedFunctionNames));
                    break;
                case "AITrackCurStop":
                    mutations.Add(new(operationNode.Index, operationNode.SymbolName, null, null, null,
                        "CurrentTrackStop"));
                    break;
            }
        }
        return mutations.ToArray();
    }

    private static PcAiTrackMutation AnalyzeTrackChange(PcSerializedAiFunction function,
        PcAiInstruction operationNode, IReadOnlyDictionary<uint, string> linkedFunctionNames)
    {
        PcAiInstruction? functionArgument = PreviousNode(function, operationNode.Index, 1);
        PcAiInstruction? trackArgument = PreviousNode(function, operationNode.Index, 2);
        int? trackIndex = IsIntegerLiteral(trackArgument) ? trackArgument!.Value : null;
        PcAiTrackMutation target = ResolveFunctionTarget(operationNode, functionArgument, linkedFunctionNames,
            trackIndex is >= 0 and < TrackCount ? "TrackReplacementCandidate"
                : trackIndex is null ? "DynamicTrackIndex" : "TrackIndexOutOfRange");
        return target with { TrackIndex = trackIndex };
    }

    private static PcAiTrackMutation AnalyzeTrackStop(PcSerializedAiFunction function,
        PcAiInstruction operationNode)
    {
        PcAiInstruction? trackArgument = PreviousNode(function, operationNode.Index, 1);
        if (!IsIntegerLiteral(trackArgument))
            return new(operationNode.Index, operationNode.SymbolName!, null, null, null, "DynamicTrackIndex");

        int trackIndex = trackArgument!.Value;
        return new(operationNode.Index, operationNode.SymbolName!, trackIndex, null, null,
            trackIndex is >= 0 and < TrackCount ? "TrackStopCandidate" : "TrackIndexOutOfRange");
    }

    private static PcAiTrackMutation AnalyzeCurrentTrackChange(PcSerializedAiFunction function,
        PcAiInstruction operationNode, IReadOnlyDictionary<uint, string> linkedFunctionNames) =>
        ResolveFunctionTarget(operationNode, PreviousNode(function, operationNode.Index, 1),
            linkedFunctionNames, "CurrentTrackChangeCandidate");

    private static PcAiTrackMutation ResolveFunctionTarget(PcAiInstruction operationNode,
        PcAiInstruction? functionArgument, IReadOnlyDictionary<uint, string> linkedFunctionNames,
        string resolvedStatus)
    {
        if (functionArgument is not { EncodedCategory: TypedValueCategory, EncodedValue2: PointerReferenceType })
            return new(operationNode.Index, operationNode.SymbolName!, null, null, null,
                "IndirectOrNonLiteralTarget");

        uint targetKey = unchecked((uint)functionArgument.Value);
        if (targetKey is 0 or uint.MaxValue)
            return new(operationNode.Index, operationNode.SymbolName!, null, targetKey, null,
                "NullFunctionReference");

        bool targetIsLinked = linkedFunctionNames.TryGetValue(targetKey, out string? targetName);
        return new(operationNode.Index, operationNode.SymbolName!, null, targetKey,
            targetIsLinked ? targetName : null,
            targetIsLinked ? resolvedStatus : "PointerRefNotLinkedByModel");
    }

    private static PcAiInstruction? PreviousNode(PcSerializedAiFunction function, int nodeIndex, int distance)
    {
        int argumentIndex = nodeIndex - distance;
        return argumentIndex >= 0 ? function.Nodes[argumentIndex] : null;
    }

    private static bool IsIntegerLiteral(PcAiInstruction? node) =>
        node is { EncodedCategory: TypedValueCategory, EncodedValue2: IntegerLiteralType };

    private static bool IsTrackMutation(PcAiInstruction node) => node.SymbolName is
        "AITrackChange" or "AITrackStop" or "AITrackCurChange" or "AITrackCurChangeNow" or "AITrackCurStop";
}
