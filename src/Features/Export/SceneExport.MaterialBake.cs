using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private BakedMaterialTexture? BakeLayeredMaterial(SceneObject sceneObject, Geometry geometry,
        Geometry.Part part, Vector3[] positions, List<JadeMaterialLayer> layers)
    {
        if (!options.Textures || !IsMaskedOverlay(layers) || sceneObject.RliColors.Length > 0) return null;
        bool needsStoredUvs = layers.Any(layer => layer.UvMode == 0);
        bool validStoredUvs = part.UvIndices.Length == part.VertexIndices.Length &&
            part.UvIndices.All(index => index >= 0 && index < geometry.Uvs.Length);
        if (needsStoredUvs && !validStoredUvs) return null;
        foreach (var layer in layers.Where(layer => layer.UvMode == 6))
        {
            int space = layer.Custom >> 6 & 3;
            if (space is 1 or 2 || space == 3 && !TryBoneProjection(sceneObject, layer, out _)) return null;
        }
        var sources = new List<BakeTextureSource>();
        foreach (var layer in layers)
        {
            if (!resolver.TryResolveTexture(layer.Texture, out var texture) || texture.Width <= 0 || texture.Height <= 0) return null;
            sources.Add(new(texture.Width, texture.Height, Preview.DecodeTga(texture.ToTextureAsset()), layer.Flags));
        }
        const int tileSize = 40;
        int triangles = part.VertexIndices.Length / 3;
        int columns = (int)Math.Ceiling(Math.Sqrt(triangles));
        int size = columns * tileSize;
        if (size > 2048) return null;
        var rgba = new byte[size * size * 4];
        var cornerUvs = new Vector2[part.VertexIndices.Length];
        for (int triangle = 0; triangle < triangles; triangle++)
            BakeTriangle(triangle, columns, tileSize, size, rgba, cornerUvs, sources,
                LayerCornerUvs(sceneObject, geometry, part, positions, layers, triangle), layers[1].OverrideAlpha);
        warnings.Add($"'{sceneObject.Name}' masked material layers are baked into a triangle atlas at the selected pose; animated UV projection remains frozen.");
        return new(size, rgba, cornerUvs);
    }

    private static bool IsMaskedOverlay(List<JadeMaterialLayer> layers)
    {
        if (layers.Count != 3) return false;
        var color = layers[0]; var mask = layers[1]; var overlay = layers[2];
        bool opaqueColor = color.WriteRgb && color.WriteAlpha && color.Blend == 0 && color.ColorMode is 0 or 6;
        bool alphaMask = !mask.WriteRgb && mask.WriteAlpha && mask.Blend == 0 && mask.ColorMode == 0 && (mask.Flags & 0x400) != 0;
        bool maskedOverlay = overlay.WriteRgb && overlay.Blend == 3 && (overlay.Flags & 0x80) != 0 && overlay.ColorMode is 0 or 6;
        bool stableUvs = layers.All(layer => layer.UvMode is 0 or 6 && (layer.Flags & 0xC0000010) == 0);
        return opaqueColor && alphaMask && maskedOverlay && stableUvs;
    }

    private Vector2[][] LayerCornerUvs(SceneObject sceneObject, Geometry geometry, Geometry.Part part,
        Vector3[] positions, List<JadeMaterialLayer> layers, int triangle)
    {
        return layers.Select(layer => Enumerable.Range(triangle * 3, 3).Select(corner =>
        {
            int uvIndex = corner < part.UvIndices.Length ? part.UvIndices[corner] : -1;
            var uv = uvIndex >= 0 && uvIndex < geometry.Uvs.Length ? geometry.Uvs[uvIndex] : Vector2.Zero;
            return ExportUv(sceneObject, positions[part.VertexIndices[corner]], uv, layer);
        }).ToArray()).ToArray();
    }

    private static void BakeTriangle(int triangle, int columns, int tileSize, int size, byte[] rgba,
        Vector2[] cornerUvs, List<BakeTextureSource> sources, Vector2[][] uvs, float maskAlpha)
    {
        int originX = triangle % columns * tileSize, originY = triangle / columns * tileSize;
        const float padding = 3.5f;
        float edge = tileSize - 1 - 2 * padding;
        cornerUvs[triangle * 3] = new((originX + padding) / size, (originY + padding) / size);
        cornerUvs[triangle * 3 + 1] = new((originX + padding + edge) / size, (originY + padding) / size);
        cornerUvs[triangle * 3 + 2] = new((originX + padding) / size, (originY + padding + edge) / size);
        for (int row = 0; row < tileSize; row++)
        for (int column = 0; column < tileSize; column++)
        {
            float second = Math.Max(0, (column + .5f - padding) / edge);
            float third = Math.Max(0, (row + .5f - padding) / edge);
            float first = Math.Max(0, 1 - second - third);
            var weights = new Vector3(first, second, third) / (first + second + third);
            var color = sources[0].Sample(Interpolate(uvs[0], weights));
            float alpha = sources[1].Sample(Interpolate(uvs[1], weights)).W * maskAlpha;
            var overlay = sources[2].Sample(Interpolate(uvs[2], weights));
            var result = color * alpha + overlay * (1 - alpha);
            int offset = ((originY + row) * size + originX + column) * 4;
            rgba[offset] = Quantize(result.X); rgba[offset + 1] = Quantize(result.Y);
            rgba[offset + 2] = Quantize(result.Z); rgba[offset + 3] = 255;
        }
    }

    private static Vector2 Interpolate(Vector2[] values, Vector3 weights) =>
        values[0] * weights.X + values[1] * weights.Y + values[2] * weights.Z;
    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
