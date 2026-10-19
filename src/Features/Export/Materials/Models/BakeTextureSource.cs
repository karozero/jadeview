using System.Numerics;

namespace JadeView;

internal sealed record BakeTextureSource(int Width, int Height, byte[] Rgba, uint Flags)
{
    internal Vector4 Sample(Vector2 uv)
    {
        float horizontal = uv.X * Width - .5f, vertical = uv.Y * Height - .5f;
        if ((Flags & 4) == 0)
            return Pixel((int)MathF.Floor(horizontal + .5f), (int)MathF.Floor(vertical + .5f));
        int left = (int)MathF.Floor(horizontal), top = (int)MathF.Floor(vertical);
        var upper = Vector4.Lerp(Pixel(left, top), Pixel(left + 1, top), horizontal - left);
        var lower = Vector4.Lerp(Pixel(left, top + 1), Pixel(left + 1, top + 1), horizontal - left);
        return Vector4.Lerp(upper, lower, vertical - top);
    }

    private Vector4 Pixel(int horizontal, int vertical)
    {
        int column = Address(horizontal, Width, (Flags & 1) != 0);
        int row = Address(vertical, Height, (Flags & 2) != 0);
        int offset = (row * Width + column) * 4;
        return new Vector4(Rgba[offset], Rgba[offset + 1], Rgba[offset + 2], Rgba[offset + 3]) / 255;
    }

    private static int Address(int value, int size, bool repeat) =>
        repeat ? (value % size + size) % size : Math.Clamp(value, 0, size - 1);
}
