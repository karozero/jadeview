using System.Numerics;
using System.Globalization;
using System.Text;

namespace JadeView;

public sealed class Geometry
{
    public int Offset, Size;
    public uint Key, MaterialKey;
    public List<string> Objects = [];
    public string Name => Objects.Count > 0 ? Objects[0] : $"Unnamed · {(Key != 0 ? Key.ToString("X8") : $"@{Offset:X8}")}";
    public string ExportName => string.Concat(Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    public Vector3[] Positions = [];
    public Vector2[] Uvs = [];
    public uint[] VertexColors = [];
    public SkinBinding[] Skin = [];
    public List<Part> Parts = [];
    public List<GeometryBillboard> Billboards = [];
    public ParticleDefinition? Particles;
    public int ParticleStart,ParticlePart;
    public int Triangles => Parts.Sum(p => p.VertexIndices.Length / 3);
    public sealed record Part(int MaterialId, int[] VertexIndices, int[] UvIndices);
    public Vector3 Min => Positions.Aggregate(Vector3.Min);
    public Vector3 Max => Positions.Aggregate(Vector3.Max);
    public static Vector3 ToRender(Vector3 v) => new(v.X, v.Z, -v.Y);
    public string Obj()
    {
        var s = new StringBuilder("# JadeView: original Jade vertices and per-corner UVs\n");
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        foreach (var v in Positions) s.AppendLine($"v {F(v.X)} {F(v.Y)} {F(v.Z)}");
        foreach (var uv in Uvs) s.AppendLine($"vt {F(uv.X)} {F(1 - uv.Y)}");
        foreach (var part in Parts)
        {
            s.AppendLine($"g material_{part.MaterialId}");
            for (int i = 0; i < part.VertexIndices.Length; i += 3)
                s.AppendLine("f " + string.Join(' ', Enumerable.Range(i, 3).Select(j => Uvs.Length == 0 ? $"{part.VertexIndices[j] + 1}" : $"{part.VertexIndices[j] + 1}/{part.UvIndices[j] + 1}")));
        }
        return s.ToString();
    }
}
public sealed record GeometryBillboard(int Vertex, Vector3 Center, float HalfWidth, float HalfHeight, bool Wind);
public sealed record JadeMaterial(uint Key, int Offset, uint Diffuse, uint[] Textures, uint[] Children, int Type = 0, byte[]? Bytes = null);
public sealed class SceneAssets
{
    public List<Geometry> Models = [];
    public List<JadeMaterial> Materials = [];
    public List<TextureAsset> Textures = [];
    public Dictionary<uint, uint> ModelMaterials = [];
    public List<string> Diagnostics = [];
    public int DuplicateCount;
    public ResourceResolver? Resolver { get; set; }
    public JadeMaterial? MaterialFor(Geometry model, int part)
    {
        var m = Materials.FirstOrDefault(m => m.Key == model.MaterialKey);
        int id = model.Parts[part].MaterialId;
        if (m?.Children.Length > 0 && id >= 0 && m.Children.Length > 0)
            m = Materials.FirstOrDefault(x => x.Key == m.Children[Math.Min(id, m.Children.Length - 1)]);
        return m;
    }
    public List<TextureAsset?> TexturesFor(Geometry model)
    {
        if (Resolver != null)
            return Resolver.ResolveTexturesForGeometry(model, model.MaterialKey, useFallback: false);
        var material = Materials.FirstOrDefault(m => m.Key == model.MaterialKey);
        var result = new List<TextureAsset?>();
        for (int i = 0; i < model.Parts.Count; i++)
        {
            var m = MaterialFor(model, i);
            uint key = m?.Textures.FirstOrDefault() ?? 0;
            // Original PC BGE allocates .raw and its .tex wrapper consecutively;
            // FF8 links name the raw key, whereas material layers reference the wrapper.
            result.Add(Textures.FirstOrDefault(t => t.Name == key.ToString("X8")) ??
                (key > 0 ? Textures.FirstOrDefault(t => t.Name == (key - 1).ToString("X8")) : null));
        }
        return result;
    }
}

// PC BGE .gro records inside FF0: no editor normals, separate corner UV indices,
// 20-byte triangles, optional skin weights and collision boxes.
// PC collision layout checked against BGE.exe callback 00431910; later Jaded differs.
public static class Models
{
    sealed record Record(int Offset, int Size, int Type, Geometry? Model, uint Diffuse, uint[] References);
    sealed record Ref(uint Key, bool Material);
    public static SceneAssets Read(byte[] bytes, string? archivePath = null)
    {
        var replay = string.IsNullOrEmpty(archivePath) ? null : PcSceneLoader.Read(bytes, archivePath);
        if (replay?.Complete != true) replay = null;
        var scene = new SceneAssets();
        var records = new List<Record>();
        var pairs = new List<(int Offset, uint Geometry, uint Material)>();
        var objects = new Dictionary<uint, List<string>>();
        var lodGroups = new Dictionary<uint, uint[]>();
        string world = "";
        var chunks = replay == null ? SceneLoader.ScanChunks(bytes, scene.Diagnostics)
            : replay.Records.Where(r => r.Kind is 'm' or 'l' or 'z')
                .Select(r => new SceneLoader.ChunkInfo(r.Offset - 4, r.Size + 4, Preview.U32(bytes, r.Offset), false)).ToList();
        if (replay != null) scene.Diagnostics.Add($"Typed PC FF0: {replay.Records.Count} reads; SPE skipped {replay.SpeSkipped}, cached {replay.CachedSkipped}; full stream consumed.");
        if(replay!=null)foreach(var record in replay.Records.Where(r=>r.Kind=='m' && r.Size>=12 && Preview.I32(bytes,r.Offset)==11))
        {
            var particle=ParticleDefinition.Read(bytes,record.Offset+4,record.Size-4);
            if(particle!=null)scene.Models.Add(particle.Geometry(record.Key,record.Offset,record.Size));
            else scene.Diagnostics.Add($"Unsupported PAG at {record.Key:X8}, size {record.Size}.");
        }
        foreach (var chunk in chunks)
        {
            if (chunk.IsSpecialTag) continue;
            int p = chunk.Offset + 4;
            int size = chunk.Size - 4;
            if (size < 8 || p < 4 || p > bytes.Length - size) continue;
            if (bytes.AsSpan(p, 4).SequenceEqual(".wow"u8) && size >= 76)
            {
                world = ReadName(bytes.AsSpan(p + 16, 60));
                // WOW references and following resource blocks still need to be scanned.
                continue;
            }
            if (bytes.AsSpan(p, 4).SequenceEqual(".gao"u8) && size >= 114)
            {
                uint flags = Preview.U32(bytes, p + 8);
                if ((flags & 0x5000) == 0x5000)
                {
                    int visu = p + 90 + ((flags & 0x80000) != 0 ? 48 : 24);
                    if (visu + 8 <= p + size)
                    {
                        uint geometry = Preview.U32(bytes, visu);
                        pairs.Add((p, geometry, Preview.U32(bytes, visu + 4)));
                        // Gao stores its object name at the end of the serialized block.
                        int end = p + size;
                        if (bytes[end - 1] == 0)
                        {
                            int start = end - 2;
                            while (start >= visu + 8 && end - start <= 256 && bytes[start] != 0) start--;
                            if (start >= visu + 8 && bytes[start] == 0)
                            {
                                string name = ReadName(bytes.AsSpan(start + 1, end - start - 2));
                                if (name.Length > 0 && geometry is not (0 or uint.MaxValue))
                                {
                                    if (!objects.TryGetValue(geometry, out var names)) objects[geometry] = names = [];
                                    string label = world.Length > 0 ? world + " / " + name : name;
                                    if (!names.Contains(label)) names.Add(label);
                                }
                            }
                        }
                    }
                }
                p += size - 1; continue;
            }
            int type = Preview.I32(bytes, p);
            try
            {
                if (type == 1 && size >= 28)
                {
                    try
                    {
                        var model = ReadGeometry(bytes, p, size);
                        records.Add(new(p, size, type, model, 0, [])); scene.Models.Add(model); p += size - 1;
                    }
                    catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
                    {
                        scene.Diagnostics.Add($"GRO type 1 rejected at 0x{p:X8}, size {size}: {ex.Message}");
                    }
                }
                else if (type == 8 && size >= 15)
                {
                    // PC GRO_GeoStaticLOD callback 00433E90: byte count, six distance
                    // bytes, then count GRO keys. No editor padding byte in FF0.
                    int n = bytes[p + 4];
                    if (n is < 1 or > 6 || size != 11 + n * 4) continue;
                    uint[] refs = Enumerable.Range(0, n).Select(i => Preview.U32(bytes, p + 11 + i * 4)).ToArray();
                    if (refs.Any(k => k < 0x01000000 && k is not (0 or uint.MaxValue))) continue;
                    records.Add(new(p, size, type, null, 0, refs)); p += size - 1;
                }
                else if (type == 4 && size < 1032)
                {
                    int n = Preview.I32(bytes, p + 4);
                    if (n < 1 || n > 255 || size != 8 + n * 4) continue;
                    uint[] refs = Enumerable.Range(0, n).Select(i => Preview.U32(bytes, p + 8 + i * 4)).ToArray();
                    if (refs.Any(k => k < 0x01000000)) continue;
                    records.Add(new(p, size, type, null, 0xFFFFFF, refs)); p += size - 1;
                }
                else if (type == 3 && size == 36)
                {
                    float opacity = BitConverter.Int32BitsToSingle(Preview.I32(bytes, p + 20));
                    uint key = Preview.U32(bytes, p + 28);
                    if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) continue;
                    records.Add(new(p, size, type, null, Preview.U32(bytes, p + 8),
                        key is not (0 or uint.MaxValue) ? [key] : []));
                    p += size - 1;
                }
                else if (type == 5 && size >= 36)
                {
                    float opacity = BitConverter.Int32BitsToSingle(Preview.I32(bytes, p + 20));
                    uint first = Preview.U32(bytes, p + 28);
                    if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) continue;
                    // Original PC callbacks 00447670/00447970 have eight fixed DWORDs.
                    // Each 20-byte layer starts with a serialized next-layer marker.
                    int start = p + 36, n = (size - 36) / 20;
                    if ((size - 36) % 20 != 0 || (first == 0) != (n == 0)) continue;
                    bool validChain = true;
                    for (int j = 0; j < n; j++)
                        if ((Preview.U16(bytes, start + j * 20) != 0) != (j + 1 < n))
                            { validChain = false; break; }
                    if (!validChain) continue;
                    uint[] refs = Enumerable.Range(0, n).Select(i => Preview.U32(bytes, start + i * 20 + 16)).ToArray();
                    // The PC registry accepts short texture keys (e.g. 0x10/0x20).
                    // Dropping their complete material record also shifts GRO/GRM replay.
                    records.Add(new(p, size, type, null, Preview.U32(bytes, p + 8), refs)); p += size - 1;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
            { scene.Diagnostics.Add($"GRO type {type} rejected at 0x{p:X8}, size {size}: {ex.Message}"); }
        }

        // The binary stream omits keys of already-loaded FIX resources. Resolve references
        // in load order, using local material references to disambiguate skipped FIX keys.
        // If a key cannot be established, the viewer still exposes the real geometry by offset.
        if (replay != null)
        {
            var identities = replay.Records.Where(r => r.Kind == 'm').ToDictionary(r => r.Offset);
            foreach (var gao in replay.Records.Where(r => r.Kind == 'l' && r.Geometry is not (0 or uint.MaxValue)))
                scene.ModelMaterials[gao.Geometry] = gao.Material;
            foreach (var record in records)
            {
                if (!identities.TryGetValue(record.Offset, out var identity)) continue;
                uint key = identity.Key;
                if (record.Model is { } model) { model.Key = key; model.MaterialKey = scene.ModelMaterials.GetValueOrDefault(key); }
                else if (record.Type == 8) lodGroups[key] = record.References;
                else scene.Materials.Add(new(key, record.Offset, record.Diffuse,
                    record.Type is 3 or 5 ? record.References : [], record.Type == 4 ? record.References : [], record.Type,
                    bytes.AsSpan(record.Offset, record.Size).ToArray()));
            }
        }
        else
        {
        var queue = new List<Ref>(); var seen = new HashSet<uint>(); int pairIndex = 0;
        void Add(uint key, bool mat) { if (key is not (0 or uint.MaxValue) && seen.Add(key)) queue.Add(new(key, mat)); }
        bool Matches(uint key, Record record)
        {
            var refs = record.References.Where(k => k != uint.MaxValue && k != 0).ToArray();
            return refs.Length > 0 && refs.All(k => (k >> 16) == (key >> 16) && Math.Abs((long)k - key) < 512);
        }
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            while (pairIndex < pairs.Count && pairs[pairIndex].Offset < record.Offset)
            { var pair = pairs[pairIndex++]; Add(pair.Geometry, false); Add(pair.Material, true); scene.ModelMaterials[pair.Geometry] = pair.Material; }
            bool material = record.Type is 3 or 4 or 5;
            int chosen = -1;
            if (!material && i + 1 < records.Count && records[i + 1].Type is 3 or 4 or 5)
            {
                var following = records[i + 1];
                var possible = pairs.Where(pair => pair.Offset < record.Offset && Matches(pair.Material, following) && queue.Any(q => q.Key == pair.Geometry && !q.Material)).ToList();
                if (possible.Select(p => p.Geometry).Distinct().Count() == 1) chosen = queue.FindIndex(q => q.Key == possible[0].Geometry);
            }
            if (material)
            {
                var candidates = queue.Select((r, index) => (r, index)).Where(t => t.r.Material && Matches(t.r.Key, record)).ToList();
                if (candidates.Count == 1) chosen = candidates[0].index;
            }
            if (chosen < 0) chosen = queue.FindIndex(r => r.Material == material);
            uint key = chosen >= 0 ? queue[chosen].Key : 0;
            if (chosen >= 0) queue.RemoveRange(0, chosen + 1);
            if (record.Model is { } m)
            { m.Key = key; m.MaterialKey = scene.ModelMaterials.GetValueOrDefault(key); }
            else if (record.Type == 8)
            {
                if (key != 0) lodGroups[key] = record.References;
                foreach (uint child in record.References) Add(child, false);
            }
            else
            {
                scene.Materials.Add(new(key, record.Offset, record.Diffuse, record.Type is 3 or 5 ? record.References : [], record.Type == 4 ? record.References : [], record.Type,
                    bytes.AsSpan(record.Offset, record.Size).ToArray()));
                if (record.Type == 4) foreach (uint k in record.References) Add(k, true);
            }
        }
        }
        // Preview the first available LOD. Preserve its parent key so GAO references
        // resolve without duplicating triangles from all LOD levels at once.
        var byKey = scene.Models.Where(m => m.Key != 0).GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.First());
        Geometry? ResolveLod(uint key, HashSet<uint> path)
        {
            if (byKey.TryGetValue(key, out var mesh)) return mesh;
            if (path.Count >= 64 || !path.Add(key)) return null;
            if (lodGroups.TryGetValue(key, out var children))
                foreach (uint child in children)
                    if (ResolveLod(child, path) is { } found) { path.Remove(key); return found; }
            path.Remove(key);
            return null;
        }
        foreach (uint key in lodGroups.Keys)
        {
            if (byKey.ContainsKey(key)) continue;
            if (ResolveLod(key, []) is not { } mesh) continue;
            var alias = new Geometry { Key = key, Offset = mesh.Offset, Size = mesh.Size,
                Positions = mesh.Positions, Uvs = mesh.Uvs, Parts = mesh.Parts,
                MaterialKey = scene.ModelMaterials.GetValueOrDefault(key) };
            scene.Models.Add(alias);
            byKey[key] = alias;
        }
        foreach (var m in scene.Models)
            if (objects.TryGetValue(m.Key, out var names)) m.Objects.AddRange(names);
        // Identical bytes can belong to distinct GRO keys referenced by distinct GAOs.
        // Keep those resource identities; deleting a key leaves its objects without meshes.
        var unique = new Dictionary<(string Hash, uint Material, uint Key), Geometry>();
        foreach (var m in scene.Models)
        {
            var identity = (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(m.Offset, m.Size))), m.MaterialKey, m.Key);
            if (unique.TryGetValue(identity, out var existing))
            {
                foreach (string name in m.Objects) if (!existing.Objects.Contains(name)) existing.Objects.Add(name);
                scene.DuplicateCount++;
            }
            else unique.Add(identity, m);
        }
        scene.Models = unique.Values.OrderBy(m => m.Objects.Count == 0).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return scene;
    }

    static string ReadName(ReadOnlySpan<byte> bytes)
    {
        int zero = bytes.IndexOf((byte)0);
        if (zero >= 0) bytes = bytes[..zero];
        if (bytes.Length is < 2 or > 255) return "";
        foreach (byte b in bytes) if (b < 32 || b == 127) return "";
        string name = Encoding.GetEncoding(1252).GetString(bytes).Trim();
        return name.Any(char.IsLetter) ? name : "";
    }

    public static Geometry ReadGeometry(byte[] b, int start, int size)
    {
        int p = start + 4, end = start + size;
        int U32() { Need(4); int v = Preview.I32(b, p); p += 4; return v; }
        int U16() { Need(2); int v = Preview.U16(b, p); p += 2; return v; }
        float F32() => BitConverter.Int32BitsToSingle(U32());
        void Need(int n) { if (n < 0 || p > end - n) throw new InvalidDataException("Truncated GRO."); }
        void Skip(int n) { Need(n); p += n; }
        int version = 0, vertices = U32();
        if ((uint)vertices == 0xC0DE2002)
        { version = U32(); vertices = U32(); int levels = U32(); if (levels != 0) U32(); if (version is < 0 or > 4) throw new InvalidDataException(); }
        int colors = U32(), uvs = U32(), elements = U32(), mrm = U32(); uint flags = (uint)U32();
        if (vertices < 3 || vertices > 65535 || colors < 0 || colors > vertices * 2 || uvs < 0 || uvs > 65535 || elements < 1 || elements > 128 || mrm is not (0 or 1)) throw new InvalidDataException();
        var skin = new List<SkinBinding>();
        if ((flags & 0xC0DE2002) == 0xC0DE2002)
        {
            U16(); int bones = U16(); if (bones > 512) throw new InvalidDataException();
            for (int i = bones - 1; i >= 0; i--)
            {
                int bone=U16(), count=U16();Need(68);var bind=AnimationMath.Matrix(b,p);Skip(68);
                var weights=new uint[count];for(int j=0;j<count;j++)weights[j]=(uint)U32();
                skin.Add(new(bone,bind,weights));
            }
        }
        if ((flags & 1) != 0)
        {
            int boxes = U32(); if (boxes < 0 || boxes > 10000) throw new InvalidDataException();
            for (int i = 0; i < boxes; i++)
            {
                int n = U32(); if (n < 0 || n > 10000) throw new InvalidDataException(); Skip(24);
                for (int j = 0; j < n; j++) { U16(); int triangles = U16(); Skip(triangles * 2); }
            }
            // Original PC callback 00431910 reads positions immediately after boxes.
            // The recursive collision tree belongs to a later format; consuming it here
            // interprets vertex floats as tree links and rejects otherwise valid meshes.
        }
        var model = new Geometry { Offset = start, Size = size, Positions = new Vector3[vertices], Uvs = new Vector2[uvs] };
        for (int i = 0; i < vertices; i++)
        {
            var v = new Vector3(F32(), F32(), F32());
            // Real PC meshes in FF007061/FF00ABB4/FF00AE8A exceed 100000 units.
            // File bounds and index validation protect parsing; world size is not a format limit.
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)) throw new InvalidDataException();
            model.Positions[i] = v;
        }
        if (model.Min == model.Max) throw new InvalidDataException();
        if (mrm != 0) Skip(vertices * 2 + 4 + (version == 0 ? 32 : 0));
        model.VertexColors = new uint[colors];
        for (int i = 0; i < colors; i++) model.VertexColors[i] = unchecked((uint)U32());
        for (int i = 0; i < uvs; i++) { float u = F32(), v = F32(); if (!float.IsFinite(u) || !float.IsFinite(v)) throw new InvalidDataException(); model.Uvs[i] = new(u, v); }
        var headers = new (int Count, int Material, int Mrm, int Used)[elements];
        for (int i = 0; i < elements; i++)
        {
            var h = (Count: U32(), Material: U32(), Mrm: U32(), Used: U32());
            if (h.Count < 0 || h.Count > 100000 || h.Material < -1 || h.Material > 65535 || h.Mrm is not (0 or 1) || h.Used < 0 || h.Used > vertices) throw new InvalidDataException(); headers[i] = h;
        }
        foreach (var h in headers)
        {
            var vi = new int[h.Count * 3]; var ui = new int[vi.Length];
            for (int i = 0; i < h.Count; i++)
            {
                for (int j = 0; j < 3; j++) { vi[i * 3 + j] = U16(); if (vi[i * 3 + j] >= vertices) throw new InvalidDataException(); }
                for (int j = 0; j < 3; j++) { ui[i * 3 + j] = U16(); if (uvs > 0 && ui[i * 3 + j] >= uvs) throw new InvalidDataException(); }
                Skip(8);
            }
            Skip(h.Mrm * 8 + h.Used * 2);
            if (vi.Length > 0) model.Parts.Add(new(h.Material, vi, ui));
        }
        model.Skin = skin.ToArray();
        if (model.Triangles == 0) throw new InvalidDataException();
        return model;
    }
}
