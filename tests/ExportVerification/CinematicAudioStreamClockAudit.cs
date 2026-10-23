using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Raylib_cs;

internal static unsafe class CinematicAudioStreamClockAudit
{
    private const uint SampleRate = 48000;
    private const int BufferFrames = 4096;
    private const int ProbeDurationMilliseconds = 1800;
    private static long processedFrames;

    internal static void Run()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady())
        {
            Console.WriteLine("AUDIO STREAM CLOCK PROBE: audio device unavailable.");
            return;
        }

        AudioStream stream = default;
        bool streamLoaded = false;
        bool processorAttached = false;
        try
        {
            Raylib.SetAudioStreamBufferSizeDefault(BufferFrames);
            stream = Raylib.LoadAudioStream(SampleRate, 16, 2);
            streamLoaded = Raylib.IsAudioStreamValid(stream);
            if (!streamLoaded)
            {
                Console.WriteLine("AUDIO STREAM CLOCK PROBE: stream creation failed.");
                return;
            }

            Raylib.SetAudioStreamVolume(stream, 0);
            Interlocked.Exchange(ref processedFrames, 0);
            Raylib.AttachAudioStreamProcessor(stream, &CountProcessedFrames);
            processorAttached = true;

            short[] silentBuffer = new short[BufferFrames * 2];
            fixed (short* samples = silentBuffer)
            {
                RefillProcessedBuffers(stream, samples);
                Raylib.PlayAudioStream(stream);
                Stopwatch elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < ProbeDurationMilliseconds)
                {
                    RefillProcessedBuffers(stream, samples);
                    Thread.Sleep(5);
                }

                elapsed.Stop();
                long frames = Interlocked.Read(ref processedFrames);
                double measuredRate = frames / elapsed.Elapsed.TotalSeconds;
                Console.WriteLine($"AUDIO STREAM CLOCK PROBE: elapsed={elapsed.Elapsed.TotalSeconds:F2}s; "
                    + $"processed={frames} frames; measuredRate={measuredRate:F0} Hz; "
                    + $"streamRate={stream.SampleRate} Hz; muted=true");
            }
        }
        finally
        {
            if (streamLoaded)
            {
                if (processorAttached) Raylib.DetachAudioStreamProcessor(stream, &CountProcessedFrames);
                Raylib.StopAudioStream(stream);
                Raylib.UnloadAudioStream(stream);
            }
            Raylib.CloseAudioDevice();
        }
    }

    private static void RefillProcessedBuffers(AudioStream stream, short* samples)
    {
        int refillCount = 0;
        while (refillCount < 2 && Raylib.IsAudioStreamProcessed(stream))
        {
            Raylib.UpdateAudioStream(stream, samples, BufferFrames);
            refillCount++;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CountProcessedFrames(void* buffer, uint frameCount)
    {
        Interlocked.Add(ref processedFrames, frameCount);
    }
}
