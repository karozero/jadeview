using System.Numerics;

namespace JadeView;

/// <summary>
/// Pure packed-color equivalent of SceneRenderer's JadeVertexShader RLI and ambient
/// stage. This evaluates only the current vertex color; it does not mutate geometry/RLI.
/// </summary>
public static class ComputedVertexColorEvaluator
{
    private const uint UseRli = 0x00000040;
    private const uint UseAmbient = 0x00000100;
    private const uint DontScaleRli = 0x02000000;
    private const float InterpolerBias = 49152f;

    public static ComputedVertexColorResult Evaluate(in ComputedVertexColorInput input)
    {
        bool useRli = (input.EffectiveDrawFlags & UseRli) != 0;
        bool applyAmbient = (input.EffectiveDrawFlags & UseAmbient) != 0;
        uint computedColor = useRli ? input.RawPackedRli : 0;

        bool scaleRli = useRli
            && (input.EffectiveDrawFlags & DontScaleRli) == 0
            && input.RliFactor > .02f;
        if (scaleRli)
        {
            uint destinationColor = PackDestination(input.RliDestinationColor, input.RliFactor);
            computedColor = SaturatingSubtract(computedColor, destinationColor);
        }

        if (applyAmbient)
            computedColor = ApplyAmbient(computedColor, input.AmbientColor);

        return new(input.RawPackedRli, computedColor, useRli, applyAmbient);
    }

    internal static uint ApplyAmbient(uint color, Vector4 ambient) => AddAmbientRgb(color, ambient);

    private static uint PackDestination(Vector4 destinationColor, float factor)
    {
        byte red = ScaleDestinationChannel(ToByte(destinationColor.X), factor);
        byte green = ScaleDestinationChannel(ToByte(destinationColor.Y), factor);
        byte blue = ScaleDestinationChannel(ToByte(destinationColor.Z), factor);
        byte alpha = ScaleDestinationChannel(ToByte(destinationColor.W), factor);
        return Pack(red, green, blue, alpha);
    }

    private static byte ScaleDestinationChannel(byte channel, float factor)
    {
        if (factor >= .98f) return channel;
        if (factor <= .02f) return 0;

        uint interpoler = BitConverter.SingleToUInt32Bits(factor + InterpolerBias);
        uint replicatedChannel = (uint)channel * 0x01010101u;
        uint scaledChannel = 0;
        if ((interpoler & 128u) != 0) scaledChannel += (replicatedChannel & 0xfefefefeu) >> 1;
        if ((interpoler & 64u) != 0) scaledChannel += (replicatedChannel & 0xfcfcfcfcu) >> 2;
        if ((interpoler & 32u) != 0) scaledChannel += (replicatedChannel & 0xf8f8f8f8u) >> 3;
        if ((interpoler & 16u) != 0) scaledChannel += (replicatedChannel & 0xf0f0f0f0u) >> 4;
        if ((interpoler & 8u) != 0) scaledChannel += (replicatedChannel & 0xe0e0e0e0u) >> 5;
        if ((interpoler & 4u) != 0) scaledChannel += (replicatedChannel & 0xc0c0c0c0u) >> 6;
        if ((interpoler & 2u) != 0) scaledChannel += (replicatedChannel & 0x80808080u) >> 7;
        return (byte)scaledChannel;
    }

    private static uint SaturatingSubtract(uint source, uint destination)
    {
        byte red = (byte)Math.Max(0, (int)(source & 0xff) - (int)(destination & 0xff));
        byte green = (byte)Math.Max(0, (int)(source >> 8 & 0xff) - (int)(destination >> 8 & 0xff));
        byte blue = (byte)Math.Max(0, (int)(source >> 16 & 0xff) - (int)(destination >> 16 & 0xff));
        byte alpha = (byte)Math.Max(0, (int)(source >> 24) - (int)(destination >> 24));
        return Pack(red, green, blue, alpha);
    }

    private static uint AddAmbientRgb(uint color, Vector4 ambient)
    {
        byte red = AddAmbient(color, ambient.X, 0);
        byte green = AddAmbient(color, ambient.Y, 8);
        byte blue = AddAmbient(color, ambient.Z, 16);
        return (color & 0xff000000u) | Pack(red, green, blue, 0) & 0x00ffffffu;
    }

    private static byte AddAmbient(uint color, float ambient, int shift)
    {
        float source = (color >> shift & 0xff) / 255f;
        float sum = Math.Min(source + ambient, 1f);
        return ToByte(sum);
    }

    private static byte ToByte(float normalized)
    {
        if (!float.IsFinite(normalized)) return 0;
        float clamped = Math.Clamp(normalized, 0f, 1f);
        return (byte)MathF.Floor(clamped * 255f + .5f);
    }

    private static uint Pack(byte red, byte green, byte blue, byte alpha) =>
        red | (uint)green << 8 | (uint)blue << 16 | (uint)alpha << 24;
}
