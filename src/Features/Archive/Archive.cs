using System.Buffers.Binary;
using System.Text;

namespace JadeView;

// BIG 0x22 layout was studied using JadeStudio's format descriptions.
// All I/O below is independent, bounded, and preserves the original archive metadata.
public sealed class Archive
{
    public const int BlockSize = 512000;
    public string PathName { get; }
    public long Length { get; }
    public List<Entry> Entries { get; } = [];
    public int DirectoryCount { get; private set; }
    readonly long fatOffset, namesOffset;
    readonly int capacity;

    private string? _sha256;
    public string Sha256
    {
        get
        {
            if (_sha256 == null)
            {
                using var fs = File.OpenRead(PathName);
                _sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fs));
            }
            return _sha256;
        }
    }

    public Archive(string path)
    {
        PathName = Path.GetFullPath(path);
        using var s = File.OpenRead(PathName);
        Length = s.Length;
        using var r = new BinaryReader(s);
        if (Length < 68 || r.ReadUInt32() != 0x00474942 || r.ReadInt32() != 0x22)
            throw new InvalidDataException("Only original PC BIG 0x22 archives are supported (not BUG/HD/console).");
        int count = r.ReadInt32();
        s.Position = 32;
        capacity = r.ReadInt32();
        int fats = r.ReadInt32();
        if (fats != 1 || count < 0 || capacity < count || capacity > 1_000_000)
            throw new InvalidDataException("Invalid or unsupported BIG table.");
        s.Position = 44;
        if (r.ReadInt32() != count) throw new InvalidDataException("FAT entry count does not match the header.");
        s.Position = 52;
        fatOffset = r.ReadUInt32();
        namesOffset = checked(fatOffset + capacity * 8L);
        long dirsOffset = checked(namesOffset + capacity * 84L);
        CheckRange(dirsOffset, capacity * 84L, Length);
        var dirs = new (string Name, int Parent)[capacity];
        s.Position = dirsOffset;
        for (int i = 0; i < capacity; i++)
        {
            s.Position += 16;
            int parent = r.ReadInt32();
            dirs[i] = (CString(r.ReadBytes(64)), parent);
        }
        DirectoryCount = dirs.Count(d => d.Name.Length > 0);
        string Folder(int id)
        {
            var parts = new List<string>();
            var seen = new HashSet<int>();
            while (id >= 0 && id < capacity && seen.Add(id))
            {
                var d = dirs[id];
                if (d.Name.Length == 0 || id == 0) break;
                parts.Add(d.Name); id = d.Parent;
            }
            parts.Reverse();
            return parts.Count > 0 ? string.Join('/', parts) : "ROOT";
        }
        for (int i = 0; i < count; i++)
        {
            s.Position = fatOffset + i * 8L;
            uint offset = r.ReadUInt32(), key = r.ReadUInt32();
            CheckRange(offset, 4, Length);
            s.Position = offset;
            int stored = r.ReadInt32();
            if (stored < 0) throw new InvalidDataException($"Negative size {key:X8}.");
            CheckRange(offset + 4L, stored, Length);
            s.Position = namesOffset + i * 84L + 12;
            int directory = r.ReadInt32();
            s.Position += 4;
            string name = CString(r.ReadBytes(64));
            Entries.Add(new(i, key, offset, stored, string.IsNullOrEmpty(name) ? $"{key:X8}.bin" : name, Folder(directory)));
        }
    }

    public byte[] Read(Entry entry, int maxBytes = 256 * 1024 * 1024)
    {
        using var output = new MemoryStream();
        Decode(entry, output, maxBytes);
        return output.ToArray();
    }
    public void Export(Entry entry, string path)
    {
        if (SamePath(path, PathName)) throw new IOException("Cannot export over the open archive.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temp)) Decode(entry, output, long.MaxValue);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void Decode(Entry e, Stream output, long limit)
    {
        using var s = File.OpenRead(PathName);
        using var r = new BinaryReader(s);
        s.Position = e.Offset + 4L;
        if (!e.Compressed)
        {
            if (e.StoredSize > limit) throw new InvalidDataException("File too large to preview. Export it instead.");
            Copy(s, output, e.StoredSize); return;
        }
        long end = s.Position + e.StoredSize, total = 0;
        bool final = false;
        while (s.Position + 8 <= end)
        {
            int plain = r.ReadInt32(), packed = r.ReadInt32();
            if (plain == 0 && packed == 0) { final = true; break; }
            if (plain < 0 || plain > BlockSize || packed < 1 || packed > BlockSize * 2 || s.Position + packed > end)
                throw new InvalidDataException($"Invalid LZO block in {e.Id}.");
            total += plain;
            if (total > limit) throw new InvalidDataException("File too large to preview. Export it instead.");
            var block = r.ReadBytes(packed);
            output.Write(packed == plain ? block : Lzo.Decode(block, plain));
            if (plain < BlockSize) { final = true; break; }
            if (s.Position == end) { final = true; break; }
        }
        if (!final) throw new InvalidDataException($"Truncated LZO stream in {e.Id}.");
    }

    // Copy-on-write: retain every original byte, append new records, patch only FAT offsets/sizes.
    // No in-place mutation of the game archive and no reconstruction of unknown directory metadata.
    public void Save(string destination, IReadOnlyDictionary<int, string> replacements, Action<float>? progress = null)
    {
        destination = Path.GetFullPath(destination);
        if (SamePath(destination, PathName)) throw new IOException("Save the new .bf with a different filename.");
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(PathName))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                var buffer = new byte[1024 * 1024];
                int n;
                while ((n = input.Read(buffer)) != 0) { output.Write(buffer, 0, n); progress?.Invoke((float)input.Position / Length * .8f); }
                int done = 0;
                foreach (var (index, path) in replacements)
                {
                    var entry = Entries[index];
                    using var replacement = File.OpenRead(path);
                    if (SamePath(path, destination) || SamePath(path, PathName)) throw new IOException("An archive cannot be used as a replacement file.");
                    long offset = (output.Length + 2047) & ~2047L;
                    if (offset > uint.MaxValue) throw new IOException("Archive would exceed the BIG limit of 4 GB.");
                    output.Position = offset;
                    writer.Write(0);
                    if (!entry.Compressed) replacement.CopyTo(output);
                    else
                    {
                        var block = new byte[BlockSize];
                        int read;
                        do
                        {
                            read = 0;
                            while (read < block.Length) { int got = replacement.Read(block, read, block.Length - read); if (got == 0) break; read += got; }
                            // BIG explicitly supports raw blocks when input/output lengths match.
                            writer.Write(read); writer.Write(read); writer.Write(block, 0, read);
                        } while (read == BlockSize);
                        long paddedEnd = ((output.Position + 4 + 2047) & ~2047L) - 4;
                        if (paddedEnd > output.Position) { output.Position = paddedEnd - 1; writer.Write((byte)0); }
                    }
                    long size = output.Position - offset - 4;
                    if (size > int.MaxValue || output.Position > uint.MaxValue) throw new IOException("Replacement exceeds the BIG size limit.");
                    output.Position = offset; writer.Write((int)size);
                    output.Position = fatOffset + index * 8L; writer.Write((uint)offset);
                    output.Position = namesOffset + index * 84L; writer.Write((int)size);
                    output.Position = output.Length;
                    progress?.Invoke(.8f + .2f * ++done / Math.Max(1, replacements.Count));
                }
                output.Flush(true);
            }
            var verify = new Archive(temp);
            foreach (var (index, path) in replacements)
            {
                using var expected = File.OpenRead(path);
                using var comparison = new CompareStream(expected);
                verify.Decode(verify.Entries[index], comparison, long.MaxValue);
                comparison.Complete();
            }
            File.Move(temp, destination, true);
            progress?.Invoke(1);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void Copy(Stream input, Stream output, long count)
    {
        var b = new byte[128 * 1024];
        while (count > 0) { int n = input.Read(b, 0, (int)Math.Min(count, b.Length)); if (n == 0) throw new EndOfStreamException(); output.Write(b, 0, n); count -= n; }
    }
    static void CheckRange(long offset, long size, long length)
    { if (offset < 0 || size < 0 || offset > length || size > length - offset) throw new InvalidDataException("Entry outside archive bounds."); }
    public static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    static string CString(byte[] b) => Encoding.UTF8.GetString(b.AsSpan(0, Array.IndexOf(b, (byte)0) is int end && end >= 0 ? end : b.Length));
    sealed class CompareStream(Stream expected) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        { var b = new byte[buffer.Length]; expected.ReadExactly(b); if (!buffer.SequenceEqual(b)) throw new InvalidDataException("Saved replacement verification failed."); }
        public void Complete() { if (expected.Position != expected.Length) throw new InvalidDataException("Saved replacement size mismatch."); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long n) => throw new NotSupportedException();
    }
}

