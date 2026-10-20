using System.Buffers.Binary;

namespace JadeView;

internal static class CinematicActionTransitions
{
    internal static CinematicActionTransition[][] Read(ReadOnlySpan<byte> bytes, bool[] present)
    {
        var result = new CinematicActionTransition[present.Length][];
        int cursor = 0;
        for (int itemIndex = 0; itemIndex < present.Length; itemIndex++)
        {
            result[itemIndex] = [];
            if (!present[itemIndex]) continue;
            int count = unchecked((int)ReadWord(bytes, ref cursor));
            ReadWord(bytes, ref cursor); // Serialized array granularity; not its element count.
            if (count < 0 || count > (bytes.Length - cursor) / 8)
                throw new InvalidDataException("Invalid action transition count.");
            var keys = new uint[count];
            for (int index = 0; index < count; index++) keys[index] = ReadWord(bytes, ref cursor);
            var transitions = new CinematicActionTransition[count];
            for (int index = 0; index < count; index++)
            {
                uint packed = ReadWord(bytes, ref cursor);
                transitions[index] = new(keys[index], (ushort)packed, (byte)(packed >> 16), (byte)(packed >> 24));
            }
            result[itemIndex] = transitions;
        }
        if (cursor != bytes.Length) throw new InvalidDataException("Unexpected action transition bytes.");
        return result;
    }

    private static uint ReadWord(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        if (cursor > bytes.Length - 4) throw new InvalidDataException("Truncated action transition table.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cursor..]);
        cursor += 4;
        return value;
    }
}
