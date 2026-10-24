namespace JadeView;

/// <summary>
/// Finds only source-backed AIExecute calls whose immediate argument is an encoded pointerref constant.
/// This is a local serialization pattern check, not VM stack or control-flow execution.
/// </summary>
internal static class PcAiDirectFunctionCallAnalyzer
{
    internal static PcAiDirectFunctionCall[] Analyze(PcSerializedAiFunction function,
        IReadOnlyDictionary<uint, string> linkedFunctionNames)
    {
        var calls = new List<PcAiDirectFunctionCall>();
        foreach (PcAiInstruction callNode in function.Nodes.Where(node => node.SymbolName == "AIExecute"))
        {
            PcAiInstruction? argument = callNode.Index > 0 ? function.Nodes[callNode.Index - 1] : null;
            bool hasDirectReference = argument is { EncodedCategory: 0x01, EncodedValue2: 0x27 };
            if (!hasDirectReference)
            {
                calls.Add(new(callNode.Index, "AIExecute", null, null, null,
                    "IndirectOrNonLiteralArgument"));
                continue;
            }

            uint targetKey = unchecked((uint)argument!.Value);
            bool targetLinked = linkedFunctionNames.TryGetValue(targetKey, out string? targetName);
            calls.Add(new(callNode.Index, "AIExecute", argument.Index, targetKey,
                targetLinked ? targetName : null,
                targetLinked ? "ResolvedLinkedFunction" : "PointerRefNotLinkedByModel"));
        }
        return calls.ToArray();
    }
}
