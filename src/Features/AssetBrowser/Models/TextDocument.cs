using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

public sealed class TextDocument
{
    public List<List<TextGroup>> Batches { get; set; } = [];
    public sealed class TextGroup
    {
        public uint Id { get; set; }
        public byte[] References { get; set; } = [];
        public uint Flags { get; set; }
        public List<int> StringOffsets { get; set; } = [];
        public List<string> Strings { get; set; } = [];
    }
    public static TextDocument Read(byte[] b)
    {
        using var s = new MemoryStream(b); using var r = new BinaryReader(s);
        int Count(int unit)
        { int length = r.ReadInt32(); if (length < 0 || length % unit != 0 || length > s.Length - s.Position) throw new InvalidDataException("Invalid text table."); return length / unit; }
        var doc = new TextDocument();
        while (s.Position < s.Length)
        {
            int count = Count(8); var groups = new List<TextGroup>();
            for (int i = 0; i < count; i++)
            { uint id = r.ReadUInt32(); if (r.ReadUInt32() != 0x6778742E) throw new InvalidDataException("Expected .txg."); groups.Add(new() { Id = id }); }
            foreach (var g in groups) { int n = Count(16); g.References = r.ReadBytes(n * 16); g.Flags = r.ReadUInt32(); }
            foreach (var g in groups)
            {
                int n = Count(4); var offsets = new int[n]; for (int i = 0; i < n; i++) offsets[i] = r.ReadInt32();
                int length = Count(1); var pool = r.ReadBytes(length);
                g.StringOffsets = offsets.ToList();
                foreach (int offset in offsets)
                {
                    if (offset < 0 || offset >= pool.Length) throw new InvalidDataException("String outside pool bounds.");
                    int end = Array.IndexOf(pool, (byte)0, offset);
                    if (end < 0) throw new InvalidDataException("Truncated string.");
                    g.Strings.Add(Encoding.GetEncoding(1252).GetString(pool, offset, end - offset));
                }
            }
            doc.Batches.Add(groups);
        }
        return doc;
    }
    public byte[] Write()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        var encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        foreach (var batch in Batches)
        {
            w.Write(checked(batch.Count * 8)); foreach (var g in batch) { w.Write(g.Id); w.Write(0x6778742E); }
            foreach (var g in batch) { w.Write(g.References.Length); w.Write(g.References); w.Write(g.Flags); }
            foreach (var g in batch)
            {
                var parts = g.Strings.Select(str => encoding.GetBytes(str)).ToList();
                w.Write(checked(parts.Count * 4)); int offset = 0; var offsets = new List<int>(parts.Count);
                foreach (var part in parts) { offsets.Add(offset); w.Write(offset); offset = checked(offset + part.Length + 1); }
                g.StringOffsets = offsets;
                w.Write(offset); foreach (var part in parts) { w.Write(part); w.Write((byte)0); }
            }
        }
        return s.ToArray();
    }
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static byte[] Import(string path, TextDocument original)
    {
        var edit = JsonSerializer.Deserialize<TextDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty JSON.");
        if (edit.Batches.Count != original.Batches.Count) throw new InvalidDataException("Cannot change text block count.");
        for (int i = 0; i < edit.Batches.Count; i++)
        {
            if (edit.Batches[i].Count != original.Batches[i].Count) throw new InvalidDataException("Cannot change group count.");
            for (int j = 0; j < edit.Batches[i].Count; j++)
            {
                var a = original.Batches[i][j]; var e = edit.Batches[i][j];
                if (a.Id != e.Id || a.Flags != e.Flags || !a.References.SequenceEqual(e.References) || a.Strings.Count != e.Strings.Count)
                    throw new InvalidDataException("Edit only Strings contents; preserve keys and structure.");
                if (e.Strings.Any(str => str.Contains('\0'))) throw new InvalidDataException("Strings must not contain NUL.");
            }
        }
        return edit.Write();
    }
}
