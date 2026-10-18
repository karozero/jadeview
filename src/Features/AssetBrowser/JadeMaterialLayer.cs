using System.Numerics;

namespace JadeView;

// PC SubTexture layout, decoded independently from Iris common.a.d_0/Common.j.
public sealed record JadeMaterialLayer(ushort Custom, uint Flags, uint TransformU, uint TransformV, uint Texture)
{
    public int UvMode => (int)(Flags >> 20 & 15);
    public int ColorMode => (int)(Flags >> 12 & 15);
    public int Blend => (int)(Flags >> 16 & 15);
    public bool DepthEqual => (Flags & 0x100) != 0;
    public bool DepthWrite => (Flags & 0x200) == 0;
    public bool WriteRgb => (Flags & 0x40) == 0;
    public bool WriteAlpha => (Flags & 0x20) == 0;
    // Iris: (color | 0xff000000) XOR (customAlpha XOR 0xff000000).
    public float OverrideAlpha => (Custom >> 8) / 255f;
    public bool Disabled => (Flags & 0x800) != 0;
    public Vector4 UvMatrix(float time, out Vector2 offset)
    {
        offset = Vector2.Zero;
        if ((TransformU == 0x3f800000 && TransformV == 0x3f800000) || TransformU == 0 || TransformV == 0)
            return new(1, 0, 0, 1);
        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        static float Shift(uint bits) => bits is 0 or 0x08001000 ? 0 : F(bits << 16);
        float u = F(TransformU & 0xfffe0000), v = F(TransformV & 0xfffe0000);
        offset = new(Shift(TransformU) * u, Shift(TransformV) * v);
        // Jade packs scrolling speeds into the low 16 bits of the scale words.
        static float Scroll(float speed, float time)
        {
            uint origin = BitConverter.SingleToUInt32Bits(12288f);
            return F(origin | (BitConverter.SingleToUInt32Bits(speed * time + 12288f) & 0x3ff)) - 12288f;
        }
        if ((Flags & 0x40000000) != 0) offset.X = Scroll(offset.X, time);
        if ((Flags & 0x80000000) != 0) offset.Y = Scroll(offset.Y, time);
        int rotation = (int)((TransformV & 1) | 4 * (TransformU & 1) |
            ((TransformU & 0x10000) | (TransformV >> 2 & 0x4000)) >> 13);
        ReadOnlySpan<float> sine = [0, .3826834f, .7071067f, .9238795f, 1, .9238795f, .7071067f, .3826834f,
            0, -.3826834f, -.7071067f, -.9238795f, -1, -.9238795f, -.7071067f, -.3826834f];
        float sn = sine[rotation & 15], cs = sine[(rotation + 4) & 15];
        return new(u * cs, u * sn, -v * sn, v * cs);
    }
    public static List<JadeMaterialLayer> Read(byte[]? bytes)
    {
        var result = new List<JadeMaterialLayer>();
        if (bytes == null || bytes.Length < 36) return result;
        int type = Preview.I32(bytes, 0);
        if (type == 5)
            for (int p = 36; p + 20 <= bytes.Length; p += 20)
                result.Add(new((ushort)Preview.U16(bytes, p + 2), Preview.U32(bytes, p + 4),
                    Preview.U32(bytes, p + 8), Preview.U32(bytes, p + 12), Preview.U32(bytes, p + 16)));
        else if (type == 3)
            result.Add(new(0, 7, 0, 0, Preview.U32(bytes, 28)));
        return result;
    }
}
