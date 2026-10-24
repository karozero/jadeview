using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Структурный загрузчик сцен Jade (Beyond Good & Evil PC).
/// Разбирает FF0-контейнеры, извлекает миры (.wow), игровые объекты (.gao),
/// их имена, иерархию, флаги, 4x4 матрицы трансформаций и ссылки на визуальные ресурсы.
/// </summary>
public static class SceneLoader
{
    private const uint MagicWow = 0x776F772E; // ".wow" (little-endian: 2E 77 6F 77)
    private const uint MagicGao = 0x6F61672E; // ".gao" (little-endian: 2E 67 61 6F)
    private const uint TagSmd = 0x646D732E; // ".smd"
    private const uint TagSnk = 0x6B6E732E; // ".snk"
    private const uint MagicRli = 0x80 | ((uint)'R' << 8) | ((uint)'L' << 16) | ((uint)'I' << 24);

    // Флаги идентичности GAO (OBJconst.h)
    public const uint IdentityFlagBone = 0x00000001;
    public const uint IdentityFlagAnims = 0x00000002;
    public const uint IdentityFlagDyna = 0x00000004;
    public const uint IdentityFlagBaseObject = 0x00001000;
    public const uint IdentityFlagVisu = 0x00004000;
    public const uint IdentityFlagOBBox = 0x00080000;
    public const uint IdentityFlagHierarchy = 0x00400000;
    public const uint IdentityFlagGroup = 0x00800000;

    public sealed record ChunkInfo(int Offset, int Size, uint Magic, bool IsSpecialTag);

    public static SceneDocument Load(byte[] bytes, string containerName = "", string bfPath = "", string bfSha256 = "", uint? worldListKey = null)
    {
        var doc = new SceneDocument
        {
            ContainerName = containerName,
            BaseBfPath = bfPath,
            BaseBfSha256 = bfSha256
        };

        if (bytes == null || bytes.Length < 16)
        {
            doc.Diagnostics.Add("Error: empty or truncated container.");
            return doc;
        }

        if (!string.IsNullOrEmpty(bfPath))
        {
            var replay = PcSceneLoader.Read(bytes, bfPath, worldListKey);
            if (replay.OrderedWorldRootKeys.Length > 0)
                doc.WorldList = new GameWorldListDescriptor(replay.OuterWorldListKey, replay.OrderedWorldRootKeys);
            if (replay.Complete)
            {
                doc.ReservedResourceKeys.UnionWith(replay.Records.Select(record => record.Key));
                foreach (var worldRecord in replay.Worlds)
                {
                    var world = new WorldData
                    {
                        Key = worldRecord.Key,
                        Name = worldRecord.Name,
                        Lighting = PcWorldLightingReader.Read(bytes.AsSpan(worldRecord.Offset, worldRecord.Size), worldRecord.Key),
                        RecordRange = new SourceRange(worldRecord.Offset - 4, worldRecord.Size + 4)
                    };
                    int index = 0;
                    // Keep the exact record instance captured when this world was loaded.
                    // A key may be evicted and read again in a later world; the final
                    // Resources dictionary then points at that later occurrence.
                    foreach (var record in worldRecord.WorldMembers)
                    {
                        var chunk = new ChunkInfo(record.Offset - 4, record.Size + 4, MagicGao, false);
                        var obj = ParseGao(bytes, chunk, record.Key, index++, doc.Diagnostics);
                        if (obj != null)
                        {
                            obj.Name = record.Name;
                            obj.InitialCapabilities = record.InitialCapabilities;
                            obj.SerializedStateFlags = record.SerializedStateFlags;
                            obj.SoundBankKey = record.SoundBank;
                            obj.CollisionInstanceKey = record.CollisionInstance;
                            obj.CollisionMapKey = record.CollisionMap;
                            obj.LightResourceKey = record.LightKey;
                            obj.Light = record.Light;
                            obj.PcBounds = record.Bounds;
                            obj.Modifiers = record.Modifiers.Select(m => new SceneModifier(m.Type, bytes.AsSpan(m.Offset, m.Size).ToArray())).ToList();
                            if (record.LightError != null) doc.Diagnostics.Add($"{record.Name}: {record.LightError}");
                            if (record.BoundsError != null) doc.Diagnostics.Add($"{record.Name}: {record.BoundsError}");
                            world.Objects.Add(obj);
                        }
                    }
                    doc.Worlds.Add(world);
                }
                ActorLoader.Build(bytes, replay, doc);
                foreach (var obj in doc.Worlds.SelectMany(w => w.Objects))
                    foreach (var resource in new[] { (obj.GeometryKey, ResourceType.Geometry), (obj.MaterialKey, ResourceType.Material) })
                        if (resource.Item1 is not (0 or uint.MaxValue))
                            doc.Resources.TryAdd(resource.Item1, new ResourceItem { Id = new ResourceId(resource.Item1, containerName), Type = resource.Item2 });
                doc.Diagnostics.Add($"Typed PC FF0: {replay.Records.Count} reads, complete {replay.End} bytes; SPE {replay.SpeSkipped}, cached {replay.CachedSkipped}. No guessed GAO identities.");
                return doc;
            }
            doc.Diagnostics.Add($"Typed PC FF0 unavailable: {replay.Error} Legacy scene scan used.");
        }

        // 1. Сканируем последовательность чанков контейнера
        var chunks = ScanChunks(bytes, doc.Diagnostics);
        doc.Diagnostics.Add($"Recognized chunks: {chunks.Count}. Container size: {bytes.Length:N0} bytes.");

        // 2. Читаем Merged WOW Header в начале контейнера (если есть)
        var wowKeysFromHeader = ReadMergedWowHeader(bytes, doc.Diagnostics);

        // 3. Находим все WOW-записи
        var wowChunks = chunks.Where(c => c.Magic == MagicWow).ToList();
        doc.Diagnostics.Add($"WOW entries in header: {wowKeysFromHeader.Count}, .wow chunks: {wowChunks.Count}.");

        // Словарь всех GAO по их смещениям для быстрого поиска
        var gaoChunks = chunks.Where(c => c.Magic == MagicGao).ToList();
        doc.Diagnostics.Add($"GAO chunks in container: {gaoChunks.Count}.");

        var gaoMapByOffset = gaoChunks.ToDictionary(c => c.Offset);

        // Множество смещений GAO, привязанных к мирам через группы
        var assignedGaoOffsets = new HashSet<int>();

        // 4. Разбираем каждый мир
        for (int wIdx = 0; wIdx < wowChunks.Count; wIdx++)
        {
            var wChunk = wowChunks[wIdx];
            uint worldKey = wIdx < wowKeysFromHeader.Count ? wowKeysFromHeader[wIdx] : (uint)wIdx;

            var worldData = ParseWorld(bytes, wChunk, worldKey, chunks, gaoMapByOffset, assignedGaoOffsets, doc.Diagnostics);
            doc.Worlds.Add(worldData);
        }

        // 5. Обрабатываем непривязанные GAO (динамические, префабы или спавнеры)
        var unassignedGaos = gaoChunks.Where(g => !assignedGaoOffsets.Contains(g.Offset)).ToList();
        if (unassignedGaos.Count > 0)
        {
            doc.Diagnostics.Add($"Additional dynamic/prefab GAOs: {unassignedGaos.Count}.");
            var extraWorld = new WorldData
            {
                Key = 0xFFFFFFFF,
                Name = "Additional / dynamic objects",
                RecordRange = new SourceRange(-1, 0)
            };

            int instIdx = 0;
            foreach (var gChunk in unassignedGaos)
            {
                var sceneObj = ParseGao(bytes, gChunk, 0, instIdx++, doc.Diagnostics);
                if (sceneObj != null)
                {
                    extraWorld.Objects.Add(sceneObj);
                }
            }

            if (extraWorld.Objects.Count > 0)
            {
                doc.Worlds.Add(extraWorld);
            }
        }

        // 6. Регистрируем все обнаруженные ресурсы (GRO, Material) в каталоге ресурсов документа
        foreach (var w in doc.Worlds)
        {
            foreach (var obj in w.Objects)
            {
                if (obj.GeometryKey != 0 && !doc.Resources.ContainsKey(obj.GeometryKey))
                {
                    doc.Resources[obj.GeometryKey] = new ResourceItem
                    {
                        Id = new ResourceId(obj.GeometryKey, containerName),
                        Type = ResourceType.Geometry,
                        State = ResolutionState.Unresolved
                    };
                }

                if (obj.MaterialKey != 0 && !doc.Resources.ContainsKey(obj.MaterialKey))
                {
                    doc.Resources[obj.MaterialKey] = new ResourceItem
                    {
                        Id = new ResourceId(obj.MaterialKey, containerName),
                        Type = ResourceType.Material,
                        State = ResolutionState.Unresolved
                    };
                }
            }
        }

        doc.Diagnostics.Add($"Document created: worlds = {doc.Worlds.Count}, objects = {doc.Worlds.Sum(w => w.Objects.Count)}, confirmed GRO/material references = {doc.Resources.Count}.");
        doc.ReservedResourceKeys.UnionWith(doc.Worlds.SelectMany(world => world.Objects).Select(sceneObject => sceneObject.GaoKey));
        doc.ReservedResourceKeys.UnionWith(doc.Resources.Keys);
        return doc;
    }

    public static SceneDocument Load(Archive archive, Entry entry)
    {
        byte[] bytes = archive.Read(entry);
        return Load(bytes, entry.Name, archive.PathName, archive.Sha256, worldListKey: entry.Key);
    }

    public static SceneDocument Load(Archive archive, uint key)
    {
        var entry = archive.Entries.FirstOrDefault(e => e.Key == key)
            ?? throw new KeyNotFoundException($"Container key {key:X8} not found in archive {archive.PathName}.");
        return Load(archive, entry);
    }

    public static SceneDocument Load(Archive archive, string keyHex)
    {
        var entry = archive.Entries.FirstOrDefault(e => e.Id.Equals(keyHex, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Container ID {keyHex} not found in archive {archive.PathName}.");
        return Load(archive, entry);
    }

    private static List<uint> ReadMergedWowHeader(byte[] bytes, List<string> diagnostics)
    {
        var keys = new List<uint>();
        if (bytes.Length < 4) return keys;

        int headerLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
        if (headerLen <= 0 || headerLen > bytes.Length - 4 || headerLen % 8 != 0)
        {
            diagnostics.Add("Missing or nonstandard Merged WOW header; locating worlds by .wow signatures.");
            return keys;
        }

        int count = headerLen / 8;
        int pos = 4;
        for (int i = 0; i < count; i++)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos, 4));
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4, 4));
            if (magic == MagicWow)
            {
                keys.Add(key);
            }
            pos += 8;
        }

        diagnostics.Add($"Merged WOW Header: declared {count} worlds, valid .wow keys: {keys.Count}.");
        return keys;
    }

    public static List<ChunkInfo> ScanChunks(byte[] bytes, List<string> diagnostics)
    {
        var chunks = new List<ChunkInfo>();
        int pos = 0;

        // Если в начале контейнера есть Merged WOW Header, пропускаем его таблицу
        if (bytes.Length >= 4)
        {
            int headerLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            if (headerLen > 0 && headerLen <= bytes.Length - 4 && headerLen % 8 == 0)
            {
                // Проверяем, действительно ли там WOW-ключи
                uint firstMagic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
                if (firstMagic == MagicWow)
                {
                    pos = 4 + headerLen;
                }
            }
        }

        while (pos + 4 <= bytes.Length)
        {
            uint firstDw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos, 4));

            // Пропуск нулевых bytesов выравнивания
            if (firstDw == 0)
            {
                pos += 4;
                continue;
            }

            // Специальные блоки анимаций и звука Jade (.smd и .snk)
            if (firstDw == TagSmd || firstDw == TagSnk)
            {
                if (pos + 8 > bytes.Length) break;
                int realLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4, 4));
                if (realLen < 0 || pos + 8 + realLen > bytes.Length)
                {
                    diagnostics.Add($"Warning: corrupted {((firstDw == TagSmd) ? ".smd" : ".snk")} block at offset 0x{pos:X8}.");
                    break;
                }
                chunks.Add(new ChunkInfo(pos, realLen + 8, firstDw, true));
                pos += 8 + realLen;
                continue;
            }

            int sz = (int)firstDw;
            if (sz < 0 || pos + 4 + sz > bytes.Length)
            {
                // Если стандартный размер выходит за границы, сканируем вперед на 4 bytesа
                pos += 4;
                continue;
            }

            uint magic = sz >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4, 4)) : 0;
            chunks.Add(new ChunkInfo(pos, sz + 4, magic, false));
            pos += 4 + sz;
        }

        return chunks;
    }

    private static WorldData ParseWorld(
        byte[] bytes,
        ChunkInfo wChunk,
        uint worldKey,
        List<ChunkInfo> allChunks,
        Dictionary<int, ChunkInfo> gaoMapByOffset,
        HashSet<int> assignedGaoOffsets,
        List<string> diagnostics)
    {
        int p = wChunk.Offset + 4; // Пропускаем 4-байтный заголовок размера
        int end = wChunk.Offset + wChunk.Size;

        // Поля WOW:
        // +0:  4s magic (".wow")
        // +4:  uint version (3)
        // +8:  uint opaque state/flags (not an object count in PC containers)
        // +12: uint ambientColor
        // +16: char name[60]
        // +76: Matrix (64 bytes floats + 4 bytes type)
        // +148: float fov
        // +152: uint backgroundColor
        // +156: uint ambientColor2
        // +160: int lodCut
        // +164: uint worldGrid1Key
        // +168: uint worldGrid2Key
        // +172: uint gameObjectGroupKey
        // +176: uint allNetworksKey
        // +180: uint worldTextKey

        string worldName = "";
        uint grid1Key = 0xFFFFFFFF, grid2Key = 0xFFFFFFFF, groupKey = 0xFFFFFFFF;
        uint networksKey = 0, textKey = 0;

        if (p + 180 <= bytes.Length)
        {
            worldName = ReadFixedString(bytes.AsSpan(p + 16, 60));
            grid1Key = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 160, 4));
            grid2Key = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 164, 4));
            groupKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 168, 4));
            networksKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 172, 4));
            textKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 176, 4));
        }

        var worldData = new WorldData
        {
            Key = worldKey,
            Name = string.IsNullOrEmpty(worldName) ? $"World {worldKey:X8}" : worldName,
            Lighting = PcWorldLightingReader.Read(bytes.AsSpan(p, end - p), worldKey),
            RecordRange = new SourceRange(wChunk.Offset, wChunk.Size)
        };

        // Ищем чанк группы игровых objects
        // В потоке FF0 он следует сразу после сеток мира (Grid1, Grid2)
        int streamPos = end;
        if (grid1Key != 0 && grid1Key != 0xFFFFFFFF && streamPos + 4 <= bytes.Length)
        {
            int g1Sz = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(streamPos, 4));
            streamPos += 4 + g1Sz;
        }
        if (grid2Key != 0 && grid2Key != 0xFFFFFFFF && streamPos + 4 <= bytes.Length)
        {
            int g2Sz = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(streamPos, 4));
            streamPos += 4 + g2Sz;
        }

        var groupKeys = new List<uint>();
        if (groupKey != 0 && groupKey != 0xFFFFFFFF && streamPos + 4 <= bytes.Length)
        {
            int gSz = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(streamPos, 4));
            if (gSz > 0 && streamPos + 4 + gSz <= bytes.Length && (gSz % 4 == 0))
            {
                int count = gSz / 4;
                for (int i = 0; i < count; i++)
                {
                    uint gaoKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(streamPos + 4 + i * 4, 4));
                    groupKeys.Add(gaoKey);
                }
                streamPos += 4 + gSz;
            }
        }

        // Пропускаем AllNetworks чанки, если они присутствуют перед объектами
        if (networksKey != 0 && networksKey != 0xFFFFFFFF)
        {
            // Networks могут содержать 2 или 3 блока
            while (streamPos + 4 <= bytes.Length)
            {
                uint nextMagic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(streamPos + 4, 4));
                if (nextMagic == MagicGao || nextMagic == MagicWow) break;

                int blockSz = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(streamPos, 4));
                if (blockSz <= 0 || streamPos + 4 + blockSz > bytes.Length)
                {
                    streamPos += 4;
                    break;
                }
                streamPos += 4 + blockSz;
            }
        }

        // Теперь читаем последовательные GAO мира
        int matched = 0;
        int currStream = streamPos;
        for (int i = 0; i < groupKeys.Count; i++)
        {
            uint gKey = groupKeys[i];
            if (currStream + 4 > bytes.Length) break;

            if (gaoMapByOffset.TryGetValue(currStream, out var gChunk))
            {
                var sceneObj = ParseGao(bytes, gChunk, gKey, i, diagnostics);
                if (sceneObj != null)
                {
                    worldData.Objects.Add(sceneObj);
                    assignedGaoOffsets.Add(gChunk.Offset);
                    matched++;
                }
                currStream += gChunk.Size;
            }
            else
            {
                // Если поток рассинхронизировался, ищем следующий GAO в окрестности
                var nextGao = allChunks.FirstOrDefault(c => c.Magic == MagicGao && c.Offset >= currStream);
                if (nextGao != null && nextGao.Offset - currStream < 1024)
                {
                    currStream = nextGao.Offset;
                    var sceneObj = ParseGao(bytes, nextGao, gKey, i, diagnostics);
                    if (sceneObj != null)
                    {
                        worldData.Objects.Add(sceneObj);
                        assignedGaoOffsets.Add(nextGao.Offset);
                        matched++;
                    }
                    currStream += nextGao.Size;
                }
                else
                {
                    break;
                }
            }
        }

        diagnostics.Add($"World '{worldData.Name}' (0x{worldKey:X8}): group contains {groupKeys.Count} keys, matched GAOs: {matched}.");
        return worldData;
    }

    internal static SceneObject? ParseGao(byte[] bytes, ChunkInfo gChunk, uint gaoKey, int instanceIndex, List<string> diagnostics)
    {
        int p = gChunk.Offset + 4; // Пропускаем заголовок размера
        int dataSize = gChunk.Size - 4;
        if (dataSize < 90 || p + dataSize > bytes.Length) return null;

        uint editorFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 4, 4));
        uint idFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 8, 4));
        uint mask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 12, 4));

        // 6 bytesов состояния: uc_Secto, uc_VisiCoeff, uc_LOD_Vis, uc_LOD_Dist, uc_DesignFlags, c_FixFlags
        // Матрица трансформации всегда начинается со смещения 22 в данных GAO
        int matOffsetInGao = 22;
        int matGlobalOffset = p + matOffsetInGao;

        var matrixByteRange = new SourceRange(matGlobalOffset, 68);
        var jadeMatrix = JadeMatrix.Read(bytes.AsSpan(matGlobalOffset, 68));
        var transformData = jadeMatrix.ToTransformData(matrixByteRange);

        // Bounding Volume
        bool isOBBox = (idFlags & IdentityFlagOBBox) != 0;
        int bvSize = isOBBox ? 48 : 24; // 4 вектора для OBBox или 2 вектора для AABB

        int visuPos = p + matOffsetInGao + 68 + bvSize; // 68 bytes = матрица 64 bytesа + 4 bytesа тип матрицы

        uint geoKey = 0;
        uint matKey = 0;
        int currP = visuPos;
        uint[] rliColors = [];
        uint drawFlags = 0x01000008;

        if ((idFlags & IdentityFlagBaseObject) != 0 && (idFlags & IdentityFlagVisu) != 0)
        {
            if (currP + 20 <= p + dataSize)
            {
                geoKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP, 4));
                matKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP + 4, 4));
                drawFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP + 8, 4));
                uint dispOrder = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP + 12, 4));
                uint rli = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP + 16, 4));
                currP += 20;

                if (rli == MagicRli)
                {
                    currP += 4; // RLI key
                }
                else if (rli != 0 && rli < 65536)
                {
                    if (currP <= p + dataSize - (int)rli * 4)
                    {
                        rliColors = new uint[rli];
                        for (int i = 0; i < rliColors.Length; i++)
                            rliColors[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP + i * 4, 4));
                    }
                    currP += (int)(rli * 4); // Instance colors; alpha also controls fog transparency.
                }
            }
        }

        // Иерархия (Hierarchy)
        uint fatherKey = 0;
        Matrix4x4? hierarchyLocalMatrix = null;
        if ((idFlags & IdentityFlagHierarchy) != 0)
        {
            if (currP + 4 <= p + dataSize)
            {
                fatherKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(currP, 4));
                if (currP + 4 + 68 <= p + dataSize)
                {
                    // Iris/Jaded serialize father key followed by the distinct 68-byte
                    // MATH matrix used as st_LocalMatrix (OBJload.c hierarchy reader).
                    hierarchyLocalMatrix = AnimationMath.Matrix(bytes, currP + 4);
                }
                currP += 4 + 68; // FatherKey + LocalMatrix
            }
        }

        // Object name: длина и C-строка в конце GAO-записи
        string name = ReadNameFromEndOfGao(bytes.AsSpan(p, dataSize));
        if (string.IsNullOrEmpty(name))
        {
            name = $"Object_{gaoKey:X8}";
        }

        // Анализ доступности редактирования
        var caps = DetermineCapabilities(idFlags, fatherKey);
        if (!jadeMatrix.IsValid)
        {
            caps.Flags = EditCapabilities.None;
            caps.RestrictionReason = "Matrix contains unsupported data; preview only.";
        }

        return new SceneObject
        {
            Id = (gaoKey != 0) ? $"{gaoKey:X8}_{instanceIndex}" : $"GAO_{gChunk.Offset:X8}_{instanceIndex}",
            GaoKey = gaoKey,
            Name = name,
            ParentId = (fatherKey != 0 && fatherKey != 0xFFFFFFFF) ? $"{fatherKey:X8}" : null,
            SerializedHierarchyLocalMatrix = hierarchyLocalMatrix,
            Transform = transformData,
            InitialTransform = transformData.Clone(),
            GeometryKey = (geoKey != 0 && geoKey != 0xFFFFFFFF) ? geoKey : 0,
            MaterialKey = (matKey != 0 && matKey != 0xFFFFFFFF) ? matKey : 0,
            Flags = idFlags,
            ControlFlags = (mask & 0x7b9c0080)
                | ((mask & 0x20000000) != 0 ? GameObjectRuntimeFlags.Invisible : 0)
                | ((mask & 0x40000000) != 0 ? GameObjectRuntimeFlags.Inactive : 0),
            RliColors = rliColors,
            DrawFlags = drawFlags,
            SerializedStateFlags = mask,
            RecordRange = new SourceRange(gChunk.Offset, gChunk.Size),
            Capabilities = caps
        };
    }

    private static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> span)
    {
        float m11 = BinaryPrimitives.ReadSingleLittleEndian(span[0..4]);
        float m12 = BinaryPrimitives.ReadSingleLittleEndian(span[4..8]);
        float m13 = BinaryPrimitives.ReadSingleLittleEndian(span[8..12]);
        float m14 = BinaryPrimitives.ReadSingleLittleEndian(span[12..16]);

        float m21 = BinaryPrimitives.ReadSingleLittleEndian(span[16..20]);
        float m22 = BinaryPrimitives.ReadSingleLittleEndian(span[20..24]);
        float m23 = BinaryPrimitives.ReadSingleLittleEndian(span[24..28]);
        float m24 = BinaryPrimitives.ReadSingleLittleEndian(span[28..32]);

        float m31 = BinaryPrimitives.ReadSingleLittleEndian(span[32..36]);
        float m32 = BinaryPrimitives.ReadSingleLittleEndian(span[36..40]);
        float m33 = BinaryPrimitives.ReadSingleLittleEndian(span[40..44]);
        float m34 = BinaryPrimitives.ReadSingleLittleEndian(span[44..48]);

        float m41 = BinaryPrimitives.ReadSingleLittleEndian(span[48..52]);
        float m42 = BinaryPrimitives.ReadSingleLittleEndian(span[52..56]);
        float m43 = BinaryPrimitives.ReadSingleLittleEndian(span[56..60]);
        float m44 = BinaryPrimitives.ReadSingleLittleEndian(span[60..64]);

        return new Matrix4x4(
            m11, m12, m13, m14,
            m21, m22, m23, m24,
            m31, m32, m33, m34,
            m41, m42, m43, m44
        );
    }

    public static (Vector3 Translation, Quaternion Rotation, Vector3 Scale) DecomposeMatrix(Matrix4x4 m)
    {
        var translation = new Vector3(m.M41, m.M42, m.M43);

        if (Matrix4x4.Decompose(m, out Vector3 scale, out Quaternion rotation, out _))
        {
            return (translation, rotation, scale);
        }

        // Ручное разложение ориентации и масштаба при невырожденных столбцах
        var rowX = new Vector3(m.M11, m.M12, m.M13);
        var rowY = new Vector3(m.M21, m.M22, m.M23);
        var rowZ = new Vector3(m.M31, m.M32, m.M33);

        float sx = rowX.Length();
        float sy = rowY.Length();
        float sz = rowZ.Length();

        if (sx > 1e-6f) rowX /= sx; else rowX = Vector3.UnitX;
        if (sy > 1e-6f) rowY /= sy; else rowY = Vector3.UnitY;
        if (sz > 1e-6f) rowZ /= sz; else rowZ = Vector3.UnitZ;

        var rotMat = new Matrix4x4(
            rowX.X, rowX.Y, rowX.Z, 0,
            rowY.X, rowY.Y, rowY.Z, 0,
            rowZ.X, rowZ.Y, rowZ.Z, 0,
            0, 0, 0, 1
        );

        var rot = Quaternion.CreateFromRotationMatrix(rotMat);
        return (translation, rot, new Vector3(sx, sy, sz));
    }

    private static string ReadNameFromEndOfGao(ReadOnlySpan<byte> gaoData)
    {
        if (gaoData.Length < 8) return "";

        // Проверяем завершающий нулевой bytes
        if (gaoData[^1] == 0)
        {
            int sEnd = gaoData.Length - 1;
            int sStart = sEnd - 1;
            while (sStart >= 0 && gaoData[sStart] != 0)
            {
                sStart--;
            }

            int stringLen = sEnd - sStart - 1;
            if (stringLen is >= 2 and <= 256)
            {
                // Проверяем 4-байтную заявленную длину перед строкой
                if (sStart >= 3)
                {
                    int declaredLen = BinaryPrimitives.ReadInt32LittleEndian(gaoData.Slice(sStart - 3, 4));
                    if (declaredLen == stringLen + 1)
                    {
                        var slice = gaoData.Slice(sStart + 1, stringLen);
                        return Encoding.GetEncoding(1252).GetString(slice).Trim();
                    }
                }

                var fallbackSlice = gaoData.Slice(sStart + 1, stringLen);
                return Encoding.GetEncoding(1252).GetString(fallbackSlice).Trim();
            }
        }

        return "";
    }

    private static string ReadFixedString(ReadOnlySpan<byte> bytes)
    {
        int zero = bytes.IndexOf((byte)0);
        if (zero >= 0) bytes = bytes[..zero];
        if (bytes.Length == 0) return "";
        return Encoding.GetEncoding(1252).GetString(bytes).Trim();
    }

    private static ObjectEditCapabilities DetermineCapabilities(uint idFlags, uint fatherKey)
    {
        var caps = new ObjectEditCapabilities();

        if ((idFlags & IdentityFlagHierarchy) != 0 && fatherKey != 0 && fatherKey != 0xFFFFFFFF)
        {
            caps.Flags = EditCapabilities.None;
            caps.RestrictionReason = $"Object belongs to a hierarchy (parent: {fatherKey:X8}); local matrix synchronization is required.";
            return caps;
        }

        if ((idFlags & (IdentityFlagBone | IdentityFlagAnims)) != 0)
        {
            caps.Flags = EditCapabilities.None;
            caps.RestrictionReason = "Skeletal or animated object (position is controlled by animation).";
            return caps;
        }

        // Static игровой объект: перемещение и вращение подтверждены
        caps.Flags = EditCapabilities.Translate | EditCapabilities.Rotate;
        caps.RestrictionReason = "";
        return caps;
    }
}
