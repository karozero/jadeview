namespace JadeView;

internal sealed record CinematicSoundModifierFile(uint SoundKey, uint FadeOutKey, bool HasPlaylist)
{
    private const uint FileVersion = 3;
    private const uint ExtendedPlayerChunk = 0;
    private const int FixedPlayerDataSize = 224;
    private const int MaximumListCount = 4096;

    internal static CinematicSoundModifierFile? Read(byte[] bytes)
    {
        if (bytes.Length < 16 + FixedPlayerDataSize + 8
            || Preview.U32(bytes, 0) != FileVersion
            || Preview.U32(bytes, 8) != ExtendedPlayerChunk)
            return null;

        uint chunkSize = Preview.U32(bytes, 12);
        if (chunkSize < FixedPlayerDataSize || chunkSize > bytes.Length - 16)
            return null;

        uint playlistCount = checked((uint)Preview.U16(bytes, 232));
        uint insertCount = checked((uint)Preview.U16(bytes, 236));
        if (playlistCount > MaximumListCount || insertCount > MaximumListCount)
            return null;

        ulong variableSize = (ulong)playlistCount * 8 + (ulong)insertCount * 4;
        ulong expectedChunkSize = FixedPlayerDataSize + variableSize;
        ulong terminatorOffset = 16 + expectedChunkSize;
        if (expectedChunkSize != chunkSize || terminatorOffset > (ulong)bytes.Length - 8
            || Preview.U32(bytes, (int)terminatorOffset) != uint.MaxValue
            || Preview.U32(bytes, (int)terminatorOffset + 4) != 0)
            return null;

        uint soundKey = Preview.U32(bytes, 16);
        uint fadeOutKey = Preview.U32(bytes, 72);
        if (fadeOutKey is 0 or uint.MaxValue) fadeOutKey = uint.MaxValue;
        bool hasPlaylist = playlistCount > 0;
        if (hasPlaylist) soundKey = uint.MaxValue;
        if (!hasPlaylist && soundKey is 0 or uint.MaxValue) return null;

        return new(soundKey, fadeOutKey, hasPlaylist);
    }
}
