using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

sealed class Preview
{
    public byte[] Bytes = [];
    public string Kind = "Binary asset", Details = "";
    public List<TextureAsset> Textures = [];
    public AudioAsset? Audio;
    public TextDocument? Text;
    public SceneAssets? Scene;
    public Entry? TextureContainer { get; set; }
    public byte[]? AtlasBytes { get; set; }
    public string Readable = "";
    public static Preview Parse(Entry entry, byte[] bytes)
    {
        var p = new Preview { Bytes = bytes };
        if (bytes.AsSpan().StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            p.Kind = "WAVE audio";
            try { p.Audio = DecodeWave(bytes); p.Details = $"{p.Audio.Rate:N0} Hz · {p.Audio.Channels} channels · {p.Audio.Seconds:F1} s"; }
            catch (Exception ex) { p.Details = "Audio decoding: " + ex.Message; }
        }
        else if (entry.Name.StartsWith("ff8", StringComparison.OrdinalIgnoreCase))
        {
            p.Kind = "Jade texture container";
            p.TextureContainer = entry;
            p.AtlasBytes = bytes;
            try { p.Textures = ReadTextures(bytes); p.Details = $"{p.Textures.Count} textures available"; }
            catch (Exception ex) { p.Details = "Texture decoding: " + ex.Message; }
        }
        else if (entry.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            p.Kind = "Jade text";
            try
            {
                p.Text = TextDocument.Read(bytes);
                p.Readable = string.Join("\n\n", p.Text.Batches.SelectMany(b => b).SelectMany(g => g.Strings.Select((s, i) => $"[{g.Id:X8}:{i}]\n{s}")));
                p.Details = $"{p.Text.Batches.Sum(b => b.Sum(g => g.Strings.Count))} strings · Windows-1252";
            }
            catch (InvalidDataException)
            {
                p.Details = "This layout does not support JSON import. Extracted strings are shown; binary replacement is available.";
                var lines = new List<string>();
                int start = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] != 0) continue;
                    int length = i - start;
                    if (length >= 8 && length < 4096 && bytes.AsSpan(start, length).ToArray().All(c => c >= 32 || c is 9 or 10 or 13))
                        lines.Add($"[{start:X8}] " + Encoding.GetEncoding(1252).GetString(bytes, start, length));
                    start = i + 1;
                }
                p.Readable = string.Join("\n\n", lines);
            }
        }
        else if (bytes.AsSpan().StartsWith("BIK"u8))
        { p.Kind = "Bink video"; p.Details = "No built-in Bink decoder. Export the file to an external player."; }
        else if (entry.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase))
        { p.Kind = "Jade models"; p.Scene = Models.Read(bytes); p.Details = $"{p.Scene.Models.Count} models · vertices, UV, materials"; }
        else if (bytes.Length < 2 * 1024 * 1024 && bytes.Take(Math.Min(4096, bytes.Length)).Count(c => c == 0) == 0)
        { p.Readable = Encoding.GetEncoding(1252).GetString(bytes); }
        return p;
    }

    public static List<TextureAsset> ReadTextures(byte[] bytes, IReadOnlyDictionary<int, uint>? descriptorKeys = null)
    {
        var chunks = new List<(int Offset, int Size, int Type, int Width, int Height, bool Header)>();
        int pos = 0;
        while (pos + 4 <= bytes.Length)
        {
            int size = I32(bytes, pos), off = pos + 4;
            if (size < 0 || size > bytes.Length - off) throw new InvalidDataException($"Invalid chunk size 0x{pos:X}.");
            bool header = size >= 32 && bytes.AsSpan(off + 20, 12).SequenceEqual(new byte[] { 0x34, 0x12, 0xD0, 0xCA, 0xFF, 0, 0xFF, 0, 0xDE, 0xC0, 0xDE, 0xC0 });
            int type = header ? U16(bytes, off + 6) : 0;
            chunks.Add((pos, size, type, header ? U16(bytes, off + 8) : 0, header ? U16(bytes, off + 10) : 0, header));
            pos = off + size;
            if (header && type == 5 && size == 96) pos += 4;
        }
        var links = chunks.Where(c => c.Header && c.Type == 7 && c.Size >= 40)
            .Select(c => (Offset: c.Offset, Texture: U32(bytes, c.Offset + 36), Palette: U32(bytes, c.Offset + 40))).ToList();
        var paletteChunks = chunks.Where(c => !c.Header && c.Size is 48 or 64 or 768 or 1024).ToList();
        var keys = links.Select(l => l.Palette).Distinct().ToList();
        var palettes = new Dictionary<uint, byte[]>();
        for (int i = 0; i < Math.Min(keys.Count, paletteChunks.Count); i++)
        { var c = paletteChunks[i]; palettes[keys[i]] = bytes.AsSpan(c.Offset + 4, c.Size).ToArray(); }
        var result = new List<TextureAsset>();
        var tgaHeaders = chunks.Where(c => c.Header && c.Type == 0x1001 && c.Size == 32).ToList();
        int indexed = 0, tgaIndex = 0;
        foreach (var c in chunks.Where(c => c.Header && c.Size > 32 && c.Type is 0x4006 or 0x5006 or 0x1001))
        {
            if (c.Type == 0x1001)
            {
                var data = bytes.AsSpan(c.Offset + 36, c.Size - 32).ToArray();
                uint key = tgaIndex < tgaHeaders.Count ? descriptorKeys?.GetValueOrDefault(tgaHeaders[tgaIndex].Offset) ?? 0 : 0;
                if (data.Length >= 18) result.Add(new(key != 0 ? key.ToString("X8") : $"TGA_{tgaIndex:D3}", c.Width, c.Height, data, c.Offset, c.Type, null, key, 0, key));
                tgaIndex++;
                continue;
            }
            int ordinal = indexed++;
            if (ordinal >= links.Count || !palettes.TryGetValue(links[ordinal].Palette, out var palette)) continue;
            int pixels = checked(c.Width * c.Height), colors = c.Type == 0x5006 ? 16 : 256;
            int stride = palette.Length / colors;
            if (pixels <= 0 || pixels > 16 * 1024 * 1024 || stride is not (3 or 4)) continue;
            int required = c.Type == 0x5006 ? (pixels + 1) / 2 : pixels;
            if (required > c.Size - 32) continue;
            var tga = new byte[18 + pixels * 4];
            tga[2] = 2; W16(tga, 12, c.Width); W16(tga, 14, c.Height); tga[16] = 32; tga[17] = 8;
            for (int i = 0; i < pixels; i++)
            {
                int v = bytes[c.Offset + 36 + (c.Type == 0x5006 ? i / 2 : i)];
                if (c.Type == 0x5006) v = i % 2 == 0 ? v >> 4 : v & 15;
                int source = v * stride, target = 18 + i * 4;
                palette.AsSpan(source, 3).CopyTo(tga.AsSpan(target)); tga[target + 3] = stride == 4 ? palette[source + 3] : (byte)255;
            }
            var link = links[ordinal];
            uint wrapper = descriptorKeys?.GetValueOrDefault(link.Offset) ?? 0;
            result.Add(new(wrapper != 0 ? wrapper.ToString("X8") : link.Texture.ToString("X8"),
                c.Width, c.Height, tga, c.Offset, c.Type, palette, link.Texture, link.Palette, wrapper));
        }
        return result;
    }

    // Replace a paletted image using the original palette; no metadata or linkage changes.
    public byte[] ReplaceTexture(TextureAsset texture, byte[] rgba, int width, int height)
    {
        if (width != texture.Width || height != texture.Height) throw new InvalidDataException($"Required size: {texture.Width} × {texture.Height}.");
        if (texture.Palette == null) throw new InvalidDataException("PNG import supports indexed 4/8-bit textures.");
        var result = (byte[])Bytes.Clone();
        var pal = texture.Palette;
        int colors = texture.Type == 0x5006 ? 16 : 256, stride = pal.Length / colors;
        for (int i = 0; i < width * height; i++)
        {
            // Original Jade texture indices are stored bottom-up, RGBA import is top-down.
            int source = ((height - 1 - i / width) * width + i % width) * 4;
            int best = 0, bestDistance = int.MaxValue;
            for (int j = 0; j < colors; j++)
            {
                int p = j * stride, dr = rgba[source] - pal[p + 2], dg = rgba[source + 1] - pal[p + 1], db = rgba[source + 2] - pal[p];
                int da = rgba[source + 3] - (stride == 4 ? pal[p + 3] : 255);
                int distance = dr * dr + dg * dg + db * db + da * da;
                if (distance < bestDistance) { bestDistance = distance; best = j; if (distance == 0) break; }
            }
            int target = texture.ChunkOffset + 36 + (colors == 16 ? i / 2 : i);
            if (colors == 256) result[target] = (byte)best;
            else result[target] = (byte)(i % 2 == 0 ? (result[target] & 15) | best << 4 : (result[target] & 240) | best);
        }
        return result;
    }

    internal static AudioAsset DecodeWave(byte[] b)
    {
        if (b.Length < 12 || !b.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !b.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Audio resource is not a RIFF WAVE stream.");
        int format = 0, channels = 0, rate = 0, align = 0, bits = 0, dataOffset = 0, dataSize = 0;
        int[] c1 = [256, 512, 0, 192, 240, 460, 392], c2 = [0, -256, 0, 64, 0, -208, -232];
        for (int pos = 12; pos + 8 <= b.Length;)
        {
            int size = I32(b, pos + 4), off = pos + 8;
            if (size < 0 || size > b.Length - off) throw new InvalidDataException("Truncated WAVE chunk.");
            if (b.AsSpan(pos, 4).SequenceEqual("fmt "u8) && size >= 16)
            {
                format = U16(b, off); channels = U16(b, off + 2); rate = I32(b, off + 4); align = U16(b, off + 12); bits = U16(b, off + 14);
                if (format == 2 && size >= 22)
                {
                    int count = U16(b, off + 20);
                    if (count > 0 && count <= 256 && size >= 22 + count * 4)
                    { c1 = new int[count]; c2 = new int[count]; for (int i = 0; i < count; i++) { c1[i] = I16(b, off + 22 + 4 * i); c2[i] = I16(b, off + 24 + 4 * i); } }
                }
            }
            if (b.AsSpan(pos, 4).SequenceEqual("data"u8)) { dataOffset = off; dataSize = size; }
            pos = off + size + (size & 1);
        }
        if (channels is not (1 or 2) || rate < 1000 || rate > 192000 || dataOffset == 0) throw new InvalidDataException("Unsupported WAVE header.");
        var samples = new List<short>();
        if (format == 1 && bits is 8 or 16)
        {
            for (int p = dataOffset; p < dataOffset + dataSize; p += bits / 8)
                samples.Add(bits == 16 ? I16(b, p) : (short)((b[p] - 128) << 8));
        }
        else if (format == 2 && bits == 4 && align >= 7 * channels)
        {
            int[] adapt = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
            for (int start = dataOffset; start + 7 * channels <= dataOffset + dataSize; start += align)
            {
                int end = Math.Min(start + align, dataOffset + dataSize);
                int[] predictor = new int[channels], delta = new int[channels], first = new int[channels], second = new int[channels];
                int p = start;
                for (int c = 0; c < channels; c++) { predictor[c] = b[p++]; if (predictor[c] >= c1.Length) throw new InvalidDataException("Invalid ADPCM predictor."); }
                for (int c = 0; c < channels; c++) { delta[c] = U16(b, p); p += 2; }
                for (int c = 0; c < channels; c++) { first[c] = I16(b, p); p += 2; }
                for (int c = 0; c < channels; c++) { second[c] = I16(b, p); p += 2; }
                samples.AddRange(second.Select(v => (short)v)); samples.AddRange(first.Select(v => (short)v));
                void Nibble(int c, int n)
                {
                    long estimate = ((long)first[c] * c1[predictor[c]] + (long)second[c] * c2[predictor[c]]) / 256;
                    int value = (int)Math.Clamp(estimate + (n < 8 ? n : n - 16) * (long)delta[c], short.MinValue, short.MaxValue);
                    second[c] = first[c]; first[c] = value; delta[c] = Math.Max(16, adapt[n] * delta[c] / 256); samples.Add((short)value);
                }
                while (p < end) { int v = b[p++]; Nibble(0, v >> 4); Nibble(channels == 2 ? 1 : 0, v & 15); }
            }
        }
        else throw new InvalidDataException($"Codec {format}, {bits} bits is not supported.");
        var pcm = samples.ToArray();
        return AudioAsset.FromSamples(pcm, channels, rate);
    }
    internal static int I32(byte[] b, int p) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p, 4));
    internal static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
    internal static int U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2));
    internal static short I16(byte[] b, int p) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(p, 2));
    static void W16(byte[] b, int p, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p, 2), checked((ushort)v));

    public static byte[] DecodeTga(TextureAsset t)
    {
        var b = t.Tga;
        if (b.Length < 18) throw new InvalidDataException("Truncated TGA.");
        int type = b[2], bits = b[16], width = U16(b, 12), height = U16(b, 14), stride = bits / 8;
        if (b[1] != 0 || type is not (2 or 3 or 10 or 11) || bits is not (8 or 24 or 32) || width <= 0 || height <= 0 || width * (long)height > 16 * 1024 * 1024)
            throw new InvalidDataException("Unsupported TGA variant.");
        var rgba = new byte[width * height * 4];
        int pos = 18 + b[0], pixel = 0;
        void Pixel(int offset)
        {
            if (offset + stride > b.Length || pixel >= width * height) throw new InvalidDataException("TGA outside bounds.");
            int x = pixel % width, y = pixel / width;
            if ((b[17] & 32) == 0) y = height - 1 - y;
            if ((b[17] & 16) != 0) x = width - 1 - x;
            int target = (y * width + x) * 4;
            rgba[target] = stride == 1 ? b[offset] : b[offset + 2];
            rgba[target + 1] = stride == 1 ? b[offset] : b[offset + 1];
            rgba[target + 2] = b[offset]; rgba[target + 3] = stride == 4 ? b[offset + 3] : (byte)255; pixel++;
        }
        while (pixel < width * height)
        {
            if (type < 10) { Pixel(pos); pos += stride; continue; }
            if (pos >= b.Length) throw new InvalidDataException("Truncated RLE TGA.");
            int packet = b[pos++], n = (packet & 127) + 1;
            for (int i = 0; i < n; i++) { Pixel(pos); if ((packet & 128) == 0) pos += stride; }
            if ((packet & 128) != 0) pos += stride;
        }
        return rgba;
    }
}

