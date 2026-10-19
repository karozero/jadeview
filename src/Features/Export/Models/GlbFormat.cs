namespace JadeView;

internal static class GlbFormat
{
    internal const uint Magic = 0x46546C67;
    internal const uint Version = 2;
    internal const uint JsonChunk = 0x4E4F534A;
    internal const uint BinaryChunk = 0x004E4942;
    internal const int Float = 5126;
    internal const int UnsignedShort = 5123;
    internal const int VertexBuffer = 34962;
    internal const int HeaderSize = 12;
    internal const int ChunkHeaderSize = 8;
    internal const byte JsonPadding = 32;
    internal const int Alignment = 4;
}
