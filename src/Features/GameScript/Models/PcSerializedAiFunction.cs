namespace JadeView;

internal sealed record PcSerializedAiFunction(
    int StackOffsetCount,
    PcAiInstruction[] Nodes,
    PcAiInstruction[] DebugNodes,
    byte[] LocalStringConstants,
    PcAiLocalVariable[] LocalVariables,
    byte[] TrailingBytes);

internal sealed record PcAiInstruction(
    int Index,
    int Value,
    ushort EncodedValue2,
    byte Flags,
    byte EncodedCategory,
    string CategoryName,
    string? SymbolName,
    int? RelativeBranchTarget,
    string? BranchDescription);

internal sealed record PcAiLocalVariable(int Offset, int Type, string Name);
