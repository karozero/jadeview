namespace JadeView;

internal static class JadeMaterialFlags
{
    internal const uint AlphaTest = 0x10;
    internal const uint InverseBlend = 0x80;
    internal const uint OverrideAlpha = 0x400;
    internal const uint Disabled = 0x800;
    internal const uint ScrollU = 0x40000000;
    internal const uint ScrollV = 0x80000000;
    internal const uint Scrolling = ScrollU | ScrollV;
    internal const uint BlendMask = 0xF0000;
}
