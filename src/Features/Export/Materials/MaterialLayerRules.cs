namespace JadeView;

// Pure format rules: no actor identities, resolver, image buffers or glTF state.
internal static class MaterialLayerRules
{
    internal static MaterialLayerComposition Composition(IReadOnlyList<JadeMaterialLayer> layers)
    {
        if (IsMaskedOverlay(layers)) return MaterialLayerComposition.DestinationAlphaMask;
        if (IsAlphaOverlay(layers)) return MaterialLayerComposition.SourceAlphaOverlay;
        if (IsReflectionComposition(layers)) return MaterialLayerComposition.ReflectionDetail;
        return MaterialLayerComposition.Unsupported;
    }

    internal static ExportColorLayerSelection SelectColor(List<JadeMaterialLayer> layers)
    {
        var primary = layers.FirstOrDefault(layer => layer.WriteRgb && layer.Blend is 0 or 7 &&
            layer.Texture is not (0 or uint.MaxValue)) ?? layers.FirstOrDefault(layer =>
            layer.WriteRgb && layer.Texture is not (0 or uint.MaxValue)) ?? layers.FirstOrDefault(layer => layer.WriteRgb);
        bool reflectionBase = primary is { Blend: 0, UvMode: 2 or 3 };
        if (!reflectionBase) return new(primary);
        int primaryIndex = layers.IndexOf(primary!);
        var detail = layers.Skip(primaryIndex + 1).FirstOrDefault(IsReflectionDetail);
        if (detail == null) return new(primary);
        // Detail alpha is a reflection coefficient over the opaque base.
        const uint blendAndAlphaFlags = 0xF0000 | JadeMaterialFlags.OverrideAlpha | JadeMaterialFlags.InverseBlend;
        return new(detail with { Flags = detail.Flags & ~blendAndAlphaFlags }, OmitsReflection: true);
    }

    private static bool IsReflectionDetail(JadeMaterialLayer layer)
    {
        bool additiveDetail = layer.Blend == 2 && (layer.Flags & JadeMaterialFlags.InverseBlend) != 0;
        bool stableMapping = layer.UvMode is 0 or 6;
        bool plainColor = layer.ColorMode is 0 or 3 or 5 or 6;
        bool noAlphaTest = (layer.Flags & JadeMaterialFlags.AlphaTest) == 0;
        bool hasTexture = layer.Texture is not (0 or uint.MaxValue);
        return layer.WriteRgb && additiveDetail && stableMapping && plainColor && noAlphaTest && hasTexture;
    }

    private static bool IsReflectionComposition(IReadOnlyList<JadeMaterialLayer> layers)
    {
        if (layers.Count != 2) return false;
        var reflection = layers[0];
        bool opaqueReflection = reflection.WriteRgb && reflection.Blend == 0 && reflection.UvMode == 2;
        bool plainReflectionColor = reflection.ColorMode is 0 or 2 or 6;
        bool staticTransforms = layers.All(layer => (layer.Flags & (JadeMaterialFlags.Scrolling | JadeMaterialFlags.AlphaTest)) == 0);
        return opaqueReflection && plainReflectionColor && staticTransforms && IsReflectionDetail(layers[1]);
    }

    private static bool StableUvs(IReadOnlyList<JadeMaterialLayer> layers) =>
        layers.All(layer => layer.UvMode is 0 or 6 && (layer.Flags & (JadeMaterialFlags.Scrolling | JadeMaterialFlags.AlphaTest)) == 0);

    private static bool IsMaskedOverlay(IReadOnlyList<JadeMaterialLayer> layers)
    {
        if (layers.Count != 3) return false;
        var color = layers[0];
        var mask = layers[1];
        var overlay = layers[2];
        bool opaqueColor = color.WriteRgb && color.WriteAlpha && color.Blend == 0 && color.ColorMode is 0 or 6;
        bool alphaMask = !mask.WriteRgb && mask.WriteAlpha && mask.Blend == 0 && mask.ColorMode == 0 && (mask.Flags & JadeMaterialFlags.OverrideAlpha) != 0;
        bool maskedOverlay = overlay.WriteRgb && overlay.Blend == 3 && (overlay.Flags & JadeMaterialFlags.InverseBlend) != 0 && overlay.ColorMode is 0 or 6;
        return opaqueColor && alphaMask && maskedOverlay && StableUvs(layers);
    }

    private static bool IsAlphaOverlay(IReadOnlyList<JadeMaterialLayer> layers)
    {
        if (layers.Count != 2) return false;
        var color = layers[0];
        var overlay = layers[1];
        bool opaqueColor = color.WriteRgb && color.WriteAlpha && color.Blend == 0;
        bool sourceAlpha = overlay.WriteRgb && overlay.WriteAlpha && overlay.Blend == 1 && (overlay.Flags & JadeMaterialFlags.InverseBlend) == 0;
        bool plainColors = layers.All(layer => layer.ColorMode is 0 or 6);
        return opaqueColor && sourceAlpha && plainColors && StableUvs(layers);
    }
}
