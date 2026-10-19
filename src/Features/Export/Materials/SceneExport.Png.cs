using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace JadeView;

public sealed partial class SceneExporter
{
    private static byte[] Png(ResolvedTexture texture)
    {
        byte[] rgba = Preview.DecodeTga(texture.ToTextureAsset());
        return Png(texture.Width, texture.Height, rgba);
    }
    private static byte[] Png(int width, int height, byte[] source)
    {
        byte[] rgba = (byte[])source.Clone();
        using var bitmap = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int offset = 0; offset < rgba.Length; offset += 4)
                (rgba[offset], rgba[offset + 2]) = (rgba[offset + 2], rgba[offset]);
            for (int y = 0; y < height; y++)
                Marshal.Copy(rgba, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
