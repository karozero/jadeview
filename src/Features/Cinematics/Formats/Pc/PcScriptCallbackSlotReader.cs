using System.Buffers.Binary;

namespace JadeView;

internal static class PcScriptCallbackSlotReader
{
    private const int CallbackSlotCount = 5;

    internal static CinematicScriptCallbackSlot[]? Read(ReadOnlySpan<byte> bytes,
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

        int callbackDataOffset = checked(layout.StorageStart + layout.StorageLength);
        int callbackDataLength = CallbackSlotCount * sizeof(uint);
        if (callbackDataOffset > bytes.Length - callbackDataLength) return null;

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
}
