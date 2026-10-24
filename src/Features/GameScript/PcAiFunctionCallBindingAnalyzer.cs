namespace JadeView;

/// <summary>
/// Recognizes adjacent operands and narrow source-backed constant-return assignments.
/// It intentionally does not emulate the VM stack or control flow.
/// </summary>
internal static class PcAiFunctionCallBindingAnalyzer
{
    private const byte LocalScalarVariableCategory = 0x05;
    private const byte GlobalScalarVariableCategory = 0x14;
    private const byte TypedValueCategory = 0x01;
    private const ushort IntegerLiteralType = 0x21;
    private const string AffectKeyword = "Affect";

    internal static PcAiFunctionCallBinding[] Analyze(PcSerializedAiFunction function,
        Func<PcAiInstruction, string?> resolveVariableName)
    {
        var bindings = new List<PcAiFunctionCallBinding>();
        foreach (PcAiInstruction call in function.Nodes.Where(node => node.SymbolName != null
                     && PcAiFunctionContractCatalog.TryGet(node.SymbolName, out _)))
        {
            PcAiFunctionContractCatalog.TryGet(call.SymbolName!, out PcAiFunctionContract contract);
            PcAiInstruction? argument = call.Index > 0 ? function.Nodes[call.Index - 1] : null;
            if (contract.ArgumentCount == 0)
            {
                bindings.Add(AnalyzeConstantReturnCall(function, call, contract, resolveVariableName));
                continue;
            }

            bool isScalarVariableArgument = argument is { EncodedCategory: LocalScalarVariableCategory }
                or { EncodedCategory: GlobalScalarVariableCategory };
            string argumentDescription = isScalarVariableArgument
                ? resolveVariableName(argument!) ?? $"{argument!.CategoryName} at node {argument.Index}"
                : argument is { EncodedCategory: TypedValueCategory, EncodedValue2: 0x27 or 0x2A or 0x2B } typedArgument
                    ? $"typed reference 0x{typedArgument.Value:X8}"
                : IsIntegerLiteral(argument) && argument is { } integerArgument
                        ? $"integer literal {integerArgument.Value}"
                        : "nonliteral or unsupported operand sequence";

            PcAiInstruction? resultTarget = call.Index > 1 ? function.Nodes[call.Index - 2] : null;
            PcAiInstruction? affect = call.Index + 1 < function.Nodes.Length
                ? function.Nodes[call.Index + 1] : null;
            bool hasLinearResultAssignment = contract.ReturnType != "VOID"
                && (resultTarget is { EncodedCategory: LocalScalarVariableCategory }
                    or { EncodedCategory: GlobalScalarVariableCategory })
                && affect is { SymbolName: AffectKeyword };
            bool hasLinearArgument = isScalarVariableArgument
                || IsIntegerLiteral(argument);
            bool hasSupportedSignature = contract.ArgumentCount == 1;
            string status = hasSupportedSignature && hasLinearArgument
                && (contract.ReturnType == "VOID" || hasLinearResultAssignment)
                ? "LinearOperandPattern"
                : "UnresolvedStackBinding";
            string? resultDescription = hasLinearResultAssignment
                ? resolveVariableName(resultTarget!) ?? $"{resultTarget!.CategoryName} at node {resultTarget.Index}"
                : null;
            PcAiFunctionCallBinding? constantArgumentSource = isScalarVariableArgument
                ? FindAdjacentConstantAssignment(function, call, argument!, bindings)
                : null;

            bindings.Add(new(call.Index, call.SymbolName!, argument?.Index ?? -1, argumentDescription,
                hasLinearResultAssignment ? resultTarget!.Index : null, resultDescription,
                status, contract.Evidence, ResultAssignmentNodeIndex: hasLinearResultAssignment ? affect!.Index : null,
                ResolvedArgumentValue: constantArgumentSource?.ResolvedIntegerValue,
                ArgumentValueAssignmentNodeIndex: constantArgumentSource?.ResultAssignmentNodeIndex));
        }

        return bindings.ToArray();
    }

    private static PcAiFunctionCallBinding AnalyzeConstantReturnCall(PcSerializedAiFunction function,
        PcAiInstruction call, PcAiFunctionContract contract,
        Func<PcAiInstruction, string?> resolveVariableName)
    {
        PcAiInstruction? resultTarget = call.Index > 0 ? function.Nodes[call.Index - 1] : null;
        bool hasScalarTarget = resultTarget is { EncodedCategory: LocalScalarVariableCategory }
            or { EncodedCategory: GlobalScalarVariableCategory };
        string? resultDescription = hasScalarTarget
            ? resolveVariableName(resultTarget!) ?? $"{resultTarget!.CategoryName} at node {resultTarget.Index}"
            : null;

        if (hasScalarTarget && contract.ReturnType != "VOID" && contract.ConstantReturnValue is int constantValue)
        {
            if (HasAffectAt(function, call.Index + 1))
                return ResolvedBinding("ResolvedConstantExpression", constantValue, call.Index + 1);

            if (TryReadInteger(function, call.Index + 1, out int rightOperand)
                && TryResolveIntegerOperator(function, call.Index + 2, constantValue, rightOperand,
                    out int resolvedValue)
                && HasAffectAt(function, call.Index + 3))
                return ResolvedBinding("ResolvedConstantExpression", resolvedValue, call.Index + 3);
        }

        return new(call.Index, call.SymbolName!, -1, "no arguments",
            null, null, "UnresolvedStackBinding", contract.Evidence);

        PcAiFunctionCallBinding ResolvedBinding(string status, int value, int assignmentNodeIndex) =>
            new(call.Index, call.SymbolName!, -1, "no arguments", resultTarget!.Index,
                resultDescription, status, contract.Evidence, value, assignmentNodeIndex);
    }

    private static PcAiFunctionCallBinding? FindAdjacentConstantAssignment(PcSerializedAiFunction function,
        PcAiInstruction consumerCall, PcAiInstruction argument,
        IReadOnlyList<PcAiFunctionCallBinding> previousBindings)
    {
        if (consumerCall.Index < 2 || function.Nodes[consumerCall.Index - 2].SymbolName != AffectKeyword)
            return null;
        if (!IsScalarVariable(argument))
            return null;

        PcAiFunctionCallBinding? assignment = previousBindings.LastOrDefault(binding =>
            binding.Status == "ResolvedConstantExpression"
            && binding.ResolvedIntegerValue.HasValue
            && binding.ResultTargetNodeIndex is int targetIndex
            && binding.ResultAssignmentNodeIndex == consumerCall.Index - 2
            && NodeAt(function, targetIndex) is { } targetVariable
            && SameVariable(targetVariable, argument));
        if (assignment == null || assignment.ResultTargetNodeIndex is not int resultTargetIndex
            || assignment.ResultAssignmentNodeIndex is not int resultAssignmentIndex)
            return null;

        bool controlFlowCanBypassAssignment = function.Nodes.Any(node =>
            node.RelativeBranchTarget is int target
            && ((target > resultTargetIndex && target <= resultAssignmentIndex)
                || (target >= argument.Index && target <= consumerCall.Index)));
        return controlFlowCanBypassAssignment ? null : assignment;
    }

    private static PcAiInstruction? NodeAt(PcSerializedAiFunction function, int index) =>
        index >= 0 && index < function.Nodes.Length ? function.Nodes[index] : null;

    private static bool IsScalarVariable(PcAiInstruction? node) =>
        node is { EncodedCategory: LocalScalarVariableCategory }
        or { EncodedCategory: GlobalScalarVariableCategory };

    private static bool SameVariable(PcAiInstruction first, PcAiInstruction second) =>
        first.EncodedCategory == second.EncodedCategory && first.Value == second.Value;

    private static bool HasAffectAt(PcSerializedAiFunction function, int nodeIndex) =>
        nodeIndex >= 0 && nodeIndex < function.Nodes.Length
        && function.Nodes[nodeIndex].SymbolName == AffectKeyword;

    private static bool TryReadInteger(PcSerializedAiFunction function, int nodeIndex, out int value)
    {
        if (nodeIndex >= 0 && nodeIndex < function.Nodes.Length
            && IsIntegerLiteral(function.Nodes[nodeIndex])
            && function.Nodes[nodeIndex] is { } integerNode)
        {
            value = integerNode.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool IsIntegerLiteral(PcAiInstruction? node) =>
        node is { EncodedCategory: TypedValueCategory, EncodedValue2: IntegerLiteralType };

    private static bool TryResolveIntegerOperator(PcSerializedAiFunction function, int operatorNodeIndex,
        int leftOperand, int rightOperand, out int result)
    {
        string? operation = operatorNodeIndex >= 0 && operatorNodeIndex < function.Nodes.Length
            ? function.Nodes[operatorNodeIndex].SymbolName : null;
        switch (operation)
        {
            case "Plus":
                result = unchecked(leftOperand + rightOperand);
                return true;
            case "Minus":
                result = unchecked(leftOperand - rightOperand);
                return true;
            case "Mul":
                result = unchecked(leftOperand * rightOperand);
                return true;
            case "Div" when rightOperand != 0
                && !(leftOperand == int.MinValue && rightOperand == -1):
                result = leftOperand / rightOperand;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
