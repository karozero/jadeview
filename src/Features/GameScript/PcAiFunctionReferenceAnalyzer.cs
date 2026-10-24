namespace JadeView;

/// <summary>
/// Resolves two source-confirmed serialized function-reference operations without evaluating arguments.
/// </summary>
internal static class PcAiFunctionReferenceAnalyzer
{
    internal static PcAiFunctionReference[] Analyze(PcSerializedAiFunction function,
        IReadOnlyDictionary<uint, string> linkedFunctionNames)
    {
        var references = PcAiDirectFunctionCallAnalyzer.Analyze(function, linkedFunctionNames)
            .Select(call => new PcAiFunctionReference(call.CallNodeIndex, call.EngineFunction,
                call.ReferenceNodeIndex, call.TargetKey, call.TargetName, call.Status))
            .ToList();

        foreach (PcAiInstruction node in function.Nodes.Where(node => node.SymbolName == "AITrackCurChangeNow"))
        {
            PcAiInstruction? argument = node.Index > 0 ? function.Nodes[node.Index - 1] : null;
            bool hasDirectReference = argument is { EncodedCategory: 0x01, EncodedValue2: 0x27 };
            if (!hasDirectReference)
            {
                references.Add(new(node.Index, "AITrackCurChangeNow", null, null, null,
                    "IndirectOrNonLiteralArgument"));
                continue;
            }

            uint targetKey = unchecked((uint)argument!.Value);
            if (targetKey is 0 or uint.MaxValue)
            {
                references.Add(new(node.Index, "AITrackCurChangeNow", argument.Index, targetKey, null,
                    "NullFunctionReference"));
                continue;
            }

            bool targetLinked = linkedFunctionNames.TryGetValue(targetKey, out string? targetName);
            references.Add(new(node.Index, "AITrackCurChangeNow", argument.Index, targetKey,
                targetLinked ? targetName : null,
                targetLinked ? "ResolvedLinkedFunction" : "PointerRefNotLinkedByModel"));
        }

        return references.OrderBy(reference => reference.NodeIndex)
            .ThenBy(reference => reference.Operation, StringComparer.Ordinal).ToArray();
    }
}
