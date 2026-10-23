using System.Buffers.Binary;
using System.Text;

namespace JadeView;

internal readonly record struct AudioWaveLoopRegion(int StartFrame, int EndFrame);

internal static class AudioWaveLoopReader
{
    private const int CuePointSize = 24;
    private const int MinimumLoopSizeBytes = 128;

    internal static AudioWaveLoopRegion? Read(ReadOnlySpan<byte> wave, int decodedFrameCount,
        int channels, int format, int samplesPerBlock)
    {
        if (decodedFrameCount <= 0 || channels <= 0 || wave.Length < 12) return null;

        var cues = new List<CuePoint>();
        int position = 12;
        while (position + 8 <= wave.Length)
        {
            ReadOnlySpan<byte> chunkName = wave.Slice(position, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wave.Slice(position + 4, 4));
            int dataOffset = position + 8;
            if (chunkSize < 0 || chunkSize > wave.Length - dataOffset) break;
            ReadOnlySpan<byte> chunk = wave.Slice(dataOffset, chunkSize);

            if (chunkName.SequenceEqual("cue "u8))
                ReadCuePoints(chunk, cues);
            else if (chunkName.SequenceEqual("LIST"u8) && chunk.Length >= 4
                && chunk[..4].SequenceEqual("adtl"u8))
                ReadAssociatedData(chunk[4..], cues);

            position = dataOffset + chunkSize + (chunkSize & 1);
        }

        CuePoint? selectedLoop = cues.LastOrDefault(cue =>
            cue.Label?.Equals("loop", StringComparison.OrdinalIgnoreCase) == true);
        if (selectedLoop is not { } loop || loop.SampleLength == 0) return null;

        int loopStart = (int)Math.Min(loop.Position, (uint)decodedFrameCount);
        long requestedEnd = (long)loop.Position + loop.SampleLength;
        int loopEnd = (int)Math.Clamp(requestedEnd, (long)loopStart, decodedFrameCount);
        if (loopEnd <= loopStart || LoopSizeBytes(loopEnd - loopStart, channels, format,
                samplesPerBlock) <= MinimumLoopSizeBytes)
            return null;

        return new(loopStart, loopEnd);
    }

    private static void ReadCuePoints(ReadOnlySpan<byte> chunk, List<CuePoint> cues)
    {
        if (chunk.Length < sizeof(uint)) return;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
        if (count > (chunk.Length - sizeof(uint)) / CuePointSize) return;

        int offset = sizeof(uint);
        for (int cueIndex = 0; cueIndex < count; cueIndex++, offset += CuePointSize)
        {
            uint identifier = BinaryPrimitives.ReadUInt32LittleEndian(chunk[offset..]);
            uint samplePosition = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(offset + 4)..]);
            cues.Add(new(identifier, samplePosition));
        }
    }

    private static void ReadAssociatedData(ReadOnlySpan<byte> chunks, List<CuePoint> cues)
    {
        int offset = 0;
        while (offset + 8 <= chunks.Length)
        {
            ReadOnlySpan<byte> chunkName = chunks.Slice(offset, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(chunks.Slice(offset + 4, 4));
            int dataOffset = offset + 8;
            if (chunkSize < 0 || chunkSize > chunks.Length - dataOffset) return;
            ReadOnlySpan<byte> chunk = chunks.Slice(dataOffset, chunkSize);
            if (chunk.Length >= 4)
            {
                uint identifier = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
                CuePoint? cue = cues.FirstOrDefault(candidate => candidate.Identifier == identifier);
                if (cue != null)
                {
                    if (chunkName.SequenceEqual("labl"u8))
                    {
                        int terminator = chunk[4..].IndexOf((byte)0);
                        if (terminator >= 0)
                            cue.Label = Encoding.ASCII.GetString(chunk.Slice(4, terminator));
                    }
                    else if (chunkName.SequenceEqual("ltxt"u8) && chunk.Length >= 8)
                        cue.SampleLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                }
            }

            offset = dataOffset + chunkSize + (chunkSize & 1);
        }
    }

    private static double LoopSizeBytes(int frameCount, int channels, int format, int samplesPerBlock)
    {
        if (format == 2 && samplesPerBlock > 0)
            return frameCount * 36d * channels / samplesPerBlock;
        return frameCount * 2d * channels;
    }

    private sealed class CuePoint(uint identifier, uint position)
    {
        internal uint Identifier { get; } = identifier;
        internal uint Position { get; } = position;
        internal uint SampleLength { get; set; }
        internal string? Label { get; set; }
    }
}
