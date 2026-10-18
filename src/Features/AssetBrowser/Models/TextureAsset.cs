using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

public sealed record TextureAsset(string Name, int Width, int Height, byte[] Tga, int ChunkOffset, int Type, byte[]? Palette,
    uint RawKey = 0, uint PaletteKey = 0, uint WrapperKey = 0);
