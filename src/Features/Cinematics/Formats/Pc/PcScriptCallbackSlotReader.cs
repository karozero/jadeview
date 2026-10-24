using System.Buffers.Binary;

namespace JadeView;

internal static class PcScriptCallbackSlotReader
{
    private const int CallbackSlotCount = 5;

    internal static CinematicScriptCallbackSlot[]? Read(ReadOnlySpan<byte> bytes,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        if (!TryGetCallbackDataOffset(bytes, out int callbackDataOffset)) return null;

        var slots = new CinematicScriptCallbackSlot[CallbackSlotCount];
        for (int slotIndex = 0; slotIndex < CallbackSlotCount; slotIndex++)
        {
            uint functionKey = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(callbackDataOffset + slotIndex * sizeof(uint), sizeof(uint)));
            string? functionName = functionKey == 0 ? null : functionNames.GetValueOrDefault(functionKey);
            slots[slotIndex] = new(slotIndex, functionKey, functionName);
        }
        return slots;
    }

    internal static bool HasCompleteCallbackTable(ReadOnlySpan<byte> bytes) =>
        TryGetCallbackDataOffset(bytes, out _);

    private static bool TryGetCallbackDataOffset(ReadOnlySpan<byte> bytes, out int callbackDataOffset)
    {
        callbackDataOffset = 0;
        PcVariableStorageLayout layout;
        try
        {
            layout = PcVariableStorageReader.ReadLayout(bytes);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        callbackDataOffset = checked(layout.StorageStart + layout.StorageLength);
        int callbackDataLength = CallbackSlotCount * sizeof(uint);
        return callbackDataOffset <= bytes.Length - callbackDataLength;
    }
}
