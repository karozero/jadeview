using System.Numerics;

namespace JadeView;

internal static class AnimationPackedRotation
{
    // EVE_Event_InterpolationKey_Load: one signed short and two signed bytes
    // are added to the previous quaternion's quantized XYZ components.
    internal static Quaternion? DecodeDelta(int format, ReadOnlySpan<byte> bytes, Quaternion? previous)
    {
        if (previous is not { } reference) return null;
        int rotationFormat = format & 0x780;
        if (rotationFormat is not (0x100 or 0x200 or 0x400)) return null;
        if (reference.W < 0) reference = Quaternion.Negate(reference);
        int wide = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes);
        int narrowFirst = (sbyte)bytes[2];
        int narrowSecond = (sbyte)bytes[3];
        var (deltaX, deltaY, deltaZ) = rotationFormat switch
        {
            0x100 => (wide, narrowFirst, narrowSecond),
            0x200 => (narrowFirst, wide, narrowSecond),
            _ => (narrowFirst, narrowSecond, wide)
        };
        float decodedX = Add(reference.X, deltaX);
        float decodedY = Add(reference.Y, deltaY);
        float decodedZ = Add(reference.Z, deltaZ);
        float decodedW = MathF.Sqrt(Math.Max(0, 1 - decodedX * decodedX - decodedY * decodedY - decodedZ * decodedZ));
        return Quaternion.Normalize(new(decodedX, decodedY, decodedZ, decodedW));
    }

    private static float Add(float component, int delta)
    {
        int quantized = (int)(component * 32767f + .5f);
        return unchecked((short)(quantized + delta)) / 32767f;
    }
}
