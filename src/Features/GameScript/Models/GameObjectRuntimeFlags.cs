namespace JadeView;

internal static class GameObjectRuntimeFlags
{
    internal const uint Invisible = 0x00200000;
    internal const uint Inactive = 0x00400000;
    internal const uint ControlMask = 0xFFFF0000;
    internal const uint CustomBitsMask = 0x0000FF00;

    // Control setters change the high word of flags2; the low status word survives.
    internal static uint Apply(uint flags, uint setMask, uint clearMask) =>
        (flags & ~(clearMask & ControlMask)) | (setMask & ControlMask);
}
