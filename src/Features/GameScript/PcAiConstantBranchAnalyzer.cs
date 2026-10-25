namespace JadeView;

/// <summary>
/// Resolves a constant-return engine call, an optional integer literal operation/Not,
/// and a conditional jump.
/// It does not evaluate variables, general expressions, or function execution.
/// </summary>
internal static class PcAiConstantBranchAnalyzer
{
    private const string NotKeyword = "Not";
    private const string JumpFalseKeyword = "JumpFalseSP";
    private const string JumpTrueKeyword = "JumpTrueSP";

    internal static PcAiConstantBranch[] Analyze(PcSerializedAiFunction function)
    {
        var branches = new List<PcAiConstantBranch>();
        foreach (PcAiInstruction call in function.Nodes.Where(node => node.SymbolName != null
                     && PcAiFunctionContractCatalog.TryGet(node.SymbolName, out _)))
        {
            PcAiFunctionContractCatalog.TryGet(call.SymbolName!, out PcAiFunctionContract contract);
            if (contract.ArgumentCount != 0 || contract.ConstantReturnValue is not int constantValue)
                continue;

            bool conditionIsTrue = constantValue != 0;
            string conditionDescription = $"{call.SymbolName}() = {constantValue}";
            int conditionNodeIndex = call.Index + 1;
            if (PcAiConstantIntegerEvaluator.TryReadLiteral(NodeAt(function, conditionNodeIndex),
                    out int rightOperand)
                && PcAiConstantIntegerEvaluator.TryEvaluate(SymbolAt(function, conditionNodeIndex + 1),
                    constantValue, rightOperand, out int expressionValue, out string expression))
            {
                conditionIsTrue = expressionValue != 0;
                conditionDescription = $"{call.SymbolName}() = {constantValue}; {expression} = {expressionValue}";
                conditionNodeIndex += 2;
            }

            if (SymbolAt(function, conditionNodeIndex) == NotKeyword)
            {
                conditionIsTrue = !conditionIsTrue;
                conditionDescription += $"; {NotKeyword} => {FormatBoolean(conditionIsTrue)}";
                conditionNodeIndex++;
            }

            PcAiInstruction? jump = NodeAt(function, conditionNodeIndex);
            if (jump?.SymbolName is not (JumpFalseKeyword or JumpTrueKeyword)
                || jump.RelativeBranchTarget is not int branchTarget
                || branchTarget < 0 || branchTarget >= function.Nodes.Length)
                continue;

            bool jumpsWhenConditionMatches = jump.SymbolName == JumpTrueKeyword;
            bool takesBranch = jumpsWhenConditionMatches == conditionIsTrue;
            int fallthrough = jump.Index + 1;
            int chosenNode = takesBranch ? branchTarget : fallthrough;
            string edge = takesBranch ? "branch target" : "fallthrough";
            branches.Add(new(call.Index, call.SymbolName!, jump.Index, conditionDescription,
                conditionIsTrue, branchTarget, fallthrough, chosenNode, edge,
                $"{contract.Evidence}; external/irisEng/ai/ai_keywords.c:270-577,576-607; "
                + "external/jaded/src/libraries/AIinterp/Sources/Keywords/AIkeywords.c:387-400,466-483"));
        }

        return branches.ToArray();
    }

    private static PcAiInstruction? NodeAt(PcSerializedAiFunction function, int index) =>
        index >= 0 && index < function.Nodes.Length ? function.Nodes[index] : null;

    private static string? SymbolAt(PcSerializedAiFunction function, int index) =>
        NodeAt(function, index)?.SymbolName;

    private static string FormatBoolean(bool value) => value ? "true" : "false";
}
