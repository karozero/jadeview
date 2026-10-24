using System.Buffers.Binary;
using System.Numerics;
using JadeView;

internal static class PcObjectBoundsChecks
{
    internal static void Verify()
    {
        VerifyAabbLayoutAndOffsets();
        VerifyOrientedBoxLayout();
        VerifySphereSentinel();
        VerifyMalformedBoundsAreRejected();
        Console.WriteLine("PASS: PC GAO AABB/OBBox/sphere bounds offsets, layouts, sentinel, and malformed input checks");
    }

    private static void VerifyAabbLayoutAndOffsets()
    {
        byte[] record = CreateRecord(PcObjectBoundsReader.AxisAlignedPayloadSize);
        WriteVector(record, PcObjectBoundsReader.SerializedOffset, new(-2, -3, -4));
        WriteVector(record, PcObjectBoundsReader.SerializedOffset + 12, new(2, 3, 4));

        Require(PcObjectBoundsReader.TryRead(record, 0, out PcObjectBounds? bounds, out string error), error);
        Require(bounds?.Kind == PcObjectBoundsKind.AxisAlignedBox
            && bounds.Minimum == new Vector3(-2, -3, -4)
            && bounds.Maximum == new Vector3(2, 3, 4)
            && bounds.LocalMinimum is null && bounds.LocalMaximum is null && bounds.SphereRadius is null,
            "A non-OBBox record must read two native AABB vectors at byte offset 90.");
    }

    private static void VerifyOrientedBoxLayout()
    {
        byte[] record = CreateRecord(PcObjectBoundsReader.OrientedPayloadSize);
        WriteVector(record, PcObjectBoundsReader.SerializedOffset, new(-5, -6, -7));
        WriteVector(record, PcObjectBoundsReader.SerializedOffset + 12, new(5, 6, 7));
        WriteVector(record, PcObjectBoundsReader.SerializedOffset + 24, new(-1, -2, -3));
        WriteVector(record, PcObjectBoundsReader.SerializedOffset + 36, new(1, 2, 3));

        Require(PcObjectBoundsReader.TryRead(record, PcObjectBounds.OrientedBoundsIdentityFlag,
            out PcObjectBounds? bounds, out string error), error);
        Require(bounds?.Kind == PcObjectBoundsKind.OrientedBox
            && bounds.Minimum == new Vector3(-5, -6, -7)
            && bounds.Maximum == new Vector3(5, 6, 7)
            && bounds.LocalMinimum is Vector3 localMinimum && localMinimum == new Vector3(-1, -2, -3)
            && bounds.LocalMaximum is Vector3 localMaximum && localMaximum == new Vector3(1, 2, 3),
            "Identity bit 0x80000 must select the four-vector, 48-byte OBBox payload.");

        byte[] truncated = CreateRecord(PcObjectBoundsReader.AxisAlignedPayloadSize);
        Require(!PcObjectBoundsReader.TryRead(truncated, PcObjectBounds.OrientedBoundsIdentityFlag, out _, out _),
            "An OBBox reader must reject a record containing only the two AABB vectors.");
    }

    private static void VerifySphereSentinel()
    {
        byte[] record = CreateRecord(PcObjectBoundsReader.AxisAlignedPayloadSize);
        WriteVector(record, PcObjectBoundsReader.SerializedOffset, new(10, 20, 30));
        WriteSingle(record, PcObjectBoundsReader.SerializedOffset + 12, 4);
        WriteSingle(record, PcObjectBoundsReader.SerializedOffset + 16, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(PcObjectBoundsReader.SerializedOffset + 20), 0xffff_ffff);

        Require(PcObjectBoundsReader.TryRead(record, 0, out PcObjectBounds? bounds, out string error), error);
        Require(bounds?.Kind == PcObjectBoundsKind.Sphere
            && bounds.Minimum == new Vector3(10, 20, 30)
            && bounds.SphereRadius == 4 && bounds.Maximum == Vector3.Zero,
            "The serialized BVMax.z -1 sentinel must be represented as sphere center/radius, not NaN geometry.");
    }

    private static void VerifyMalformedBoundsAreRejected()
    {
        byte[] valid = CreateRecord(PcObjectBoundsReader.AxisAlignedPayloadSize);
        WriteVector(valid, PcObjectBoundsReader.SerializedOffset, new(-1, -1, -1));
        WriteVector(valid, PcObjectBoundsReader.SerializedOffset + 12, new(1, 1, 1));
        foreach (int length in new[] { 4, 89, 113 })
        {
            byte[] truncated = valid.AsSpan(0, length).ToArray();
            Require(!PcObjectBoundsReader.TryRead(truncated, 0, out PcObjectBounds? parsed, out string error)
                && parsed is null && error.Contains("Truncated", StringComparison.OrdinalIgnoreCase),
                $"Truncated PC GAO bounds input of {length} bytes was not rejected clearly.");
        }

        byte[] nonFinite = (byte[])valid.Clone();
        WriteSingle(nonFinite, PcObjectBoundsReader.SerializedOffset + 4, float.PositiveInfinity);
        Require(!PcObjectBoundsReader.TryRead(nonFinite, 0, out _, out string nonFiniteError)
            && nonFiniteError.Contains("non-finite", StringComparison.OrdinalIgnoreCase),
            "Non-finite bounds coordinates must be rejected.");

        byte[] inverted = (byte[])valid.Clone();
        WriteVector(inverted, PcObjectBoundsReader.SerializedOffset, new(2, -1, -1));
        Require(!PcObjectBoundsReader.TryRead(inverted, 0, out _, out string orderError)
            && orderError.Contains("minimum", StringComparison.OrdinalIgnoreCase),
            "Bounds with reversed min/max coordinates must be rejected.");
    }

    private static byte[] CreateRecord(int boundsSize)
    {
        byte[] record = new byte[PcObjectBoundsReader.SerializedOffset + boundsSize];
        ".gao"u8.CopyTo(record);
        return record;
    }

    private static void WriteVector(byte[] record, int offset, Vector3 vector)
    {
        WriteSingle(record, offset, vector.X);
        WriteSingle(record, offset + 4, vector.Y);
        WriteSingle(record, offset + 8, vector.Z);
    }

    private static void WriteSingle(byte[] record, int offset, float value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset), BitConverter.SingleToUInt32Bits(value));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
