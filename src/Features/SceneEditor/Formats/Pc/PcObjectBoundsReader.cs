using System.Buffers.Binary;
using System.Numerics;

namespace JadeView;

/// <summary>
/// Reads the serialized PC GAO bounds vectors. The vector payload starts at record offset 90,
/// after the preceding four-byte padding word: two vectors for AABB/sphere or four for OBBox.
/// </summary>
internal static class PcObjectBoundsReader
{
    internal const int SerializedOffset = 90;
    internal const int AxisAlignedPayloadSize = 24;
    internal const int OrientedPayloadSize = 48;
    private const uint SphereMaximumZSentinel = 0xffff_ffff;

    internal static bool TryRead(ReadOnlySpan<byte> gaoRecord, uint identityFlags,
        out PcObjectBounds? bounds, out string error)
    {
        bounds = null;
        error = "";
        if (gaoRecord.Length < 4 || !gaoRecord[..4].SequenceEqual(".gao"u8))
        {
            error = "PC bounds input must begin with a .gao record tag.";
            return false;
        }

        bool hasOrientedBounds = (identityFlags & PcObjectBounds.OrientedBoundsIdentityFlag) != 0;
        int payloadSize = hasOrientedBounds ? OrientedPayloadSize : AxisAlignedPayloadSize;
        if (gaoRecord.Length < SerializedOffset + payloadSize)
        {
            error = $"Truncated PC GAO bounds block: expected {payloadSize} bytes at record offset {SerializedOffset}.";
            return false;
        }

        ReadOnlySpan<byte> payload = gaoRecord.Slice(SerializedOffset, payloadSize);
        if (!TryReadVector(payload, 0, out Vector3 minimum)
            || !TryReadVector(payload, 12, out Vector3 maximum, allowSphereSentinelZ: true))
        {
            error = "PC GAO bounds contain a non-finite coordinate.";
            return false;
        }

        bool isSphere = BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) == SphereMaximumZSentinel;
        Vector3? localMinimum = null;
        Vector3? localMaximum = null;
        if (hasOrientedBounds)
        {
            if (!TryReadVector(payload, 24, out Vector3 parsedLocalMinimum)
                || !TryReadVector(payload, 36, out Vector3 parsedLocalMaximum))
            {
                error = "PC GAO oriented bounds contain a non-finite local coordinate.";
                return false;
            }
            localMinimum = parsedLocalMinimum;
            localMaximum = parsedLocalMaximum;
            if (!IsOrdered(parsedLocalMinimum, parsedLocalMaximum))
            {
                error = "PC GAO oriented local bounds have minimum coordinates greater than maximum coordinates.";
                return false;
            }
        }

        if (isSphere)
        {
            float radius = maximum.X;
            if (!float.IsFinite(radius) || radius < 0)
            {
                error = "PC GAO sphere bounds have a non-finite or negative radius.";
                return false;
            }
            bounds = new(PcObjectBoundsKind.Sphere, minimum, Vector3.Zero,
                localMinimum, localMaximum, radius);
            return true;
        }

        if (!IsOrdered(minimum, maximum))
        {
            error = "PC GAO bounds have minimum coordinates greater than maximum coordinates.";
            return false;
        }

        PcObjectBoundsKind kind = hasOrientedBounds
            ? PcObjectBoundsKind.OrientedBox
            : PcObjectBoundsKind.AxisAlignedBox;
        bounds = new(kind, minimum, maximum, localMinimum, localMaximum, null);
        return true;
    }

    private static bool TryReadVector(ReadOnlySpan<byte> payload, int offset, out Vector3 vector,
        bool allowSphereSentinelZ = false)
    {
        vector = default;
        if (offset < 0 || payload.Length - offset < 12)
            return false;

        float x = ReadSingle(payload, offset);
        float y = ReadSingle(payload, offset + 4);
        uint zBits = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 8)..]);
        if (!float.IsFinite(x) || !float.IsFinite(y)
            || (!allowSphereSentinelZ || zBits != SphereMaximumZSentinel)
            && !float.IsFinite(BitConverter.UInt32BitsToSingle(zBits)))
        {
            return false;
        }

        float z = zBits == SphereMaximumZSentinel ? 0 : BitConverter.UInt32BitsToSingle(zBits);
        vector = new(x, y, z);
        return true;
    }

    private static float ReadSingle(ReadOnlySpan<byte> payload, int offset) =>
        BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]));

    private static bool IsOrdered(Vector3 minimum, Vector3 maximum) =>
        minimum.X <= maximum.X && minimum.Y <= maximum.Y && minimum.Z <= maximum.Z;
}
