using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using JadeView;

internal static class BinkFrameAudit
{
    internal static void Write(string dllPath, string videoPath, int frameIndex, string output)
    {
        string cacheDirectory = Path.Combine(output, "bink-frame-bridge");
        using var decoder = BinkFrameDecoder.Start(dllPath, videoPath, cacheDirectory);
        byte[] rgba = decoder.DecodeRgba(frameIndex);
        string imagePath = Path.Combine(output, $"bink-frame-{frameIndex:D6}.png");
        SavePng(imagePath, decoder.Width, decoder.Height, rgba);
        Console.WriteLine($"BINK FRAME: {Path.GetFileName(videoPath)} frame {frameIndex}/{decoder.FrameCount - 1} "
            + $"({decoder.Width}x{decoder.Height} at {decoder.FramesPerSecond:F3} FPS) -> {imagePath}");
    }

    private static void SavePng(string path, int width, int height, byte[] rgba)
    {
        byte[] bgra = new byte[rgba.Length];
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            bgra[offset] = rgba[offset + 2];
            bgra[offset + 1] = rgba[offset + 1];
            bgra[offset + 2] = rgba[offset];
            bgra[offset + 3] = 255;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, width, height);
        var locked = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try { Marshal.Copy(bgra, 0, locked.Scan0, bgra.Length); }
        finally { bitmap.UnlockBits(locked); }
        bitmap.Save(path, ImageFormat.Png);
    }
}
