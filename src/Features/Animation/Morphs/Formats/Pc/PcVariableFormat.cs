namespace JadeView;

internal static class PcVariableFormat
{
    internal const int BlockLengthSize = sizeof(int);
    internal const int DescriptorSize = 12;
    internal const int NameSize = 30;
    internal const int MinimumPayloadSize = BlockLengthSize * 3;
    internal const uint ScalarLength = 1;
    // Iris ai/ai_types.c:40-53 stores these scalars as raw 32-bit words.
    internal const ushort BooleanType = 32;
    internal const ushort IntegerType = 33;
    internal const ushort FloatType = 34;
    internal const ushort ObjectReferenceType = 40;
    internal const ushort KeyType = 45;
    internal const ushort PackedColorType = 46;

    internal static bool IsSupportedWordType(ushort type) => type is BooleanType or IntegerType
        or FloatType or ObjectReferenceType or KeyType or PackedColorType;
}
