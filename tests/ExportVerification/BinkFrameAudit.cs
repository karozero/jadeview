using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using JadeView;

internal static class BinkFrameAudit
{
    internal static void WriteArchive(string archivePath, uint mediaKey, int frameIndex, string output)
    {
        var archive = new Archive(archivePath);
        Entry[] matchingEntries = archive.Entries.Where(candidate => candidate.Key == mediaKey).Take(2).ToArray();
        if (matchingEntries.Length != 1)
            throw new InvalidDataException($"Archive {archive.PathName} has no unique media entry {mediaKey:X8}.");

        byte[] video = archive.Read(matchingEntries[0]);
        if (!video.AsSpan().StartsWith("BIK"u8))
            throw new InvalidDataException($"Archive entry {mediaKey:X8} is not a Bink video.");
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "JadeView-Bink-Audit",
            Guid.NewGuid().ToString("N"));
        string videoPath = Path.Combine(temporaryDirectory, $"{mediaKey:X8}.bik");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(videoPath, video);

            string gameDirectory = Path.GetDirectoryName(archive.PathName)!;
            Write(Path.Combine(gameDirectory, "binkw32.dll"), videoPath, frameIndex, output);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

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
