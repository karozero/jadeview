param(
    [Parameter(Mandatory = $true)][string]$BinkDllPath,
    [Parameter(Mandatory = $true)][string]$VideoPath
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class JadeViewBinkFrameServer
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("binkw32.dll", EntryPoint = "_BinkOpen@8", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr Open(string path, uint flags);

    [DllImport("binkw32.dll", EntryPoint = "_BinkDoFrame@4", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    public static extern int DoFrame(IntPtr handle);

    [DllImport("binkw32.dll", EntryPoint = "_BinkGoto@12", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    public static extern void Goto(IntPtr handle, uint frame, int flags);

    [DllImport("binkw32.dll", EntryPoint = "_BinkCopyToBuffer@28", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    public static extern int CopyToBuffer(IntPtr handle, IntPtr destination, int pitch,
        uint height, uint x, uint y, uint flags);

    [DllImport("binkw32.dll", EntryPoint = "_BinkClose@4", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    public static extern void Close(IntPtr handle);
}
'@

$dllPath = [IO.Path]::GetFullPath($BinkDllPath)
$videoPath = [IO.Path]::GetFullPath($VideoPath)
$null = [JadeViewBinkFrameServer]::SetDllDirectory([IO.Path]::GetDirectoryName($dllPath))
$handle = [JadeViewBinkFrameServer]::Open($videoPath, 0x08000000)
if ($handle -eq [IntPtr]::Zero) { throw 'BinkOpen returned null.' }

$inputStream = [Console]::OpenStandardInput()
$outputStream = [Console]::OpenStandardOutput()
$inputReader = [IO.BinaryReader]::new($inputStream)
$outputWriter = [IO.BinaryWriter]::new($outputStream)

try {
    $width = [Runtime.InteropServices.Marshal]::ReadInt32($handle, 0)
    $height = [Runtime.InteropServices.Marshal]::ReadInt32($handle, 4)
    $frames = [Runtime.InteropServices.Marshal]::ReadInt32($handle, 8)
    $frameRate = [Runtime.InteropServices.Marshal]::ReadInt32($handle, 20)
    $frameRateDivisor = [Runtime.InteropServices.Marshal]::ReadInt32($handle, 24)
    $pixelCount = [long]$width * $height
    if ($width -lt 1 -or $height -lt 1 -or $frames -lt 1 -or $pixelCount -gt 16777216 -or
        $frameRate -lt 1 -or $frameRateDivisor -lt 1) {
        throw "Invalid Bink metadata: ${width}x${height}, $frames frames, rate $frameRate/$frameRateDivisor."
    }

    $outputWriter.Write([int]$width)
    $outputWriter.Write([int]$height)
    $outputWriter.Write([int]$frames)
    $outputWriter.Write([int]$frameRate)
    $outputWriter.Write([int]$frameRateDivisor)
    $outputWriter.Flush()

    $pixels = [byte[]]::new([int]($pixelCount * 4))
    $pin = [Runtime.InteropServices.GCHandle]::Alloc($pixels, [Runtime.InteropServices.GCHandleType]::Pinned)
    try {
        while ($true) {
            try { $requestedFrame = $inputReader.ReadInt32() }
            catch [IO.EndOfStreamException] { break }
            if ($requestedFrame -lt 0 -or $requestedFrame -ge $frames) {
                $outputWriter.Write([int]-1)
                $outputWriter.Flush()
                continue
            }

            [JadeViewBinkFrameServer]::Goto($handle, [uint32]($requestedFrame + 1), 0)
            [void][JadeViewBinkFrameServer]::DoFrame($handle)
            $copyResult = [JadeViewBinkFrameServer]::CopyToBuffer($handle, $pin.AddrOfPinnedObject(),
                $width * 4, [uint32]$height, 0, 0, [uint32]([long]2147483651))
            if ($copyResult -ne 0) {
                $outputWriter.Write([int]-2)
                $outputWriter.Flush()
                continue
            }

            $outputWriter.Write([int]1)
            $outputWriter.Write($pixels)
            $outputWriter.Flush()
        }
    }
    finally { $pin.Free() }
}
finally {
    $inputReader.Dispose()
    $outputWriter.Dispose()
    [JadeViewBinkFrameServer]::Close($handle)
}
