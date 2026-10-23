using System.Diagnostics;
using System.Text;

namespace JadeView;

/// <summary>
/// x64-to-x86 bridge for the retail PC Bink decoder. The game's DLL is loaded by
/// 32-bit Windows PowerShell and is never copied into or shipped with JadeView.
/// </summary>
internal sealed class BinkFrameDecoder : IDisposable
{
    private const string ScriptResourceName = "JadeView.BinkFrameServer.ps1";
    private const string ScriptFileName = "BinkFrameServer.ps1";
    private const uint BinkSurface32CopyAll = 0x80000003;
    private static readonly object ServerScriptLock = new();
    private readonly Process process;
    private readonly Stream input;
    private readonly Stream output;
    private readonly int pixelBytes;
    private readonly byte[] bgraBuffer;
    private bool disposed;

    private BinkFrameDecoder(Process process, int width, int height, int frameCount,
        int frameRate, int frameRateDivisor)
    {
        this.process = process;
        input = process.StandardInput.BaseStream;
        output = process.StandardOutput.BaseStream;
        Width = width;
        Height = height;
        FrameCount = frameCount;
        FrameRate = frameRate;
        FrameRateDivisor = frameRateDivisor;
        pixelBytes = checked(width * height * 4);
        bgraBuffer = new byte[pixelBytes];
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int FrameCount { get; }
    internal int FrameRate { get; }
    internal int FrameRateDivisor { get; }
    internal double FramesPerSecond => FrameRate / (double)FrameRateDivisor;
    internal double DurationSeconds => FrameCount * (double)FrameRateDivisor / FrameRate;

    internal static BinkFrameDecoder Start(string binkDllPath, string videoPath, string cacheDirectory)
    {
        string bridgePath = ResolvePowerShellPath();
        string fullDllPath = Path.GetFullPath(binkDllPath);
        string fullVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(fullDllPath)) throw new FileNotFoundException("Retail binkw32.dll was not found.", fullDllPath);
        if (!File.Exists(fullVideoPath)) throw new FileNotFoundException("Bink video resource was not found.", fullVideoPath);

        Directory.CreateDirectory(cacheDirectory);
        string scriptPath = WriteServerScript(cacheDirectory);
        var startInfo = new ProcessStartInfo(bridgePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(fullDllPath)!
        };
        foreach (string argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
            "-BinkDllPath", fullDllPath, "-VideoPath", fullVideoPath
        }) startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo };
        var errorOutput = new StringBuilder();
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data != null) errorOutput.AppendLine(eventArgs.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("Could not start the 32-bit Bink frame bridge.");
        process.BeginErrorReadLine();

        try
        {
            Span<byte> header = stackalloc byte[20];
            process.StandardOutput.BaseStream.ReadExactly(header);
            int width = BitConverter.ToInt32(header[..4]);
            int height = BitConverter.ToInt32(header.Slice(4, 4));
            int frameCount = BitConverter.ToInt32(header.Slice(8, 4));
            int frameRate = BitConverter.ToInt32(header.Slice(12, 4));
            int frameRateDivisor = BitConverter.ToInt32(header.Slice(16, 4));
            bool invalidMetadata = width is < 1 or > 8192 || height is < 1 or > 8192
                || (long)width * height > 16777216 || frameCount < 1
                || frameRate < 1 || frameRateDivisor < 1;
            if (invalidMetadata)
                throw new InvalidDataException($"Bink frame bridge returned invalid metadata: {width}x{height}, "
                    + $"{frameCount} frames at {frameRate}/{frameRateDivisor} FPS.");

            return new(process, width, height, frameCount, frameRate, frameRateDivisor);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            string diagnostics = errorOutput.ToString().Trim();
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw new InvalidDataException("The 32-bit Bink frame bridge could not open the video. "
                + (diagnostics.Length > 0 ? diagnostics : error.Message), error);
        }
    }

    internal byte[] DecodeRgba(int frameIndex)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frameIndex < 0 || frameIndex >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        Span<byte> request = stackalloc byte[4];
        BitConverter.TryWriteBytes(request, frameIndex);
        input.Write(request);
        input.Flush();

        Span<byte> result = stackalloc byte[4];
        output.ReadExactly(result);
        int status = BitConverter.ToInt32(result);
        if (status != 1)
            throw new InvalidDataException($"BinkCopyToBuffer failed for frame {frameIndex} (status {status}).");
        output.ReadExactly(bgraBuffer);

        return BgraToRgba(bgraBuffer);
    }

    internal static int FrameIndexAt(double timeSeconds, int frameCount, int frameRate, int frameRateDivisor)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds <= 0) return 0;
        if (frameCount <= 0 || frameRate <= 0 || frameRateDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Bink frame timing metadata must be positive.");
        double sourceFrame = Math.Floor(timeSeconds * frameRate / frameRateDivisor);
        return (int)(sourceFrame % frameCount);
    }

    internal static bool HasReachedEnd(double timeSeconds, int frameCount, int frameRate, int frameRateDivisor)
    {
        if (frameCount <= 0 || frameRate <= 0 || frameRateDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Bink frame timing metadata must be positive.");
        return double.IsFinite(timeSeconds)
            && timeSeconds >= frameCount * (double)frameRateDivisor / frameRate;
    }

    internal static byte[] BgraToRgba(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % 4 != 0) throw new ArgumentException("Bink 32-bit frame length must be divisible by four.", nameof(bgra));
        var rgba = new byte[bgra.Length];
        for (int offset = 0; offset < bgra.Length; offset += 4)
        {
            rgba[offset] = bgra[offset + 2];
            rgba[offset + 1] = bgra[offset + 1];
            rgba[offset + 2] = bgra[offset];
            rgba[offset + 3] = 255;
        }
        return rgba;
    }

    private static string ResolvePowerShellPath()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string architectureDirectory = Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32";
        string path = Path.Combine(windowsDirectory, architectureDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(path)) return path;
        throw new FileNotFoundException("32-bit Windows PowerShell is required to load the retail x86 Bink decoder.", path);
    }

    private static string WriteServerScript(string cacheDirectory)
    {
        lock (ServerScriptLock)
        {
            string path = Path.Combine(cacheDirectory, ScriptFileName);
            if (File.Exists(path)) return path;

            using Stream resource = typeof(BinkFrameDecoder).Assembly.GetManifestResourceStream(ScriptResourceName)
                ?? throw new InvalidOperationException($"Missing embedded Bink server resource: {ScriptResourceName}.");
            using var destination = File.Create(path);
            resource.CopyTo(destination);
            return path;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        input.Dispose();
        if (!process.HasExited)
        {
            if (!process.WaitForExit(500)) process.Kill(entireProcessTree: true);
        }
        process.Dispose();
    }
}
