using System.Buffers.Binary;

namespace JadeView;

internal static class PcLightReader
{
    // Field order is confirmed by Iris light.c and iristool GAOInnerParserLight.java;
    // the generic GRO header and GAO-inner length/type prefix are parsed by their callers.
    // This is the 28-byte light body only. The outer GRO record carries its type/length;
    // embedded GAO light data carries a length/type envelope of its own.
    private const int PayloadSize = 28;

    internal static bool TryReadGraphic(ReadOnlySpan<byte> record, out SceneLight? light, out string error)
    {
        light = null;
        if (record.Length != PayloadSize + 4 || BinaryPrimitives.ReadUInt32LittleEndian(record) != 2)
        {
            error = "PC light GRO must have type 2 and a 28-byte body.";
            return false;
        }
        return TryRead(record[4..], out light, out error);
    }

    /// <summary>
    /// Reads the fixed PC light body, not the surrounding GRO or GAO-inner envelope.
    /// No internal version field exists in the Iris PC light decoder.
    /// </summary>
    internal static bool TryRead(ReadOnlySpan<byte> payload, out SceneLight? light, out string error)
    {
        light = null;
        if (payload.Length != PayloadSize)
        {
            error = $"PC light body must be exactly {PayloadSize} bytes; received {payload.Length}.";
            return false;
        }

        float near = ReadSingle(payload, 8);
        float far = ReadSingle(payload, 12);
        float density = ReadSingle(payload, 16);
        float i8 = ReadSingle(payload, 20);
        if (!float.IsFinite(near) || !float.IsFinite(far)
            || !float.IsFinite(density) || !float.IsFinite(i8))
        {
            error = "PC light body contains a non-finite float value.";
            return false;
        }

        light = new(
            BinaryPrimitives.ReadUInt32LittleEndian(payload),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]),
            near,
            far,
            density,
            i8,
            BinaryPrimitives.ReadUInt32LittleEndian(payload[24..]));
        error = string.Empty;
        return true;
    }

    private static float ReadSingle(ReadOnlySpan<byte> payload, int offset) =>
        BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]));
}
