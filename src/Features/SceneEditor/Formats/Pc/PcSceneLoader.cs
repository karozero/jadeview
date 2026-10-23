using System.Text;

namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private byte[] dataBytes;
    private int dataLength;
    private uint loadingWorldKey;
    private readonly Func<Request, uint, byte[]>? frameSource;
    private readonly HashSet<uint> specialKeys;
    private readonly Queue<Request> queue = new();
    private readonly Dictionary<uint, Record> cache = [];
    private readonly HashSet<uint> shapeKeys = [];
    private readonly Result result = new();
    private int cursor, budget;
    private bool fixedResources;
    private Record? currentRecord;
    private PcSceneLoader(byte[] data, HashSet<uint> special, Func<Request, uint, byte[]>? source = null)
    {
        dataBytes = data;
        dataLength = data.Length;
        specialKeys = special;
        frameSource = source;
    }

    private uint ReadUInt32(int offset)
    {
        if (offset < 0 || offset > dataLength - 4)
            throw new InvalidDataException($"FF0 cursor {offset:X8} outside stream.");
        return Preview.U32(dataBytes, offset);
    }

    private int ReadInt32(int offset) => checked((int)ReadUInt32(offset));
    private ushort ReadUInt16(int offset)
    {
        if (offset < 0 || offset > dataLength - 2)
            throw new InvalidDataException("Truncated FF0 word.");
        return (ushort)Preview.U16(dataBytes, offset);
    }

    private string ReadText(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > dataLength - count)
            throw new InvalidDataException("Truncated FF0 string.");
        return Encoding.Latin1.GetString(dataBytes, offset, count).Split('\0')[0].Trim();
    }

    private void EnqueueRequest(uint key, char kind, bool keep = true, bool allowZero = false)
    {
        if (key == uint.MaxValue || (!allowZero && key == 0))
            return;
        var request = new Request(key, kind, keep);
        queue.Enqueue(request);
        currentRecord?.References.Add(request);
    }

    private void EnqueueReferenceAt(int offset, char kind, bool keep = true)
    {
        if (kind == 'l')
            currentRecord?.GaoReferenceOffsets.Add(offset);
        EnqueueRequest(ReadUInt32(offset), kind, keep);
    }

    private void DrainRequests()
    {
        while (queue.TryDequeue(out var request))
        {
            if (++budget > 1000000)
                throw new InvalidDataException("FF0 dependency budget exceeded.");
            if (!fixedResources && specialKeys.Contains(request.Key))
            {
                result.SpeSkipped++;
                continue;
            }

            if (cache.ContainsKey(request.Key))
            {
                result.CachedSkipped++;
                continue;
            }

            AppendSourceFrame(request);
            if (request.Kind == 'u')
                ReadShapeRecord(request);
            else
                ReadFramedRecord(request);
        }
    }

    internal static Result Read(byte[] bytes, string archivePath, uint? worldListKey = null)
    {
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, "jade.spe");
        if (!File.Exists(path))
            return new Result
            {
                Error = "jade.spe not found next to archive."
            };
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length % 4 != 0)
            return new Result
            {
                Error = "Invalid jade.spe size."
            };
        var special = new HashSet<uint>();
        for (int offset = 0; offset < raw.Length; offset += 4)
            special.Add(Preview.U32(raw, offset));
        var result = new PcSceneLoader(bytes, special).Run();
        result.OuterWorldListKey = worldListKey;
        return result;
    }

    // Re-emit records in the exact dependency/cache order used by the PC loader.
    internal static byte[] Rewrite(byte[] header, string archivePath, Func<Request, uint, byte[]> source)
    {
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, "jade.spe");
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length % 4 != 0)
            throw new InvalidDataException("Invalid jade.spe size.");
        var special = new HashSet<uint>();
        for (int offset = 0; offset < raw.Length; offset += 4)
            special.Add(Preview.U32(raw, offset));
        var writer = new PcSceneLoader(header.ToArray(), special, source);
        var replay = writer.Run();
        if (!replay.Complete)
            throw new InvalidDataException("FF0 rebuild failed: " + replay.Error);
        return writer.dataBytes.AsSpan(0, writer.dataLength).ToArray();
    }
}
