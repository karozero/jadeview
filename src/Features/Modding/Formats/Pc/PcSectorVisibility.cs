using System.Buffers.Binary;
using System.Text;

namespace JadeView;
internal static class PcSectorVisibility
{
    // Signature of the two hangar records emitted by our earlier workaround.
    // Do not normalize arbitrary null-brain objects from other games/scenes.
    internal static bool IsLegacyDisabledController(byte[] bytes, PcSceneLoader.Record record) => record.Flags == 0x008020A0 && record.BrainFieldOffset >= 0 && record.Name is "SCT_1.gao" or "SCT_2.gao" && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(record.BrainFieldOffset, 4)) == uint.MaxValue;
    internal static byte[] WithoutBrain(byte[] frame, PcSceneLoader.Record record)
    {
        int field = record.BrainFieldOffset - record.FrameRange.Offset;
        if ((record.Flags & 0x20) == 0 || field < 4 || field > frame.Length - 4)
            throw new InvalidDataException("Invalid sector-controller behavior field.");
        byte[] result = new byte[frame.Length - 4];
        frame.AsSpan(0, field).CopyTo(result);
        frame.AsSpan(field + 4).CopyTo(result.AsSpan(field));
        // The identity flag selects this field's presence and runtime extension.
        // A null resource key alone leaves that identity inconsistent.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, 4), record.Flags & ~0x20u);
        BinaryPrimitives.WriteInt32LittleEndian(result, result.Length - 4);
        return result;
    }

    // Match typed OVA descriptor names, not arbitrary strings or GAO name prefixes.
    // Other controllers (doors, gameplay, cinematics) retain their behavior.
    internal static bool IsPortalController(byte[] frame)
    {
        if (frame.Length < 12 || BinaryPrimitives.ReadInt32LittleEndian(frame) != frame.Length - 4)
            return false;
        int descriptorBytes = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
        if (descriptorBytes < 24 || descriptorBytes % 12 != 0)
            return false;
        int count = descriptorBytes / 12;
        long namesStart = 12L + descriptorBytes;
        if (namesStart + count * 30L > frame.Length)
            return false;
        bool doors = false, hiddenGroup = false;
        for (int descriptorIndex = 0; descriptorIndex < count; descriptorIndex++)
        {
            string name = Encoding.Latin1.GetString(frame, checked((int)(namesStart + descriptorIndex * 30L)), 26).Split('\0')[0];
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(16 + descriptorIndex * 12, 2));
            if (type != 40)
                continue; // GAO reference / array of GAO references
            doors |= name == "des_ao_portes";
            hiddenGroup |= name == "des_o_cacher_groupe_de";
        }

        return doors && hiddenGroup;
    }
}
