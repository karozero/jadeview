namespace JadeView;

internal static class MaterialRliAlphaPolicy
{
    internal static bool UsesRliAlpha(JadeMaterialLayer layer, JadeMaterialLayer? nextLayer)
    {
        bool usesLocalAlpha = (layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0;
        if (!usesLocalAlpha && (layer.Blend is 1 or 2
            || (layer.Flags & JadeMaterialFlags.AlphaTest) != 0)) return true;

        bool writesDestinationAlpha = nextLayer?.Blend is 3 or 4;
        return !usesLocalAlpha && layer.Blend is 0 or 7 && writesDestinationAlpha;
    }
}
