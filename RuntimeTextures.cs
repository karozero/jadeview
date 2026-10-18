using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace JadeView;

// Captured registry order, selected raw/palette indices and palettes from BGE.exe.
// The native pixel-loading loop and every 32-byte image header were checked.
internal static class RuntimeTextures
{
    private sealed record Image(uint Key, uint RawKey, int Offset, uint PaletteKey);
    private sealed record Map(string Scene, string Hash, List<Image> Images, Dictionary<uint, byte[]> Palettes);
    private sealed record NativeMap(string Scene, int HeaderLength, string HeaderHash, string MaterialsHash,
        Dictionary<string, uint> Keys);
    private static readonly List<Map> Maps = Load();
    private static readonly List<NativeMap> NativeMaps = LoadNativeMaps();

    private static List<Map> Load()
    {
        using var stream = typeof(RuntimeTextures).Assembly.GetManifestResourceStream("JadeView.LiveTextureKeys");
        if (stream == null) return [];
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<Map>>(gzip) ?? [];
    }

    private static List<NativeMap> LoadNativeMaps()
    {
        using var stream = typeof(RuntimeTextures).Assembly.GetManifestResourceStream("JadeView.NativeTextureKeyMaps");
        if (stream == null) return [];
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<NativeMap>>(gzip) ?? [];
    }

    public static List<TextureAsset>? Read(byte[] bytes, byte[] ff0Bytes, string scope)
    {
        var replayMap = Maps.FirstOrDefault(m => m.Scene.Equals(scope, StringComparison.OrdinalIgnoreCase));
        if (replayMap != null && Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(replayMap.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return ReadReplay(bytes, replayMap);
        }

        var nativeMap = NativeMaps.FirstOrDefault(m => m.Scene.Equals(scope, StringComparison.OrdinalIgnoreCase));
        string materialHash = NativeMaterialHash(ff0Bytes);
        if (nativeMap != null && nativeMap.HeaderLength >= 0 && nativeMap.HeaderLength <= bytes.Length
            && Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, nativeMap.HeaderLength)))
                .Equals(nativeMap.HeaderHash, StringComparison.OrdinalIgnoreCase)
            && materialHash.Equals(nativeMap.MaterialsHash, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var keys = nativeMap.Keys.ToDictionary(p => int.Parse(p.Key, System.Globalization.CultureInfo.InvariantCulture), p => p.Value);
                var exact = Preview.ReadTextures(bytes, keys);
                if (exact.Count > 0 && exact.All(t => t.WrapperKey != 0)) return exact;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException or FormatException)
            {
                // A stale or malformed generated map falls through to the existing parser.
            }
        }

        var map = Maps.FirstOrDefault(m => m.Scene.Equals(scope, StringComparison.OrdinalIgnoreCase));
        if (map == null || Convert.ToHexStringLower(SHA256.HashData(bytes)) != map.Hash) return null;
        return ReadReplay(bytes, map);
    }

    private static List<TextureAsset>? ReadReplay(byte[] bytes, Map map)
    {
        var textures = new List<TextureAsset>();
        foreach (var image in map.Images)
        {
            int off = image.Offset + 4;
            if (image.Offset < 0 || off > bytes.Length - 32) throw new InvalidDataException("Invalid runtime texture offset.");
            int size = Preview.I32(bytes, image.Offset);
            if (size < 32 || size > bytes.Length - off) throw new InvalidDataException("Invalid runtime texture size.");
            int format = Preview.U16(bytes, off + 6);
            int width = Preview.U16(bytes, off + 8), height = Preview.U16(bytes, off + 10);
            byte[] tga;
            byte[]? palette = null;
            if (format == 0x1001)
            {
                tga = bytes.AsSpan(off + 32, size - 32).ToArray();
            }
            else if (format is 0x4006 or 0x5006 && map.Palettes.TryGetValue(image.PaletteKey, out palette))
            {
                int pixels = checked(width * height), colors = format == 0x5006 ? 16 : 256;
                int required = colors == 16 ? (pixels + 1) / 2 : pixels;
                if (pixels <= 0 || pixels > 16 * 1024 * 1024 || required > size - 32 || palette.Length != colors * 4)
                    throw new InvalidDataException("Runtime image and palette dimensions disagree.");
                tga = new byte[18 + pixels * 4];
                tga[2] = 2;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
                tga[16] = 32; tga[17] = 8;
                for (int i = 0; i < pixels; i++)
                {
                    int index = bytes[off + 32 + (colors == 16 ? i / 2 : i)];
                    if (colors == 16) index = i % 2 == 0 ? index >> 4 : index & 15;
                    palette.AsSpan(index * 4, 4).CopyTo(tga.AsSpan(18 + i * 4));
                }
            }
            else continue;
            textures.Add(new(image.Key.ToString("X8"), width, height, tga, image.Offset, format,
                palette, image.RawKey, image.PaletteKey, image.Key));
        }
        return textures;
    }

    private static string NativeMaterialHash(byte[] ff0Bytes)
    {
        // The emulated BGE texture loader consumes MultiTexture (type 5) records;
        // MultiMaterial records do not participate in its texture key registry.
        var materials = Models.Read(ff0Bytes).Materials.Where(m => m.Type == 5).OrderBy(m => m.Offset);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var material in materials)
        {
            if (material.Bytes is { Length: > 0 } record) hash.AppendData(record);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
