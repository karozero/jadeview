namespace JadeView;

internal sealed record PcAiGlobalVariableDefault(string Name, int Offset, ushort Type,
    int ValueBlockOffset, int DefaultBufferLength, int[]? IntegerValues, string ValueStatus);
