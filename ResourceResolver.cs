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

/// <summary>
/// Разрешённый material Jade (MultiMaterial 0x04 или MultiTexture 0x05) с полным графом связей.
/// </summary>
public sealed class ResolvedMaterial
{
    public ResourceId Id { get; init; }
    public int Type { get; init; } // 4 = MultiMaterial, 5 = MultiTexture
    public int Offset { get; init; }
    public uint Diffuse { get; init; }
    public float Opacity { get; init; } = 1.0f;
    public uint Flags { get; init; }
    public byte[]? SourceBytes { get; init; }
    public List<uint> SubMaterialKeys { get; } = [];
    public List<uint> TextureKeys { get; } = [];
    public List<ResolvedTexture?> Textures { get; } = [];
    public List<ResolvedMaterial?> SubMaterials { get; } = [];

    public override string ToString() => $"Material {Id} (Type {Type}, {SubMaterialKeys.Count} submaterials, {TextureKeys.Count} textures)";
}

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

public sealed class ResolvedObjectInfo
{
    public string Name { get; init; } = "";
    public uint GaoKey { get; init; }
    public uint GeometryKey { get; init; }
    public uint MaterialKey { get; init; }
    public int Vertices { get; init; }
    public int Triangles { get; init; }
    public int PartsCount { get; init; }
    public int ResolvedTexturesCount { get; init; }
    public List<string> TextureNames { get; } = [];
    public bool IsFullyResolved => PartsCount > 0 && ResolvedTexturesCount == PartsCount;
}

/// <summary>
/// Статистика и результаты разрешения графа ресурсов сцены.
/// </summary>
public sealed class ResolutionReport
{
    public int TotalObjects { get; set; }
    public int ResolvedGeometries { get; set; }
    public int MissingGeometries { get; set; }
    public int ResolvedMaterials { get; set; }
    public int MissingMaterials { get; set; }
    public int DefaultMaterials { get; set; }
    public int ResolvedTextures { get; set; }
    public int MissingTextures { get; set; }
    public int FallbackTextures { get; set; }
    public int FixGeometriesUsed { get; set; }
    public int FixMaterialsUsed { get; set; }
    public int FixTexturesUsed { get; set; }
    public List<ResolvedObjectInfo> ResolvedObjects { get; } = [];
    public List<string> Details { get; } = [];
    public List<string> UnresolvedDetails { get; } = [];
    private readonly HashSet<string> _seenUnresolved = new(StringComparer.OrdinalIgnoreCase);

    public void AddUnresolved(string message)
    {
        if (_seenUnresolved.Add(message))
            UnresolvedDetails.Add(message);
    }

    public string FormatMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Asset resolution summary");
        sb.AppendLine($"- **Game objects (GAO):** {TotalObjects}");
        sb.AppendLine($"- **Geometry (GRO):** {ResolvedGeometries} resolved (from FIX: {FixGeometriesUsed}), {MissingGeometries} missing");
        sb.AppendLine($"- **Materials (GRM):** {ResolvedMaterials} resolved (from FIX: {FixMaterialsUsed}), {DefaultMaterials} default, {MissingMaterials} missing");
        sb.AppendLine($"- **Textures:** {ResolvedTextures} resolved (from FIX: {FixTexturesUsed}), {MissingTextures} missing, {FallbackTextures} fallback");
        
        var fully = ResolvedObjects.Where(o => o.IsFullyResolved).ToList();
        if (fully.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"#### Fully resolved scene objects ({fully.Count}):");
            foreach (var o in fully.Take(15))
            {
                string texs = string.Join(", ", o.TextureNames.Distinct());
                sb.AppendLine($"- **{o.Name}** ({o.GaoKey:X8}): {o.Vertices} vertices, {o.Triangles} triangles, {o.PartsCount} parts, textures: [{texs}]");
            }
        }

        if (UnresolvedDetails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"#### Unresolved unique references (Missing/Fallback: {UnresolvedDetails.Count}):");
            foreach (var detail in UnresolvedDetails.Take(25))
                sb.AppendLine($"- {detail}");
            if (UnresolvedDetails.Count > 25)
                sb.AppendLine($"- ...and {UnresolvedDetails.Count - 25} unresolved references");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Модуль А2: Связывает игровые объекты (GAO) с геометрией (GRO), материалами (GRM) и текстурами.
/// Различает локальные и общие (FIX) ресурсы, разделяет экземпляры и общие ресурсы,
/// отслеживает состояние разрешения (Resolved, Missing, Fallback) вместо случайного угадывания текстур.
/// </summary>
public sealed class ResourceResolver
{
    // Archive instances are immutable snapshots. Keep only key sets here, not all
    // decoded meshes. Scene switches can skip unrelated packages after the first scan.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Archive,
        System.Collections.Concurrent.ConcurrentDictionary<uint, HashSet<uint>>> FallbackKeyIndexes = new();
    private readonly Dictionary<uint, Geometry> _geometries = new();
    private readonly Dictionary<uint, ResolvedMaterial> _materials = new();
    private readonly Dictionary<uint, ResolvedTexture> _textures = new();
    private readonly Dictionary<uint, ResourceItem> _resourceItems = new();
    private readonly Dictionary<uint, string> _provenance = new();
    private readonly HashSet<uint> _verifiedTextureKeys = [];
    private readonly Dictionary<string, string> _textureLoaderStatus = new(StringComparer.OrdinalIgnoreCase);
    private bool _strictTextures;
    private readonly Dictionary<(string Scope, int Offset), ResolvedTexture> _textureVariants = [];

    public sealed record ArchiveFallbackHit(uint Key, string Type, string[] Containers, string[] MatchedObjects);
    private readonly List<ArchiveFallbackHit> _archiveFallbackHits = [];

    public IReadOnlyDictionary<uint, Geometry> Geometries => _geometries;
    public IReadOnlyDictionary<uint, ResolvedMaterial> Materials => _materials;
    public IReadOnlyDictionary<uint, ResolvedTexture> Textures => _textures;
    public IEnumerable<ResolvedTexture> TextureVariants => _textureVariants.Values;
    public IReadOnlyDictionary<uint, ResourceItem> ResourceItems => _resourceItems;
    public IReadOnlyList<ArchiveFallbackHit> ArchiveFallbackHits => _archiveFallbackHits;
    public IReadOnlyDictionary<string, string> TextureLoaderStatus => _textureLoaderStatus;

    public const uint FixWorldKey = 0x5E0084DF;
    public const uint FixSceneContainerKey = 0xFF0084DF;
    public const uint FixTextureContainerKey = 0xFF8084DF;

    public ResourceResolver()
    {
    }

    /// <summary>
    /// Автоматически загружает и кэширует общие ресурсы FIX из BIG-архива (_main_fix -> FF0084DF / FF8084DF).
    /// </summary>
    public bool LoadFixContainer(Archive archive)
    {
        var fixFf0 = archive.Entries.FirstOrDefault(e => e.Key == FixSceneContainerKey || e.Name.Equals("ff0084df.bin", StringComparison.OrdinalIgnoreCase));
        if (fixFf0 == null) return false;

        var fixFf8 = archive.Entries.FirstOrDefault(e => e.Key == FixTextureContainerKey || e.Name.Equals("ff8084df.bin", StringComparison.OrdinalIgnoreCase));

        byte[] ff0Bytes = archive.Read(fixFf0);
        byte[]? ff8Bytes = fixFf8 != null ? archive.Read(fixFf8) : null;

        RegisterContainer(ff0Bytes, ff8Bytes, "FIX", archive.PathName);
        return true;
    }

    /// <summary>
    /// Загружает локальный контейнер сцены и парный контейнер текстур из BIG-архива.
    /// </summary>
    public bool LoadSceneContainer(Archive archive, Entry sceneEntry)
    {
        uint texKey = 0xFF800000 | (sceneEntry.Key & 0x0007FFFF);
        var texEntry = archive.Entries.FirstOrDefault(e => e.Key == texKey);

        byte[] ff0Bytes = archive.Read(sceneEntry);
        byte[]? ff8Bytes = texEntry != null ? archive.Read(texEntry) : null;

        RegisterContainer(ff0Bytes, ff8Bytes, sceneEntry.Id, archive.PathName);
        LoadArchiveResourceFallback(archive, sceneEntry, ff0Bytes);
        return true;
    }

    private void LoadArchiveResourceFallback(Archive archive, Entry targetEntry, byte[] targetBytes)
    {
        var targetDoc = SceneLoader.Load(targetBytes, targetEntry.Id, archive.PathName);
        var targetObjects = targetDoc.Worlds.SelectMany(w => w.Objects).ToArray();
        var targetGeoNames = targetObjects.Where(o => o.GeometryKey != 0)
            .GroupBy(o => o.GeometryKey).ToDictionary(g => g.Key, g => g.Select(o => o.Name).Distinct().ToArray());
        var unresolvedGeo = targetGeoNames.Keys.Where(k => !_geometries.ContainsKey(k)).ToHashSet();
        var unresolvedMat = targetObjects.Select(o => o.MaterialKey).Where(k => k is not (0 or uint.MaxValue) && !_materials.ContainsKey(k)).ToHashSet();
        if (unresolvedGeo.Count == 0 && unresolvedMat.Count == 0) return;

        var geoCandidates = new Dictionary<(uint Key, string Signature), (Geometry Model, HashSet<string> Containers, HashSet<string> Names)>();
        var matCandidates = new Dictionary<(uint Key, string Signature), (JadeMaterial Material, HashSet<string> Containers)>();
        var sourceKeys = FallbackKeyIndexes.GetValue(archive, _ => new());
        foreach (var entry in archive.Entries.Where(e => (e.Key & 0xFFF00000) == 0xFF000000))
        {
            if (sourceKeys.TryGetValue(entry.Key, out var knownKeys)
                && !knownKeys.Overlaps(unresolvedGeo) && !knownKeys.Overlaps(unresolvedMat)) continue;
            byte[] bytes;
            try { bytes = archive.Read(entry); }
            catch { continue; }
            SceneDocument doc;
            SceneAssets assets;
            try
            {
                doc = SceneLoader.Load(bytes, entry.Id, archive.PathName);
                assets = Models.Read(bytes, archive.PathName);
                sourceKeys[entry.Key] = assets.Models.Select(m => m.Key).Concat(assets.Materials.Select(m => m.Key)).ToHashSet();
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
            { continue; }

            var sourceMatRefs = doc.Worlds.SelectMany(w => w.Objects).Select(o => o.MaterialKey).ToHashSet();
            foreach (var model in assets.Models)
            {
                if (model.Key == 0 || !unresolvedGeo.Contains(model.Key) || model.Objects.Count == 0) continue;
                string signature = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(model.Offset, model.Size)));
                var id = (model.Key, signature);
                if (!geoCandidates.TryGetValue(id, out var candidate))
                    candidate = (CloneGeometry(model), [], []);
                candidate.Containers.Add(entry.Id);
                foreach (string name in model.Objects.Select(ObjectLabel)) candidate.Names.Add(name);
                geoCandidates[id] = candidate;
            }
            foreach (var material in assets.Materials)
            {
                if (material.Key == 0 || !unresolvedMat.Contains(material.Key) || !sourceMatRefs.Contains(material.Key)) continue;
                string signature = MaterialSignature(material);
                var id = (material.Key, signature);
                if (!matCandidates.TryGetValue(id, out var candidate)) candidate = (material, []);
                candidate.Containers.Add(entry.Id);
                matCandidates[id] = candidate;
            }
        }

        var geoGroups = geoCandidates.GroupBy(p => p.Key.Key);
        foreach (var group in geoGroups)
        {
            // Never break a cross-container content conflict by choosing the most common guess.
            if (group.Count() != 1) continue;
            var candidate = group.Single().Value;
            var targetNames = targetGeoNames.GetValueOrDefault(group.Key) ?? [];
            bool namedMatch = targetNames.Any(candidate.Names.Contains);
            if (candidate.Containers.Count < 2 && !namedMatch) continue;
            var model = CloneGeometry(candidate.Model);
            model.Key = group.Key;
            model.Objects.Clear();
            model.Objects.AddRange(targetNames);
            _geometries[group.Key] = model;
            string scope = candidate.Containers.Order(StringComparer.Ordinal).First();
            _provenance[group.Key] = scope;
            _resourceItems[group.Key] = new ResourceItem
            {
                Id = new ResourceId(group.Key, scope),
                Type = ResourceType.Geometry,
                State = ResolutionState.Resolved,
                Payload = model
            };
            _archiveFallbackHits.Add(new ArchiveFallbackHit(group.Key, "GRO",
                candidate.Containers.Order(StringComparer.Ordinal).ToArray(),
                targetNames.Where(candidate.Names.Contains).Order(StringComparer.Ordinal).ToArray()));
        }

        foreach (var group in matCandidates.GroupBy(p => p.Key.Key))
        {
            if (group.Count() != 1) continue;
            var candidate = group.Single().Value;
            if (candidate.Containers.Count < 2) continue;
            var mat = candidate.Material;
            string scope = candidate.Containers.Order(StringComparer.Ordinal).First();
            var resolved = new ResolvedMaterial
            {
                Id = new ResourceId(group.Key, scope),
                Type = mat.Type,
                Offset = mat.Offset,
                SourceBytes = mat.Bytes,
                Diffuse = mat.Diffuse
            };
            resolved.SubMaterialKeys.AddRange(mat.Children);
            resolved.TextureKeys.AddRange(mat.Textures);
            _materials[group.Key] = resolved;
            _provenance[group.Key] = scope;
            var item = new ResourceItem
            {
                Id = new ResourceId(group.Key, scope),
                Type = ResourceType.Material,
                State = ResolutionState.Resolved,
                Payload = resolved
            };
            item.Dependencies.AddRange(mat.Children);
            item.Dependencies.AddRange(mat.Textures);
            _resourceItems[group.Key] = item;
            _archiveFallbackHits.Add(new ArchiveFallbackHit(group.Key, "GRM",
                candidate.Containers.Order(StringComparer.Ordinal).ToArray(), []));
        }
    }

    private static Geometry CloneGeometry(Geometry model) => new()
    {
        Key = model.Key,
        Offset = model.Offset,
        Size = model.Size,
        MaterialKey = model.MaterialKey,
        Positions = model.Positions,
        Uvs = model.Uvs,
        VertexColors = model.VertexColors,
        Skin = model.Skin,
        Parts = model.Parts.ToList(),
        Objects = model.Objects.ToList()
    };

    private static string ObjectLabel(string value)
    {
        int slash = value.IndexOf(" / ", StringComparison.Ordinal);
        return slash >= 0 ? value[(slash + 3)..] : value;
    }

    private static string MaterialSignature(JadeMaterial material) =>
        $"{material.Type}|{material.Diffuse:X8}|{string.Join(',', material.Textures.Select(k => k.ToString("X8")))}|{string.Join(',', material.Children.Select(k => k.ToString("X8")))}";

    /// <summary>
    /// Регистрирует и индексирует ресурсы (геометрию, материалы, текстуры) из bytesов контейнера.
    /// </summary>
    public void RegisterContainer(byte[] ff0Bytes, byte[]? ff8Bytes, string scope, string? archivePath = null)
    {
        var assets = Models.Read(ff0Bytes, archivePath);
        // Prefer executable-derived registry rules; old captures are a compatibility path.
        if (ff8Bytes != null && ff8Bytes.Length > 0)
        {
            List<TextureAsset>? observed;
            try
            {
                var replay = PcTextureLoader.Read(ff0Bytes, ff8Bytes);
                observed = replay.Textures;
                _textureLoaderStatus[scope] = $"Autonomous PC replay: {observed.Count} textures; complete FF8 consumed.";
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
            {
                _textureLoaderStatus[scope] = $"Autonomous PC replay unsupported: {ex.Message}; legacy path used.";
                observed = RuntimeTextures.Read(ff8Bytes, ff0Bytes, scope);
            }
            if (observed != null)
            {
                _strictTextures = true;
                RegisterTextures(observed, scope);
            }
            else RegisterTexturesFromBytes(ff8Bytes, scope);
        }

        // 2. Индексация моделей и материалов из FF0
        if (ff0Bytes != null && ff0Bytes.Length > 0)
        {
            RegisterSceneAssets(assets, scope);
        }
    }

    /// <summary>
    /// Парсит и индексирует текстуры из FF8 с учётом архитектуры Jade:
    /// Type 7 (PaletteLink / .tex дескриптор) связывает RawKey и PaletteKey,
    /// Материал ссылается на WrapperKey, восстановленный повторением загрузчика; RawKey + 1 остаётся только старым запасным предположением.
    /// </summary>
    public void RegisterTexturesFromBytes(byte[] ff8Bytes, string scope, IReadOnlyDictionary<int, uint>? descriptorKeys = null)
    {
        if (descriptorKeys != null) _strictTextures = true;
        var rawTextures = Preview.ReadTextures(ff8Bytes, descriptorKeys);
        RegisterTextures(rawTextures, scope);
    }

    private void RegisterTextures(List<TextureAsset> rawTextures, string scope)
    {
        foreach (var t in rawTextures)
        {
            uint rawKey = t.RawKey;
            if (rawKey == 0 && !uint.TryParse(t.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rawKey)) continue;
            bool verified = t.WrapperKey != 0;
            uint wrapperKey = verified ? t.WrapperKey : rawKey + 1;

            var resTex = new ResolvedTexture
            {
                Id = new ResourceId(verified ? wrapperKey : rawKey, scope),
                RawKey = rawKey,
                PaletteKey = t.PaletteKey,
                WrapperKey = wrapperKey,
                Width = t.Width,
                Height = t.Height,
                FormatType = t.Type,
                TgaData = t.Tga,
                PaletteData = t.Palette,
                IsFallback = false,
                HasVerifiedBinding = verified,
                ChunkOffset = t.ChunkOffset,
                Description = $"{t.Width}x{t.Height} {t.Type:X4}; Raw {rawKey:X8}, Palette {t.PaletteKey:X8}; {(verified ? "native descriptor key" : "legacy key estimate") }"
            };

            _textureVariants[(scope, t.ChunkOffset)] = resTex;

            var item = new ResourceItem
            {
                Id = new ResourceId(wrapperKey, scope),
                Type = ResourceType.Texture,
                State = ResolutionState.Resolved,
                Payload = resTex
            };
            if (verified)
            {
                _textures[wrapperKey] = resTex;
                _provenance[wrapperKey] = scope;
                _resourceItems[wrapperKey] = item;
                _verifiedTextureKeys.Add(wrapperKey);
            }
            else
            {
                // Unconfirmed aliases must not overwrite a proven descriptor identity.
                foreach (uint alias in new[] { rawKey, wrapperKey })
                    if (!_verifiedTextureKeys.Contains(alias))
                    { _textures[alias] = resTex; _provenance[alias] = scope; _resourceItems[alias] = item; }
            }
        }
    }

    /// <summary>
    /// Парсит геометрию и материалы из FF0 через проверенный сканер Models.Read,
    /// сохраняя оригинальные ключи и разделяя геометрию и материалы.
    /// </summary>
    public void RegisterSceneAssetsFromBytes(byte[] ff0Bytes, string scope)
    {
        RegisterSceneAssets(Models.Read(ff0Bytes), scope);
    }

    private void RegisterSceneAssets(SceneAssets scene, string scope)
    {

        // Индексация геометрии GRO
        foreach (var m in scene.Models)
        {
            if (m.Key != 0)
            {
                _geometries[m.Key] = m;
                _provenance[m.Key] = scope;

                var item = new ResourceItem
                {
                    Id = new ResourceId(m.Key, scope),
                    Type = ResourceType.Geometry,
                    State = ResolutionState.Resolved,
                    Payload = m
                };
                _resourceItems[m.Key] = item;
            }
        }

        // Индексация материалов GRM
        foreach (var mat in scene.Materials)
        {
            int type = mat.Type != 0 ? mat.Type : mat.Children.Length > 0 ? 4 : (mat.Textures.Length > 0 ? 5 : 3);
            var resMat = new ResolvedMaterial
            {
                Id = new ResourceId(mat.Key, scope),
                Type = type,
                Offset = mat.Offset,
                SourceBytes = mat.Bytes,
                Diffuse = mat.Diffuse,
                Opacity = 1.0f
            };

            resMat.SubMaterialKeys.AddRange(mat.Children);
            resMat.TextureKeys.AddRange(mat.Textures);

            _materials[mat.Key] = resMat;
            _provenance[mat.Key] = scope;

            var item = new ResourceItem
            {
                Id = new ResourceId(mat.Key, scope),
                Type = ResourceType.Material,
                State = ResolutionState.Resolved,
                Payload = resMat
            };
            item.Dependencies.AddRange(mat.Children);
            item.Dependencies.AddRange(mat.Textures);
            _resourceItems[mat.Key] = item;
        }
    }

    /// <summary>
    /// Связывает все объекты в SceneDocument с их ресурсами (GRO, GRM, Textures).
    /// Заполняет doc.Resources и doc.Diagnostics проверенными данными.
    /// </summary>
    public ResolutionReport ResolveSceneDocument(SceneDocument doc, bool generateFallbackTextures = true)
    {
        var report = new ResolutionReport();
        uint previewKey=0x00FFFFFF;
        foreach(var obj in doc.Worlds.SelectMany(w=>w.Objects))
        {
            if(obj.PreviewGeometryKey!=0 || obj.Actor!=null || obj.MaterialKey is 0 or uint.MaxValue || !_materials.ContainsKey(obj.MaterialKey))continue;
            var modifier=obj.Modifiers.FirstOrDefault(m=>m.Type==11);
            var pag=obj.Modifiers.FirstOrDefault(m=>m.Type==18);
            if(modifier==null && pag==null || !_geometries.TryGetValue(obj.GeometryKey,out var carrier))continue;
            try
            {
                var generated=modifier==null?null:SpriteGenerator.Build(carrier,obj,modifier.Data);
                if(pag!=null && ParticleDefinition.Read(pag.Data,0,pag.Data.Length) is {} particle)generated=particle.Attach(generated??carrier,obj);
                if(generated==null)continue;
                while(_geometries.ContainsKey(previewKey)||doc.Resources.ContainsKey(previewKey))previewKey--;
                generated.Key=previewKey;_geometries[previewKey]=generated;obj.PreviewGeometryKey=previewKey--;
                doc.Diagnostics.Add($"Procedural: {obj.Name}: {generated.Billboards.Count} sprites, original GRO {obj.GeometryKey:X8}. Full-density preview; runtime random seed and distance fade are not reproduced.");
            }
            catch(InvalidDataException ex){doc.Diagnostics.Add($"SPG {obj.Name}: {ex.Message}");}
        }

        foreach (var world in doc.Worlds)
        {
            foreach (var obj in world.Objects)
            {
                report.TotalObjects++;
                if(_geometries.GetValueOrDefault(obj.RenderGeometryKey)?.Particles is {} pag)
                    doc.Diagnostics.Add($"PAG {obj.Name}: capacity {pag.Capacity}, flags {pag.Flags:X8}, shape {pag.Shape}, direction {pag.Direction}. " +
                        (pag.Shape>5 || pag.Direction is 4 or >5 ? "External source/target geometry links are not implemented." :
                        (pag.Flags&64)==0 ? "Emitter disabled in serialized state; runtime activation is not emulated." : "CPU preview; game AI activation and exact runtime random sequence are not emulated."));

                Geometry? geom = null;

                // 1. Разрешение геометрии
                if (obj.GeometryKey != 0 && obj.GeometryKey != uint.MaxValue)
                {
                    if (_geometries.TryGetValue(obj.GeometryKey, out geom))
                    {
                        report.ResolvedGeometries++;
                        string scope = _provenance.GetValueOrDefault(obj.GeometryKey, doc.ContainerName);
                        if (scope == "FIX") report.FixGeometriesUsed++;

                        doc.Resources[obj.GeometryKey] = new ResourceItem
                        {
                            Id = new ResourceId(obj.GeometryKey, scope),
                            Type = ResourceType.Geometry,
                            State = ResolutionState.Resolved,
                            Payload = geom
                        };
                    }
                    else
                    {
                        report.MissingGeometries++;
                        report.AddUnresolved($"Object '{obj.Name}' ({obj.GaoKey:X8}): missing geometry GRO {obj.GeometryKey:X8}");
                        doc.Resources[obj.GeometryKey] = new ResourceItem
                        {
                            Id = new ResourceId(obj.GeometryKey, doc.ContainerName),
                            Type = ResourceType.Geometry,
                            State = ResolutionState.Missing,
                            Payload = null
                        };
                    }
                }

                ResolvedMaterial? resolvedMat = null;

                // 2. Разрешение материала
                if (obj.MaterialKey == 0 || obj.MaterialKey == uint.MaxValue)
                {
                    report.DefaultMaterials++;
                }
                else
                {
                    if (_materials.TryGetValue(obj.MaterialKey, out var mat))
                    {
                        resolvedMat = mat;
                        report.ResolvedMaterials++;
                        string scope = _provenance.GetValueOrDefault(obj.MaterialKey, doc.ContainerName);
                        if (scope == "FIX") report.FixMaterialsUsed++;

                        var matItem = new ResourceItem
                        {
                            Id = new ResourceId(obj.MaterialKey, scope),
                            Type = ResourceType.Material,
                            State = ResolutionState.Resolved,
                            Payload = mat
                        };
                        matItem.Dependencies.AddRange(mat.SubMaterialKeys);
                        matItem.Dependencies.AddRange(mat.TextureKeys);
                        doc.Resources[obj.MaterialKey] = matItem;

                        // Разрешение текстур для частей материала
                        ResolveMaterialTexturesRecursive(mat, doc, report, generateFallbackTextures);
                    }
                    else
                    {
                        report.MissingMaterials++;
                        report.AddUnresolved($"Object '{obj.Name}' ({obj.GaoKey:X8}): missing material GRM {obj.MaterialKey:X8}");
                        doc.Resources[obj.MaterialKey] = new ResourceItem
                        {
                            Id = new ResourceId(obj.MaterialKey, doc.ContainerName),
                            Type = ResourceType.Material,
                            State = ResolutionState.Missing,
                            Payload = null
                        };
                    }
                }

                // 3. Сбор детальной информации об объекте для отчёта
                var objInfo = new ResolvedObjectInfo
                {
                    Name = obj.Name,
                    GaoKey = obj.GaoKey,
                    GeometryKey = obj.GeometryKey,
                    MaterialKey = obj.MaterialKey,
                    Vertices = geom?.Positions.Length ?? 0,
                    Triangles = geom?.Triangles ?? 0,
                    PartsCount = geom?.Parts.Count ?? 0
                };
                if (geom != null)
                {
                    for (int i = 0; i < geom.Parts.Count; i++)
                    {
                        var part = geom.Parts[i];
                        ResolvedMaterial? effMat = resolvedMat;
                        if (resolvedMat != null && resolvedMat.SubMaterialKeys.Count > 0 && part.MaterialId >= 0 && resolvedMat.SubMaterialKeys.Count > 0)
                            effMat = _materials.GetValueOrDefault(resolvedMat.SubMaterialKeys[Math.Min(part.MaterialId, resolvedMat.SubMaterialKeys.Count - 1)]);

                        uint tk = effMat?.TextureKeys.FirstOrDefault() ?? 0;
                        if (tk != 0 && TryResolveTexture(tk, out var rTex))
                            objInfo.TextureNames.Add(rTex.Id.Key.ToString("X8"));
                    }
                }
                var finalizedObjInfo = new ResolvedObjectInfo
                {
                    Name = objInfo.Name,
                    GaoKey = objInfo.GaoKey,
                    GeometryKey = objInfo.GeometryKey,
                    MaterialKey = objInfo.MaterialKey,
                    Vertices = objInfo.Vertices,
                    Triangles = objInfo.Triangles,
                    PartsCount = objInfo.PartsCount,
                    ResolvedTexturesCount = objInfo.TextureNames.Count
                };
                finalizedObjInfo.TextureNames.AddRange(objInfo.TextureNames);
                report.ResolvedObjects.Add(finalizedObjInfo);
            }
        }

        doc.Diagnostics.Add(report.FormatMarkdown());
        return report;
    }

    private void ResolveMaterialTexturesRecursive(ResolvedMaterial mat, SceneDocument doc, ResolutionReport report, bool generateFallback, HashSet<uint>? visited = null, int depth = 0)
    {
        visited ??= [];
        if (!visited.Add(mat.Id.Key))
        {
            report.AddUnresolved($"Repeated or cyclic material reference {mat.Id.Key:X8}; traversal stopped.");
            return;
        }
        if (depth >= 64)
        {
            report.AddUnresolved($"Material graph too deep at {mat.Id.Key:X8}; traversal stopped.");
            return;
        }
        // Если это MultiMaterial (Type 4) — рекурсивно проверяем дочерние материалы
        foreach (var subKey in mat.SubMaterialKeys)
        {
            if (_materials.TryGetValue(subKey, out var subMat))
            {
                string scope = _provenance.GetValueOrDefault(subKey, doc.ContainerName);
                if (!doc.Resources.ContainsKey(subKey))
                {
                    var item = new ResourceItem
                    {
                        Id = new ResourceId(subKey, scope),
                        Type = ResourceType.Material,
                        State = ResolutionState.Resolved,
                        Payload = subMat
                    };
                    item.Dependencies.AddRange(subMat.TextureKeys);
                    doc.Resources[subKey] = item;
                }
                ResolveMaterialTexturesRecursive(subMat, doc, report, generateFallback, visited, depth + 1);
            }
            else
            {
                report.MissingMaterials++;
                report.AddUnresolved($"Child material {subKey:X8} for MultiMaterial {mat.Id.Key:X8} not found");
                if (!doc.Resources.ContainsKey(subKey))
                {
                    doc.Resources[subKey] = new ResourceItem
                    {
                        Id = new ResourceId(subKey, doc.ContainerName),
                        Type = ResourceType.Material,
                        State = ResolutionState.Missing
                    };
                }
            }
        }

        // Разрешение текстур в данном материале
        foreach (var texKey in mat.TextureKeys)
        {
            if (texKey == 0 || texKey == uint.MaxValue) continue;

            if (TryResolveTexture(texKey, out var resTex))
            {
                report.ResolvedTextures++;
                string scope = resTex.Id.Scope;
                if (scope == "FIX") report.FixTexturesUsed++;

                if (!doc.Resources.ContainsKey(texKey))
                {
                    doc.Resources[texKey] = new ResourceItem
                    {
                        Id = new ResourceId(texKey, scope),
                        Type = ResourceType.Texture,
                        State = ResolutionState.Resolved,
                        Payload = resTex
                    };
                }
            }
            else
            {
                report.MissingTextures++;
                report.AddUnresolved($"Texture {texKey:X8} (material {mat.Id.Key:X8}) missing in scene container and FIX");

                if (generateFallback)
                {
                    report.FallbackTextures++;
                    var fallbackTex = FallbackGenerator.CreateFallbackTexture(texKey, "FALLBACK", $"Missing texture {texKey:X8}");
                    doc.Resources[texKey] = new ResourceItem
                    {
                        Id = new ResourceId(texKey, "FALLBACK"),
                        Type = ResourceType.Texture,
                        State = ResolutionState.Fallback,
                        Payload = fallbackTex
                    };
                }
                else
                {
                    doc.Resources[texKey] = new ResourceItem
                    {
                        Id = new ResourceId(texKey, doc.ContainerName),
                        Type = ResourceType.Texture,
                        State = ResolutionState.Missing,
                        Payload = null
                    };
                }
            }
        }
    }

    /// <summary>
    /// Попытка разрешить текстуру по её ключу (.tex или .raw).
    /// Проверяет: прямое совпадение ключа, ключ обёртки (key - 1), и ключи в FIX.
    /// Никогда не угадывает текстуры случайно.
    /// </summary>
    public bool TryResolveTexture(uint key, out ResolvedTexture texture)
    {
        // 1. Прямой поиск в словаре (покрывает как RawKey, так и WrapperKey = RawKey + 1)
        if (_textures.TryGetValue(key, out texture!))
            return true;

        // 2. Если material ссылается на .tex, а в словаре только RawKey: key - 1
        if (!_strictTextures && key > 0 && _textures.TryGetValue(key - 1, out texture!))
            return true;

        texture = null!;
        return false;
    }

    /// <summary>
    /// Согласованный адаптер для Models.cs и Preview.cs:
    /// Возвращает список текстур для каждой части геометрии.
    /// Не угадывает случайные текстуры. Если текстура not foundа — возвращает fallback или null.
    /// </summary>
    public List<TextureAsset?> ResolveTexturesForGeometry(Geometry model, uint materialKey, bool useFallback = false)
    {
        var result = new List<TextureAsset?>();

        var rootMat = _materials.GetValueOrDefault(materialKey);

        for (int i = 0; i < model.Parts.Count; i++)
        {
            var part = model.Parts[i];
            ResolvedMaterial? effectiveMat = rootMat;

            // Если родительский material MultiMaterial (Type 4), выбираем подmaterial по MaterialId части
            if (rootMat != null && rootMat.SubMaterialKeys.Count > 0 && part.MaterialId >= 0 && rootMat.SubMaterialKeys.Count > 0)
            {
                uint childKey = rootMat.SubMaterialKeys[Math.Min(part.MaterialId, rootMat.SubMaterialKeys.Count - 1)];
                effectiveMat = _materials.GetValueOrDefault(childKey);
            }

            uint texKey = effectiveMat?.TextureKeys.FirstOrDefault() ?? 0;

            if (texKey != 0 && texKey != uint.MaxValue && TryResolveTexture(texKey, out var resTex))
            {
                result.Add(resTex.ToTextureAsset());
            }
            else if (texKey != 0 && texKey != uint.MaxValue && useFallback)
            {
                var fb = FallbackGenerator.CreateFallbackTexture(texKey);
                result.Add(fb.ToTextureAsset());
            }
            else
            {
                result.Add(null);
            }
        }

        return result;
    }

    /// <summary>
    /// Подключает ResourceResolver к существующему объекту SceneAssets,
    /// замещая эвристический поиск текстур надёжным графом связей.
    /// </summary>
    public void AttachTo(SceneAssets assets)
    {
        assets.Resolver = this;
    }

    /// <summary>
    /// Диагностический запуск для верификации и подготовки отчёта:
    /// Открывает BF, загружает FIX, загружает сцену, связывает GAO/GRO/GRM/Textures
    /// и формирует полный отчёт.
    /// </summary>
    public static ResolutionReport RunDiagnostic(string bfPath, string sceneId, string outputMarkdownPath)
    {
        var archive = new Archive(bfPath);
        var sceneEntry = archive.Entries.FirstOrDefault(e => e.Id.Equals(sceneId, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(sceneId, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.First(e => e.Key.ToString("X8").Equals(sceneId, StringComparison.OrdinalIgnoreCase));

        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, sceneEntry);

        var doc = SceneLoader.Load(archive, sceneEntry);
        var report = resolver.ResolveSceneDocument(doc, generateFallbackTextures: true);
        if (!string.IsNullOrEmpty(outputMarkdownPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputMarkdownPath))!);
            File.WriteAllText(outputMarkdownPath, report.FormatMarkdown());
        }
        return report;
    }
}
