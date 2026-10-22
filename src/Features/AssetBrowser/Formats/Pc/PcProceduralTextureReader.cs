using System.Buffers.Binary;

namespace JadeView;

internal static class PcProceduralTextureReader
{
    private const int MpegSubtype = 3;
    private const int SubtypeOffset = 42;
    private const int MpegMediaKeyOffset = 76;
    private const int MinimumDescriptorSize = MpegMediaKeyOffset + sizeof(uint);

    internal static PcTextureLoader.ProceduralTexture? Read(byte[] textureContainer,
        PcTextureLoader.ReadEvent read)
    {
        if (read.Phase != "header" || read.Type != 5 || read.Size < 44) return null;
        int dataOffset = checked(read.Offset + sizeof(uint));
        int dataEnd = checked(dataOffset + read.Size);
        if (dataOffset < 0 || dataEnd > textureContainer.Length) return null;

        ushort ReadUInt16(int relativeOffset) => BinaryPrimitives.ReadUInt16LittleEndian(
            textureContainer.AsSpan(dataOffset + relativeOffset, sizeof(ushort)));
        uint ReadUInt32(int relativeOffset) => BinaryPrimitives.ReadUInt32LittleEndian(
            textureContainer.AsSpan(dataOffset + relativeOffset, sizeof(uint)));

        int subtype = ReadUInt16(SubtypeOffset);
        uint? mediaKey = null;
        if (subtype == MpegSubtype && read.Size >= MinimumDescriptorSize)
        {
            uint key = ReadUInt32(MpegMediaKeyOffset);
            if (key is not (0 or uint.MaxValue)) mediaKey = key;
        }

        return new(read.Key, read.Offset, read.Size, subtype,
            ReadUInt16(8), ReadUInt16(10), mediaKey);
    }
}
