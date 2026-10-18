using System.Security.Cryptography;
using System.Text.Json;

namespace JadeView;

/// <summary>
/// PC texture registry replay. Rules are taken from BGE callbacks 00447670,
/// 0046B8A0, 0043CD20 and 0042C6F0. No scene IDs or generated key maps are inputs.
/// Unsupported modifier layouts abort the replay rather than shifting every later key.
/// </summary>
internal static class PcTextureLoader
{
    public sealed record ReadEvent(string Phase, int Offset, int Size, uint Key, int Type);
    public sealed record ModifierEvent(int Offset, int Type, int Consumed);
    public sealed record Result(List<TextureAsset> Textures, List<ReadEvent> Reads,
        List<ModifierEvent> Modifiers, int InitialKeys, int PaletteCount, int EndOffset);
    private sealed record Header(uint Key, int Offset, int Size, int Type, uint Raw, uint Palette, uint TrueColor);
    private sealed record Request(int Offset, uint[] Keys);
    private static ReadOnlySpan<byte> Signature => [0x34, 0x12, 0xd0, 0xca, 0xff, 0, 0xff, 0, 0xde, 0xc0, 0xde, 0xc0];

    public static Result Read(byte[] ff0, byte[] ff8)
    {
        var modifiers = new List<ModifierEvent>();
        var requests = CollectRequests(ff0, modifiers);
        var registry = new List<uint>();
        var seen = new HashSet<uint>();
        var paletteKeys = new List<uint>();
        var paletteSeen = new HashSet<uint>();
        var headers = new List<Header>();
        var reads = new List<ReadEvent>();
        void Add(uint key) { if (key != uint.MaxValue && seen.Add(key)) registry.Add(key); }
        foreach (var request in requests.OrderBy(r => r.Offset))
            foreach (uint key in request.Keys) Add(key);
        int initial = registry.Count, position = 0;
        if (initial == 0) throw new InvalidDataException("PC texture registry is empty.");
        for (int index = 0; index < registry.Count; index++)
        {
            int offset = position, size = ChunkSize(ff8, offset), p = offset + 4;
            if (!IsHeader(ff8, offset, size)) throw new InvalidDataException($"Expected PC texture header at {offset:X8}, registry index {index}.");
            int type = Preview.U16(ff8, p + 6) & 255;
            uint raw = uint.MaxValue, palette = uint.MaxValue, trueColor = uint.MaxValue;
            if (type == 7)
            {
                // Manager slot order 0,1,2,3. Native chooses a complete raw/palette
                // pair when TC is absent; this replay uses the PC manager's zero caps.
                for (int slot = 32; slot + 4 <= size && slot < 80; slot += 12)
                {
                    raw = Preview.U32(ff8, p + slot);
                    palette = slot + 8 <= size ? Preview.U32(ff8, p + slot + 4) : uint.MaxValue;
                    trueColor = slot + 12 <= size ? Preview.U32(ff8, p + slot + 8) : uint.MaxValue;
                    if (raw != uint.MaxValue || palette != uint.MaxValue || trueColor != uint.MaxValue) break;
                }
                if (raw != uint.MaxValue && palette != uint.MaxValue && trueColor == uint.MaxValue)
                {
                    if (paletteSeen.Add(palette)) paletteKeys.Add(palette);
                    Add(raw);
                }
                else if (trueColor != uint.MaxValue) Add(trueColor);
                else throw new InvalidDataException($"Unsupported PC texture slot at {offset:X8}.");
            }
            else if (type == 4)
            {
                // Sprite generator callback 0043BFE0 registers its underlying texture.
                if (size < 44) throw new InvalidDataException("Truncated sprite generator.");
                Add(Preview.U32(ff8, p + 40));
            }
            else if (type is not (1 or 5 or 6))
                throw new InvalidDataException($"Unsupported PC texture type {type} at {offset:X8}.");
            headers.Add(new(registry[index], offset, size, type, raw, palette, trueColor));
            reads.Add(new("header", offset, size, registry[index], type));
            position = checked(p + size);
            // Procedural headers carry an additional serialized word outside the size.
            if (type == 5 && size == 96) position = checked(position + 4);
            if (registry.Count > 65535) throw new InvalidDataException("PC texture registry overflow.");
        }

        var palettes = new Dictionary<uint, byte[]>();
        foreach (uint key in paletteKeys)
        {
            int size = ChunkSize(ff8, position);
            if (size is not (48 or 64 or 768 or 1024)) throw new InvalidDataException($"Unexpected palette size {size} at {position:X8}.");
            palettes[key] = ff8.AsSpan(position + 4, size).ToArray();
            reads.Add(new("palette", position, size, key, 0));
            position += 4 + size;
        }

        var textures = new List<TextureAsset>();
        var byKey = headers.ToDictionary(h => h.Key);
        // Wrappers convert their raw source separately for each palette. Shared raw keys
        // therefore do not mean shared image offsets; the engine rereads that source.
        foreach (var root in headers.Where(h => h.Type is 7 or 1))
        {
            if (root.Type == 7 && root.TrueColor != uint.MaxValue)
                throw new InvalidDataException("True-color wrapper rendering is not implemented in PC replay.");
            int size = ChunkSize(ff8, position);
            while (!IsHeader(ff8, position, size))
            {
                // Auxiliary image payloads are separate framed records. They are not
                // texture requests and must not advance the texture registry cursor.
                position += 4 + size;
                size = ChunkSize(ff8, position);
            }
            int offset = position, p = offset + 4;
            uint raw = root.Type == 1 ? root.Key : root.Raw;
            int format = Preview.U16(ff8, p + 6);
            int width = Preview.U16(ff8, p + 8), height = Preview.U16(ff8, p + 10);
            uint definitionKey = root.Type == 1 ? root.Key : root.Raw;
            if (!byKey.TryGetValue(definitionKey, out var definition)) throw new InvalidDataException("Missing raw texture descriptor.");
            int definitionStart = definition.Offset + 4;
            if (Preview.U16(ff8, definitionStart + 6) != format || Preview.U16(ff8, definitionStart + 8) != width
                || Preview.U16(ff8, definitionStart + 10) != height)
                throw new InvalidDataException($"Raw descriptor and pixel read disagree at {offset:X8}.");
            reads.Add(new("pixels", offset, size, raw, format & 255));
            if (root.Type == 1)
            {
                if (format != 0x1001 || size <= 32) throw new InvalidDataException("TGA root and image disagree.");
                textures.Add(new(root.Key.ToString("X8"), width, height, ff8.AsSpan(p + 32, size - 32).ToArray(),
                    offset, format, null, raw, 0, root.Key));
            }
            else
            {
                if (format is not (0x4006 or 0x5006)) throw new InvalidDataException("Paletted root and image disagree.");
                byte[] palette = palettes[root.Palette];
                int colors = format == 0x5006 ? 16 : 256, stride = palette.Length / colors;
                int pixels = checked(width * height), required = colors == 16 ? (pixels + 1) / 2 : pixels;
                if (pixels <= 0 || pixels > 16 * 1024 * 1024 || required > size - 32 || stride is not (3 or 4))
                    throw new InvalidDataException("PC texture dimensions and palette disagree.");
                byte[] tga = new byte[18 + pixels * 4];
                tga[2] = 2; tga[16] = 32; tga[17] = 8;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
                for (int i = 0; i < pixels; i++)
                {
                    int sample = ff8[p + 32 + (colors == 16 ? i / 2 : i)];
                    if (colors == 16) sample = i % 2 == 0 ? sample >> 4 : sample & 15;
                    palette.AsSpan(sample * stride, 3).CopyTo(tga.AsSpan(18 + i * 4));
                    tga[18 + i * 4 + 3] = stride == 4 ? palette[sample * stride + 3] : (byte)255;
                }
                textures.Add(new(root.Key.ToString("X8"), width, height, tga, offset, format,
                    palette, raw, root.Palette, root.Key));
            }
            position += 4 + size;
        }
        if (position != ff8.Length) throw new InvalidDataException($"PC replay left {ff8.Length - position} unconsumed bytes.");
        return new(textures, reads, modifiers, initial, palettes.Count, position);
    }

    private static List<Request> CollectRequests(byte[] bytes, List<ModifierEvent> modifiers)
    {
        var result = new List<Request>();
        foreach (var chunk in SceneLoader.ScanChunks(bytes, []))
        {
            if (chunk.IsSpecialTag) continue;
            int p = chunk.Offset + 4, size = chunk.Size - 4;
            if (size < 4) continue;
            if (bytes.AsSpan(p, 4).SequenceEqual(".gao"u8))
            {
                ReadModifiers(bytes, p, size, modifiers, result);
                continue;
            }
            uint type = Preview.U32(bytes, p);
            if (type == 5 && size >= 36 && (size - 36) % 20 == 0)
            {
                int count = (size - 36) / 20;
                if ((Preview.U32(bytes, p + 28) == 0) != (count == 0)) continue;
                if (Enumerable.Range(0, count).Any(i => (Preview.U16(bytes, p + 36 + i * 20) != 0) != (i + 1 < count))) continue;
                // Short keys 0x10..0xF0 are real registrations; never discard by magnitude.
                result.Add(new(p, Enumerable.Range(0, count).Select(i => Preview.U32(bytes, p + 52 + i * 20)).ToArray()));
            }
            else if (type == 3 && size == 36) result.Add(new(p, [Preview.U32(bytes, p + 28)]));
        }
        return result;
    }

    private static void ReadModifiers(byte[] b, int start, int size, List<ModifierEvent> modifiers, List<Request> requests)
    {
        int end = checked(start + size), p = start + 90;
        uint U(int offset) { if (offset < start || offset > end - 4) throw new InvalidDataException("GAO cursor outside record."); return Preview.U32(b, offset); }
        void Skip(int count) { if (count < 0 || p > end - count) throw new InvalidDataException("Truncated GAO component."); p += count; }
        uint flags = U(start + 8) & ~4u;
        Skip((flags & 0x80000) != 0 ? 48 : 24);
        if ((flags & 0x1000) != 0)
        {
            if ((flags & 0x4000) != 0) { int colors = checked((int)U(p + 16)); Skip(checked(20 + colors * 4)); }
            if ((flags & 0x400000) != 0) Skip(72);
            if ((flags & 2) != 0) Skip(16);
            if ((flags & 0x200000) != 0) { int count = checked((int)U(p)); Skip(checked(4 + count * ((flags & 0x1000000) != 0 ? 8 : 68))); }
        }
        if ((flags & 0x2000) == 0) return;
        bool hasModifiers = U(p + 4) != 0;
        Skip(22);
        foreach (uint flag in new uint[] { 0x20, 0x2000000, 0x8000000, 0x40000, 0x10 })
            if ((flags & flag) != 0) Skip(4);
        if ((flags & 0x40) != 0) Skip(92);
        if (!hasModifiers) return;
        for (int index = 0; index < 1024; index++)
        {
            uint type = U(p);
            if (type == uint.MaxValue) return;
            int offset = p;
            Skip(8);
            int used = ModifierSize(b, p, end, type);
            modifiers.Add(new(offset, (int)type, used));
            if (type == 6) { uint key = U(p + 4); if (key is not (0 or uint.MaxValue)) requests.Add(new(offset, [key])); }
            Skip(used);
        }
        throw new InvalidDataException("Modifier chain exceeds PC limit.");
    }

    internal static int ModifierSize(byte[] b, int start, int end, uint type)
    {
        int p = start;
        int Count(int offset) { if (offset < start || offset > end - 4) throw new InvalidDataException("Modifier cursor outside record."); int n = Preview.I32(b, offset); if (n < 0 || n > end - start) throw new InvalidDataException("Invalid modifier count."); return n; }
        // Serialized byte counts from the original callbacks, not sizeof(runtime struct).
        switch (type)
        {
            case 1: return 20; // 004871F0
            case 3: return 28; // 00488BE0
            case 4: // 00487A40: indexed groups and per-element sets.
                int groups = Count(start + 12), sets = Count(start + 16); p += 20;
                for (int i = 0; i < groups; i++) { int n = Count(p); p = checked(p + 4 + n * 16); }
                for (int i = 0; i < sets; i++) { int n = Count(p); p = checked(p + 12 + n * 4); }
                return p - start;
            case 5: return 0; // empty callback 00489800
            case 6: return 53; // shadow 0046B8A0, packed engine format
            case 7: return Count(start) == 4 ? 16 : Count(start) == 28 ? 28 : 24; // 004891F0
            case 8: return 132; // 0045FCC0
            case 9: // 0046B0F0: versioned object-reference entries.
                int version = Count(start), count = Count(start + 8);
                return checked(16 + count * 8 + Math.Min(version, 3) * 4);
            case 10: return 240; // 0048AD50
            case 11: return 156; // 0048C200
            case 13: return 20; // 0048C5F0
            case 14: return 96; // 0048CD20
            case 15: return 40; // 0048CF70
            case 16: // 004679C0: optional saved texture-animation byte streams.
                if (Count(start + 4) == 0) return 8;
                int first = Count(start + 8);
                return checked(16 + first + Count(checked(start + 12 + first)));
            case 18: // Particle generator serialized versions (Iris BFModifierParticleGenerator).
                return Count(start + 4) switch { 0 => 75, 9 => 152, 11 => 158,
                    var particleVersion => throw new InvalidDataException($"Unsupported particle generator version {particleVersion}.") };
            case 19: return 120; // 0048D580
            case 20: return 60; // 00489AC0
            case 21: return 4; // transformed vertex snapshot modifier
            case 23: return checked(264 + Count(start + 260)); // 00488270
            case 24: return 44; // 00472170
            default: throw new InvalidDataException($"Unsupported PC modifier {type} at {start:X8}.");
        }
    }

    private static int ChunkSize(byte[] bytes, int p)
    {
        if (p < 0 || p > bytes.Length - 4) throw new InvalidDataException("Texture cursor outside container.");
        int size = Preview.I32(bytes, p);
        if (size < 0 || size > bytes.Length - p - 4) throw new InvalidDataException($"Invalid texture chunk at {p:X8}.");
        return size;
    }
    private static bool IsHeader(byte[] bytes, int offset, int size) => size >= 32 && bytes.AsSpan(offset + 24, 12).SequenceEqual(Signature);

    public static void Verify(string archivePath, string sceneId, string output, string? tracePath = null)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.First(e => e.Id.Equals(sceneId, StringComparison.OrdinalIgnoreCase));
        var texEntry = archive.Entries.First(e => e.Key == (0xFF800000 | (entry.Key & 0x7FFFF)));
        byte[] ff8 = archive.Read(texEntry);
        var result = Read(archive.Read(entry), ff8);
        var mismatches = new List<object>();
        int compared = 0;
        if (tracePath != null)
        {
            var expected = File.ReadLines(tracePath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => JsonDocument.Parse(l)).ToArray();
            var headerReads = result.Reads.Where(r => r.Phase == "header").ToArray();
            // The hangar capture ended after the descriptor pass; the workshop
            // capture also contains pixel reads. Compare the pass actually captured.
            var actual = expected.Length == headerReads.Length ? headerReads
                : result.Reads.Where(r => r.Phase != "palette").ToArray();
            compared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < compared; i++)
            {
                var e = expected[i].RootElement; var a = actual[i];
                string key = e.GetProperty("key").GetString()!; int size = e.GetProperty("size").GetInt32();
                byte[] captured = Convert.FromHexString(e.GetProperty("header").GetString()!);
                bool sameBytes = captured.Length <= a.Size && ff8.AsSpan(a.Offset + 4, captured.Length).SequenceEqual(captured);
                if (a.Key.ToString("X8") != key || a.Size != size || !sameBytes)
                    mismatches.Add(new { Event = i, Expected = key, Actual = a.Key.ToString("X8"), ExpectedSize = size, ActualSize = a.Size });
            }
            if (expected.Length != actual.Length) mismatches.Add(new { ExpectedCount = expected.Length, ActualCount = actual.Length });
            foreach (var e in expected) e.Dispose();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Scene = sceneId, result.InitialKeys, result.PaletteCount,
            result.EndOffset, Compared = compared, Mismatches = mismatches, Modifiers = result.Modifiers,
            Reads = result.Reads.Select(r => new { r.Phase, r.Offset, r.Size, Key = r.Key.ToString("X8"), r.Type }),
            Textures = result.Textures.Select(t => new { Key = t.WrapperKey.ToString("X8"), Raw = t.RawKey.ToString("X8"),
                Palette = t.PaletteKey.ToString("X8"), t.ChunkOffset, t.Width, t.Height,
                Sha256 = Convert.ToHexString(SHA256.HashData(t.Tga)) }) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{sceneId}: {result.Textures.Count} textures, {result.PaletteCount} palettes; {compared} trace reads compared, {mismatches.Count} differences; end {result.EndOffset}.");
        if (mismatches.Count != 0) throw new InvalidDataException("Native texture trace comparison failed.");
    }

    public static void Audit(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var rows = new List<object>();
        int supported = 0, failed = 0;
        foreach (var entry in archive.Entries.Where(e => (e.Key & 0xFFF00000) == 0xFF000000))
        {
            var paired = archive.Entries.FirstOrDefault(e => e.Key == (0xFF800000 | (entry.Key & 0x7FFFF)));
            if (paired == null) continue;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = Read(archive.Read(entry), archive.Read(paired));
                rows.Add(new { Scene = entry.Id, Supported = true, Textures = result.Textures.Count,
                    Headers = result.Reads.Count(r => r.Phase == "header"), Milliseconds = timer.ElapsedMilliseconds });
                supported++;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
            {
                rows.Add(new { Scene = entry.Id, Supported = false, Error = ex.Message });
                failed++;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Supported = supported, Unsupported = failed, Scenes = rows },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Autonomous PC texture replay: {supported} complete, {failed} unsupported.");
    }
}
