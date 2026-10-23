namespace JadeView;

internal static class PcActionKitFormat
{
    // Iris reads action-kit entries as two 32-bit fields in ordinary BF loading.
    // Four-byte entries are used only by its separate /BIN speed-mode path.
    internal const int EntrySize = sizeof(uint) * 2;
    internal const int ActionKeySize = sizeof(uint);
}
