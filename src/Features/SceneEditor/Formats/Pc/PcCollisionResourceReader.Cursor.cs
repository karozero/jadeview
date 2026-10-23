using System.Buffers.Binary;
using System.Numerics;

namespace JadeView;

internal static partial class PcCollisionResourceReader
{
    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;

        internal bool Truncated { get; private set; }
        internal int Remaining => data.Length - position;
        internal bool AtEnd => position == data.Length;

        internal bool TryReadByte(out byte value)
        {
            value = 0;
            if (Remaining < sizeof(byte)) return FailTruncated();
            value = data[position++];
            return true;
        }

        internal bool TryReadUInt16(out ushort value)
        {
            value = 0;
            if (Remaining < sizeof(ushort)) return FailTruncated();
            value = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            position += sizeof(ushort);
            return true;
        }

        internal bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (Remaining < sizeof(uint)) return FailTruncated();
            value = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            position += sizeof(uint);
            return true;
        }

        internal bool TryReadBytes(int count, out byte[] value)
        {
            value = [];
            if (count < 0) return false;
            if (Remaining < count) return FailTruncated();
            value = data.Slice(position, count).ToArray();
            position += count;
            return true;
        }

        internal bool TryReadSingle(out float value)
        {
            value = 0;
            if (!TryReadUInt32(out uint bits)) return false;
            value = BitConverter.UInt32BitsToSingle(bits);
            return float.IsFinite(value);
        }

        internal bool TryReadVector(out Vector3 value)
        {
            value = default;
            if (!TryReadSingle(out float x)
                || !TryReadSingle(out float y)
                || !TryReadSingle(out float z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }

        private bool FailTruncated()
        {
            Truncated = true;
            return false;
        }

    }
}
