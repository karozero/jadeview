using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace JadeView;

internal sealed class CinematicAudioPreview : IDisposable
{
    // Queue a short amount of PCM ahead so normal render-frame jitter does not
    // interrupt the audio device's own playback clock.
    // Two queued buffers cover about two thirds of a second at 48 kHz. This
    // gives the independent audio clock more room to survive render-frame stalls.
    internal const int AudioBufferFrames = 16384;
    private const int QueueBlocksPerUpdate = 2;
    private const double MaximumQueueDriftSeconds = .75;
    private const double StatusRefreshIntervalSeconds = .25;
    private readonly CancellationTokenSource cancellation = new();
    private readonly CinematicAudioCue[] cues;
    private readonly Task<CinematicAudioMix> loading;
    private readonly short[] buffer = new short[AudioBufferFrames * 2];
    private static long processedAudioFrames;
    private AudioStream stream;
    private bool streamLoaded;
    private bool streamStarted;
    private bool streamProcessorAttached;
    private int drainedQueueObservations;
    private int queueResyncCount;
    private int nextFrame;
    private double streamStartTimelineTime;
    private double lastStatusRefreshTime = double.NegativeInfinity;
    private bool lastStatusEnabled;
    private bool lastStatusPlaying;
    private CinematicAudioMix? mix;
    internal bool Enabled = true;
    internal float Volume = .8f;
    internal bool IsLoading => !loading.IsCompleted;
    internal int DrainedQueueObservations => drainedQueueObservations;
    internal int QueueResyncCount => queueResyncCount;
    internal string Status { get; private set; } = "Loading source audio...";
    internal IReadOnlyList<string> Diagnostics => mix?.Diagnostics ?? [];

    internal CinematicAudioPreview(Archive archive, CinematicAudioCue[] cues, float duration)
    {
        this.cues = cues;
        var token = cancellation.Token;
        loading = LoadMixInBackground(archive, cues, duration, token);
    }

    private static Task<CinematicAudioMix> LoadMixInBackground(Archive archive,
        CinematicAudioCue[] cues, float duration, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<CinematicAudioMix>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(CinematicAudioMixer.Load(archive, cues, duration, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "JadeView cinematic audio mix",
            Priority = ThreadPriority.Lowest
        };
        worker.Start();
        return completion.Task;
    }

    internal void Update(float time, bool playing)
    {
        if (!TryCompleteLoad()) return;
        if (!Raylib.IsAudioDeviceReady())
        {
            StopStream();
            Status = "Audio device is not available.";
            return;
        }
        CinematicAudioMix loadedMix = mix!;
        bool shouldPlay = Enabled && playing && loadedMix.ResolvedCues > 0;
        if (!shouldPlay)
        {
            StopStream();
            RefreshStatus(time, playing, loadedMix.ResolvedCues);
            return;
        }
        if (streamLoaded && IsQueueOutOfSync(time))
        {
            queueResyncCount++;
            StopStream();
        }
        if (!EnsureStream(time)) return;
        Raylib.SetAudioStreamVolume(stream, Volume);
        int refilledBuffers = FillStream();
        if (streamStarted && refilledBuffers == QueueBlocksPerUpdate) drainedQueueObservations++;
        if (!streamStarted)
        {
            Raylib.PlayAudioStream(stream);
            streamStarted = true;
        }
        bool streamPlaying = Raylib.IsAudioStreamPlaying(stream);
        RefreshStatus(time, playing, loadedMix.ResolvedCues, streamPlaying);
    }

    private void RefreshStatus(float time, bool playing, int resolvedCueCount, bool streamPlaying = false)
    {
        bool stateChanged = Enabled != lastStatusEnabled || playing != lastStatusPlaying;
        bool timeMovedBackwards = time < lastStatusRefreshTime;
        if (!stateChanged && !timeMovedBackwards
            && time - lastStatusRefreshTime < StatusRefreshIntervalSeconds) return;

        lastStatusRefreshTime = time;
        lastStatusEnabled = Enabled;
        lastStatusPlaying = playing;
        Status = CinematicAudioPlaybackStatus.Describe(cues, resolvedCueCount, time, Enabled, playing);
        if (!playing || !Enabled) return;

        double queuedLeadMilliseconds = (nextFrame / (double)CinematicAudioMixer.SampleRate - time) * 1000;
        Status += streamPlaying
            ? $" · audio stream active · queued {queuedLeadMilliseconds:F0} ms"
                + $" · queue drained {drainedQueueObservations}× · resyncs {queueResyncCount}×"
            : " · audio stream failed to start";
        if (streamProcessorAttached)
        {
            double mixerClockTime = MixerClockTime(streamStartTimelineTime,
                Interlocked.Read(ref processedAudioFrames), CinematicAudioMixer.SampleRate);
            double clockDifferenceMilliseconds = (mixerClockTime - time) * 1000;
            Status += $" · audio mixer clock {mixerClockTime:F2}s "
                + $"({clockDifferenceMilliseconds:+0;-0;0} ms vs playhead)";
        }
    }

    private bool TryCompleteLoad()
    {
        if (mix == null && loading.IsCompleted)
        {
            if (!loading.IsCompletedSuccessfully)
            {
                Status = loading.Exception?.GetBaseException().Message ?? "Audio loading cancelled.";
                return false;
            }
            mix = loading.Result;
            Status = $"{mix.ResolvedCues} source audio cue assets · decoded PCM";
        }
        return mix != null;
    }

    private unsafe bool EnsureStream(float time)
    {
        if (!streamLoaded)
        {
            Raylib.SetAudioStreamBufferSizeDefault(AudioBufferFrames);
            stream = Raylib.LoadAudioStream(CinematicAudioMixer.SampleRate, 16, 2);
            streamLoaded = Raylib.IsAudioStreamValid(stream);
            if (!streamLoaded)
            {
                Status = "Could not open cinematic audio stream.";
                return false;
            }
            Interlocked.Exchange(ref processedAudioFrames, 0);
            streamStartTimelineTime = time;
            Raylib.AttachAudioStreamProcessor(stream, &CountProcessedAudioFrames);
            streamProcessorAttached = true;
            nextFrame = Math.Clamp((int)(time * CinematicAudioMixer.SampleRate),
                0, mix!.Audio.Samples.Length / 2);
        }
        return true;
    }

    private bool IsQueueOutOfSync(float time)
    {
        double queuedTime = nextFrame / (double)CinematicAudioMixer.SampleRate;
        double queueCapacity = QueueBlocksPerUpdate * AudioBufferFrames
            / (double)CinematicAudioMixer.SampleRate;
        return IsQueueOutOfSync(queuedTime, time, MaximumQueueDriftSeconds, queueCapacity);
    }

    internal static bool IsQueueOutOfSync(double queuedTime, double playheadTime,
        double maximumDrift, double queueCapacity) =>
        queuedTime < playheadTime - maximumDrift
        || queuedTime > playheadTime + maximumDrift + queueCapacity;

    private unsafe int FillStream()
    {
        int refilledBuffers = 0;
        for (; refilledBuffers < QueueBlocksPerUpdate && Raylib.IsAudioStreamProcessed(stream); refilledBuffers++)
        {
            int copiedFrames = CopyAudioFrames(buffer, mix!.Audio.Samples, nextFrame);
            fixed (short* samples = buffer)
                Raylib.UpdateAudioStream(stream, samples, AudioBufferFrames);
            nextFrame += copiedFrames;
            if (copiedFrames < AudioBufferFrames) nextFrame = mix.Audio.Samples.Length / 2;
        }
        return refilledBuffers;
    }

    internal static int CopyAudioFrames(Span<short> output, short[] samples, int firstFrame)
    {
        int requestedFrames = output.Length / 2;
        int availableFrames = Math.Clamp(samples.Length / 2 - Math.Max(firstFrame, 0), 0, requestedFrames);
        int availableSamples = availableFrames * 2;
        if (availableSamples > 0)
            samples.AsSpan(Math.Max(firstFrame, 0) * 2, availableSamples).CopyTo(output);
        output[availableSamples..].Clear();
        return availableFrames;
    }

    internal static double MixerClockTime(double streamStartTime, long processedFrames, int sampleRate) =>
        streamStartTime + Math.Max(0, processedFrames) / (double)sampleRate;

    internal bool TryGetTimelineClock(out double timelineTime)
    {
        if (!streamLoaded || !streamStarted || !streamProcessorAttached)
        {
            timelineTime = 0;
            return false;
        }

        timelineTime = MixerClockTime(streamStartTimelineTime,
            Interlocked.Read(ref processedAudioFrames), CinematicAudioMixer.SampleRate);
        return double.IsFinite(timelineTime);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void CountProcessedAudioFrames(void* bufferData, uint frameCount)
    {
        Interlocked.Add(ref processedAudioFrames, frameCount);
    }

    private unsafe void StopStream()
    {
        if (!streamLoaded) return;
        if (streamProcessorAttached)
        {
            Raylib.DetachAudioStreamProcessor(stream, &CountProcessedAudioFrames);
            streamProcessorAttached = false;
        }
        Raylib.StopAudioStream(stream);
        Raylib.UnloadAudioStream(stream);
        streamLoaded = false;
        streamStarted = false;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        StopStream();
        cancellation.Dispose();
    }
}
