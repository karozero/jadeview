using System.Buffers.Binary;
using System.Text;

namespace JadeView;

internal static class PcPickupVariables
{
    internal const uint BehaviorModel = 0x9E001C44;

    internal static PcPickupFlagFields? FindFlagFields(byte[] frame)
    {
        int descriptorBytes = ReadInt(frame, 4);
        int count = descriptorBytes / 12;
        int namesStart = checked(12 + descriptorBytes);
        int valuesStart = checked(namesStart + count * 30 + 4);
        bool validLayout = descriptorBytes >= 0 && descriptorBytes % 12 == 0 &&
            valuesStart <= frame.Length && ReadInt(frame, valuesStart - 4) != 0;
        if (!validLayout)
            return null;

        int activeOffset = -1, loadOffset = -1;
        for (int index = 0; index < count; index++)
        {
            int descriptor = 8 + index * 12;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(descriptor + 4));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(descriptor + 8));
            // The supported pickup's instance overrides are scalar words only.
            if (length != 1 || type is not (33 or 34))
                throw new InvalidDataException("Unsupported pickup variable layout; persistence was not changed.");
            string name = Encoding.Latin1.GetString(frame, namesStart + index * 30, 30).Split('\0')[0];
            int valueOffset = checked(valuesStart + index * 4);
            if (type == 33 && name == "des_SF_active_on_destroy")
                activeOffset = valueOffset;
            if (type == 33 && name == "des_SF_destroy_on_load")
                loadOffset = valueOffset;
        }

        if (activeOffset < 0 || loadOffset < 0)
            return null;
        int active = ReadInt(frame, activeOffset), load = ReadInt(frame, loadOffset);
        // Zero/negative flags and different flags can express intentional respawn/story logic.
        return active > 0 && active == load ? new(activeOffset, loadOffset) : null;
    }

    internal static void RemapFlags(byte[] frame, int flag)
    {
        var fields = FindFlagFields(frame) ??
            throw new InvalidDataException("The pickup no longer has the paired persistence flags recorded by this project.");
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(fields.ActiveOffset), flag);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(fields.LoadOffset), flag);
    }

    private static int ReadInt(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
}
