using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

// Offline replay of the original PC texture loader supplies descriptor identities.
// Only apply a replay to matching material records and matching FF8 header bytes.
// Editing pixels or GAO transforms does not invalidate these identities.
static class PcTextureKeys
{
    sealed record Map(string Scene, int HeaderLength, string HeaderHash, string MaterialsHash, Dictionary<int, uint> Keys);
    static readonly Dictionary<string, Map> Maps = Load();
    static Dictionary<string, Map> Load()
    {
        using var stream = typeof(PcTextureKeys).Assembly.GetManifestResourceStream("JadeView.PcTextureKeys");
        if (stream == null) return [];
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        return (JsonSerializer.Deserialize<List<Map>>(gzip) ?? []).ToDictionary(m => m.Scene);
    }
    public static IReadOnlyDictionary<int, uint>? Find(string scope, SceneAssets assets, byte[] ff0, byte[] ff8)
    {
        string key = scope == "FIX" ? "FF0084DF" : scope.ToUpperInvariant();
        if (!Maps.TryGetValue(key, out var map) || map.HeaderLength < 0 || map.HeaderLength > ff8.Length) return null;
        string headerHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ff8.AsSpan(0, map.HeaderLength)));
        if (headerHash != map.HeaderHash) return null;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var m in assets.Materials.Where(m => m.Type is 3 or 5).OrderBy(m => m.Offset))
        {
            if (m.Offset < 4 || m.Offset > ff0.Length - 4) return null;
            int size = Preview.I32(ff0, m.Offset - 4);
            if (size < 4 || size > ff0.Length - m.Offset) return null;
            hash.AppendData(ff0.AsSpan(m.Offset, size));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset()) == map.MaterialsHash ? map.Keys : null;
    }
}

