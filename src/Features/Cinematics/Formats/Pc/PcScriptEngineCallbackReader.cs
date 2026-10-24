using System.Buffers.Binary;

namespace JadeView;

/// <summary>
/// Reads the optional per-engine-event function list after the five AI track callbacks
/// in an OVA payload (Iris ai_common.c:1287-1320).
/// </summary>
internal static class PcScriptEngineCallbackReader
{
    private const int TrackCallbackCount = 5;
    private const int EngineCallbackKeySize = sizeof(uint);

    internal static CinematicScriptEngineCallbackSlot[]? Read(ReadOnlySpan<byte> bytes,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        PcVariableStorageLayout layout;
        try
        {
            layout = PcVariableStorageReader.ReadLayout(bytes);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        int trackCallbacksOffset = layout.StorageStart + layout.StorageLength;
        int remainingBytes = bytes.Length - trackCallbacksOffset;
        int trackCallbackBytes = TrackCallbackCount * EngineCallbackKeySize;
        if (remainingBytes < trackCallbackBytes) return null;
        int engineCallbackCountOffset = trackCallbacksOffset + trackCallbackBytes;
        if (engineCallbackCountOffset == bytes.Length) return [];
        if (bytes.Length - engineCallbackCountOffset < sizeof(int)) return null;

        int callbackCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[engineCallbackCountOffset..]);
        int callbackKeysOffset = engineCallbackCountOffset + sizeof(int);
        if (callbackCount < 0 || callbackCount > (bytes.Length - callbackKeysOffset) / EngineCallbackKeySize)
            return null;

        var callbacks = new CinematicScriptEngineCallbackSlot[callbackCount];
        for (int eventId = 0; eventId < callbackCount; eventId++)
        {
            int keyOffset = callbackKeysOffset + eventId * EngineCallbackKeySize;
            uint functionKey = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(keyOffset, EngineCallbackKeySize));
            string? functionName = functionKey == 0 ? null : functionNames.GetValueOrDefault(functionKey);
            callbacks[eventId] = new(eventId, functionKey, functionName);
        }

        return callbacks;
    }
}
