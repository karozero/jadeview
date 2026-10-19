using System.Numerics;

namespace JadeView;

// CPU rasterization only. UV projection/resolution and glTF writing stay outside.
internal sealed class MaterialAtlasBaker(MaterialAtlasLayout layout, IReadOnlyList<BakeTextureSource> sources,
    float alphaScale, MaterialLayerComposition composition)
{
    private readonly byte[] rgba = new byte[layout.Size * layout.Size * 4];

    internal static BakedMaterialTexture? Bake(int triangles, IReadOnlyList<BakeTextureSource> sources,
        Func<int, Vector2[][]> triangleUvs, float alphaScale, MaterialLayerComposition composition)
    {
        var layout = MaterialAtlasLayout.Create(triangles);
        if (layout.Size > MaterialAtlasLayout.MaximumSize) return null;
        var baker = new MaterialAtlasBaker(layout, sources, alphaScale, composition);
        var cornerUvs = new Vector2[triangles * 3];
        for (int triangle = 0; triangle < triangles; triangle++)
            baker.BakeTriangle(triangle, triangleUvs(triangle), cornerUvs);
        return new(layout.Size, baker.rgba, cornerUvs);
    }

    private void BakeTriangle(int triangle, Vector2[][] uvs, Vector2[] cornerUvs)
    {
        int originX = triangle % layout.Columns * MaterialAtlasLayout.TileSize;
        int originY = triangle / layout.Columns * MaterialAtlasLayout.TileSize;
        float padding = MaterialAtlasLayout.Padding;
        cornerUvs[triangle * 3] = new((originX + padding) / layout.Size, (originY + padding) / layout.Size);
        cornerUvs[triangle * 3 + 1] = new((originX + padding + layout.Edge) / layout.Size, (originY + padding) / layout.Size);
        cornerUvs[triangle * 3 + 2] = new((originX + padding) / layout.Size, (originY + padding + layout.Edge) / layout.Size);
        for (int row = 0; row < MaterialAtlasLayout.TileSize; row++)
        for (int column = 0; column < MaterialAtlasLayout.TileSize; column++)
        {
            var weights = Barycentric(column, row);
            var color = Blend(uvs, weights);
            int offset = ((originY + row) * layout.Size + originX + column) * 4;
            rgba[offset] = Quantize(color.X);
            rgba[offset + 1] = Quantize(color.Y);
            rgba[offset + 2] = Quantize(color.Z);
            rgba[offset + 3] = 255;
        }
    }

    private Vector3 Barycentric(int column, int row)
    {
        float second = Math.Max(0, (column + .5f - MaterialAtlasLayout.Padding) / layout.Edge);
        float third = Math.Max(0, (row + .5f - MaterialAtlasLayout.Padding) / layout.Edge);
        float first = Math.Max(0, 1 - second - third);
        return new Vector3(first, second, third) / (first + second + third);
    }

    private Vector4 Blend(Vector2[][] uvs, Vector3 weights)
    {
        var color = sources[0].Sample(Interpolate(uvs[0], weights));
        var secondLayer = sources[1].Sample(Interpolate(uvs[1], weights));
        float alpha = secondLayer.W * alphaScale;
        if (composition == MaterialLayerComposition.SourceAlphaOverlay)
            return color * (1 - alpha) + secondLayer * alpha;
        var overlay = sources[2].Sample(Interpolate(uvs[2], weights));
        return color * alpha + overlay * (1 - alpha);
    }

    private static Vector2 Interpolate(Vector2[] values, Vector3 weights) =>
        values[0] * weights.X + values[1] * weights.Y + values[2] * weights.Z;
    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
