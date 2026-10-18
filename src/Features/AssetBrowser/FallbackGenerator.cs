using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Детерминированный генератор fallback-текстур для визуализации неразрешённых связей.
/// Предотвращает случайное угадывание текстур и гарантирует безопасность рендера.
/// </summary>
public static class FallbackGenerator
{
    public static byte[] CreateCheckerboardTga(int width = 64, int height = 64, uint col1 = 0xFFFF00FF, uint col2 = 0xFF000000)
    {
        var tga = new byte[18 + width * height * 4];
        tga[2] = 2; // uncompressed true-color
        BinaryPrimitives.WriteInt16LittleEndian(tga.AsSpan(12, 2), (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(tga.AsSpan(14, 2), (short)height);
        tga[16] = 32;
        tga[17] = 8;
        int checkSize = Math.Max(4, width / 8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool alt = ((x / checkSize) ^ (y / checkSize)) != 0;
                uint c = alt ? col1 : col2;
                int idx = 18 + (y * width + x) * 4;
                tga[idx + 0] = (byte)(c & 0xFF);         // Blue
                tga[idx + 1] = (byte)((c >> 8) & 0xFF);  // Green
                tga[idx + 2] = (byte)((c >> 16) & 0xFF); // Red
                tga[idx + 3] = (byte)((c >> 24) & 0xFF); // Alpha
            }
        }
        return tga;
    }

    public static byte[] CreateSolidTga(int width = 16, int height = 16, uint color = 0xFF808080)
    {
        var tga = new byte[18 + width * height * 4];
        tga[2] = 2;
        BinaryPrimitives.WriteInt16LittleEndian(tga.AsSpan(12, 2), (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(tga.AsSpan(14, 2), (short)height);
        tga[16] = 32;
        tga[17] = 8;
        byte b = (byte)(color & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte r = (byte)((color >> 16) & 0xFF);
        byte a = (byte)((color >> 24) & 0xFF);
        for (int i = 0; i < width * height; i++)
        {
            int idx = 18 + i * 4;
            tga[idx + 0] = b;
            tga[idx + 1] = g;
            tga[idx + 2] = r;
            tga[idx + 3] = a;
        }
        return tga;
    }

    public static ResolvedTexture CreateFallbackTexture(uint key, string scope = "FALLBACK", string reason = "Missing texture")
    {
        return new ResolvedTexture
        {
            Id = new ResourceId(key, scope),
            RawKey = key,
            WrapperKey = key,
            Width = 64,
            Height = 64,
            FormatType = 0x1001,
            TgaData = CreateCheckerboardTga(64, 64),
            IsFallback = true,
            Description = reason
        };
    }
}

