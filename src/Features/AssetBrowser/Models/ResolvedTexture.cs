using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Разрешённый графический ассет текстуры (TGA) с метаданными и идентификатором.
/// </summary>
public sealed class ResolvedTexture
{
    public ResourceId Id { get; init; }
    public uint RawKey { get; init; }
    public uint PaletteKey { get; init; }
    public uint WrapperKey { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int FormatType { get; init; }
    public byte[] TgaData { get; init; } = [];
    public byte[]? PaletteData { get; init; }
    public bool IsFallback { get; init; }
    public bool HasVerifiedBinding { get; init; }
    public int ChunkOffset { get; init; }
    public string Description { get; init; } = "";

    public TextureAsset ToTextureAsset() => new(
        $"{Id}/raw{RawKey:X8}/pal{PaletteKey:X8}@{ChunkOffset:X8}",
        Width,
        Height,
        TgaData,
        ChunkOffset,
        FormatType,
        PaletteData, RawKey, PaletteKey, WrapperKey
    );

    public override string ToString() => $"Texture {Id} ({Width}x{Height}, {(IsFallback ? "FALLBACK" : "OK")})";
}

