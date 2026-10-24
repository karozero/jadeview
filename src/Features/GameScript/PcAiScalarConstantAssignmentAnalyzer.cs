namespace JadeView;

/// <summary>Finds serialized scalar = integer-literal Affect patterns without evaluating execution.</summary>
internal static class PcAiScalarConstantAssignmentAnalyzer
{
    private const byte LocalScalarVariableCategory = 0x05;
    private const byte GlobalScalarVariableCategory = 0x14;
    private const byte TypedValueCategory = 0x01;
    private const ushort IntegerLiteralType = 0x21;
    private const string AffectKeyword = "Affect";

    internal static PcAiScalarConstantAssignment[] Analyze(PcSerializedAiFunction function,
        Func<PcAiInstruction, string?> resolveVariableName)
    {
        var assignments = new List<PcAiScalarConstantAssignment>();
        for (int targetIndex = 0; targetIndex + 2 < function.Nodes.Length; targetIndex++)
        {
            PcAiInstruction target = function.Nodes[targetIndex];
            PcAiInstruction value = function.Nodes[targetIndex + 1];
            PcAiInstruction affect = function.Nodes[targetIndex + 2];
            bool isScalarTarget = target.EncodedCategory is LocalScalarVariableCategory or GlobalScalarVariableCategory;
            bool isIntegerLiteral = value.EncodedCategory == TypedValueCategory
                && value.EncodedValue2 == IntegerLiteralType;
            if (!isScalarTarget || !isIntegerLiteral || affect.SymbolName != AffectKeyword)
                continue;

            string targetName = resolveVariableName(target)
                ?? $"{target.CategoryName} at node {target.Index}";
            assignments.Add(new(target.Index, targetName, value.Index, value.Value, affect.Index));
        }

        return assignments.ToArray();
    }
}
