namespace JadeView;

internal readonly record struct MaterialAtlasLayout(int Columns)
{
    internal const int TileSize = 40;
    internal const float Padding = 3.5f;
    internal const int MaximumSize = 2048;
    internal int Size => Columns * TileSize;
    internal float Edge => TileSize - 1 - 2 * Padding;

    internal static MaterialAtlasLayout Create(int triangles) => new((int)Math.Ceiling(Math.Sqrt(triangles)));
}
