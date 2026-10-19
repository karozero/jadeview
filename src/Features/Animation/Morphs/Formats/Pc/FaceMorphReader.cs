using System.Numerics;

namespace JadeView;

public static class FaceMorphReader
{
    private const int HeaderSize = 20;
    private const int WordSize = sizeof(uint);
    private const int SparseEntrySize = sizeof(int) + 3 * sizeof(float);
    private const int MinimumChannelSize = 12;
    private const int ChannelStateSize = 2 * WordSize;
    // PC type-4 payload only, excluding the modifier's type/instance prefix.
    public static FaceMorphData Read(byte[] data, int vertexCount)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        Need(reader, HeaderSize);
        var header = new[] { reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32() };
        if (header[2] != vertexCount)
            throw new InvalidDataException($"Face morph expects {header[2]} vertices, but the mesh has {vertexCount}.");
        int groupCount = Count(reader, WordSize);
        int channelCount = Count(reader, MinimumChannelSize);
        var groups = new FaceMorphGroup[groupCount];
        for (int index = 0; index < groups.Length; index++)
            groups[index] = ReadGroup(reader, index, vertexCount);
        var channels = new FaceMorphChannel[channelCount];
        for (int index = 0; index < channels.Length; index++)
            channels[index] = ReadChannel(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Unexpected trailing PC face morph data.");
        return new(header, groups, channels);
    }

    private static FaceMorphGroup ReadGroup(BinaryReader reader, int index, int vertexCount)
    {
        int count = Count(reader, SparseEntrySize);
        var indices = new int[count];
        for (int entry = 0; entry < count; entry++)
        {
            int vertex = reader.ReadInt32();
            if (vertex < 0 || vertex >= vertexCount)
                throw new InvalidDataException($"Invalid vertex in face morph group {index}.");
            indices[entry] = vertex;
        }
        var deltas = new Vector3[count];
        for (int entry = 0; entry < count; entry++)
            deltas[entry] = new(Finite(reader), Finite(reader), Finite(reader));
        return new(index, indices, deltas);
    }

    private static FaceMorphChannel ReadChannel(BinaryReader reader)
    {
        int count = Count(reader, WordSize);
        Need(reader, checked(ChannelStateSize + count * WordSize));
        uint weight = reader.ReadUInt32(), progression = reader.ReadUInt32();
        var references = new uint[count];
        for (int entry = 0; entry < count; entry++) references[entry] = reader.ReadUInt32();
        // Preserve source state and holes; runtime progression is evaluated separately.
        return new(weight, progression, references);
    }

    private static int Count(BinaryReader reader, int elementBytes)
    {
        Need(reader, WordSize);
        int count = reader.ReadInt32();
        if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / elementBytes)
            throw new InvalidDataException("PC face morph count exceeds payload.");
        return count;
    }

    private static float Finite(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite face morph delta.");
        return value;
    }

    private static void Need(BinaryReader reader, int bytes)
    {
        if (bytes < 0 || reader.BaseStream.Position > reader.BaseStream.Length - bytes)
            throw new InvalidDataException("Truncated PC face morph payload.");
    }
}
