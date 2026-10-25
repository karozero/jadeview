namespace JadeView;

/// <summary>
/// Evaluates a single integer operand/operator pair whose runtime inputs are both
/// statically known. It does not interpret the VM stack or follow branches.
/// </summary>
internal static class PcAiConstantIntegerEvaluator
{
    private const byte TypedValueCategory = 0x01;
    private const ushort IntegerLiteralType = 0x21;

    internal static bool TryReadLiteral(PcAiInstruction? instruction, out int value)
    {
        if (instruction is { EncodedCategory: TypedValueCategory, EncodedValue2: IntegerLiteralType })
        {
            value = instruction.Value;
            return true;
        }

        value = 0;
        return false;
    }

    internal static bool TryEvaluate(string? operation, int leftOperand, int rightOperand,
        out int result, out string expression)
    {
        expression = operation switch
        {
            "Plus" => $"{leftOperand} + {rightOperand}",
            "Minus" => $"{leftOperand} - {rightOperand}",
            "Mul" => $"{leftOperand} * {rightOperand}",
            "Div" => $"{leftOperand} / {rightOperand}",
            "Equal" => $"{leftOperand} == {rightOperand}",
            "Different" => $"{leftOperand} != {rightOperand}",
            "GreatEq" => $"{leftOperand} >= {rightOperand}",
            "LessEq" => $"{leftOperand} <= {rightOperand}",
            "Great" => $"{leftOperand} > {rightOperand}",
            "Less" => $"{leftOperand} < {rightOperand}",
            "OrOp" => $"{leftOperand} | {rightOperand}",
            "AndOp" => $"{leftOperand} & {rightOperand}",
            "XorOp" => $"{leftOperand} ^ {rightOperand}",
            _ => string.Empty
        };

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
            case "Equal":
                result = leftOperand == rightOperand ? 1 : 0;
                return true;
            case "Different":
                result = leftOperand != rightOperand ? 1 : 0;
                return true;
            case "GreatEq":
                result = leftOperand >= rightOperand ? 1 : 0;
                return true;
            case "LessEq":
                result = leftOperand <= rightOperand ? 1 : 0;
                return true;
            case "Great":
                result = leftOperand > rightOperand ? 1 : 0;
                return true;
            case "Less":
                result = leftOperand < rightOperand ? 1 : 0;
                return true;
            case "OrOp":
                result = leftOperand | rightOperand;
                return true;
            case "AndOp":
                result = leftOperand & rightOperand;
                return true;
            case "XorOp":
                result = leftOperand ^ rightOperand;
                return true;
            default:
                result = 0;
                expression = string.Empty;
                return false;
        }
    }
}
