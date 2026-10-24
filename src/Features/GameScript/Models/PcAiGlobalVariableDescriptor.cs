namespace JadeView;

internal sealed record PcAiGlobalVariableDescriptor(
    int Offset,
    uint PackedArrayCount,
    ushort Type,
    ushort Flags,
    string Name)
{
    internal int ArrayDimensions => (int)(PackedArrayCount >> 30);
    internal uint ArrayLength => PackedArrayCount & 0x3FFFFFFF;
}
