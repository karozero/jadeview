namespace JadeView;

internal sealed record PcAiConstantArrayTable(
    int FirstArrayOffset,
    int SecondArrayOffset,
    int FirstNodeIndex,
    int NodeStride,
    PcAiConstantArrayPair[] Pairs);

internal readonly record struct PcAiConstantArrayPair(int FirstValue, uint SecondValue);
