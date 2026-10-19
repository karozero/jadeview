namespace JadeView;

internal static class PcVariableFormat
{
    internal const int BlockLengthSize = sizeof(int);
    internal const int DescriptorSize = 12;
    internal const int NameSize = 30;
    internal const int MinimumPayloadSize = BlockLengthSize * 3;
    internal const uint ScalarLength = 1;
    internal const ushort IntegerType = 33;
    internal const ushort ObjectReferenceType = 40;
}
