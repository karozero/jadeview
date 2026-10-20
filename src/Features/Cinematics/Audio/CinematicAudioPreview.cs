using Raylib_cs;

namespace JadeView;

internal sealed class CinematicAudioPreview : IDisposable
{
    private const int BufferFrames = 2048;
    private const double MaximumLagSeconds = .1;
    private const double MaximumLeadSeconds = .25;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task<CinematicAudioMix> loading;
    private readonly short[] buffer = new short[BufferFrames * 2];
    private AudioStream stream;
    private bool streamLoaded;
    private int nextFrame;
    private CinematicAudioMix? mix;
    internal bool Enabled = true;
    internal float Volume = .8f;
    internal bool IsLoading => !loading.IsCompleted;
    internal string Status { get; private set; } = "Loading source audio...";
    internal IReadOnlyList<string> Diagnostics => mix?.Diagnostics ?? [];

    internal CinematicAudioPreview(Archive archive, CinematicAudioCue[] cues, float duration)
    {
        var token = cancellation.Token;
        loading = Task.Run(() => CinematicAudioMixer.Load(archive, cues, duration, token), token);
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
        bool shouldPlay = Enabled && playing && mix!.ResolvedCues > 0;
        if (!shouldPlay)
        {
            StopStream();
            return;
        }
        if (!EnsureStream(time)) return;
        Raylib.SetAudioStreamVolume(stream, Volume);
        FillStream();
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
            Status = $"{mix.ResolvedCues} source audio cues · decoded PCM · loaded bank language";
        }
        return mix != null;
    }

    private bool EnsureStream(float time)
    {
        double queuedTime = nextFrame / (double)CinematicAudioMixer.SampleRate;
        bool clockDrifted = queuedTime < time - MaximumLagSeconds || queuedTime > time + MaximumLeadSeconds;
        if (streamLoaded && clockDrifted) StopStream();
        if (!streamLoaded)
        {
            Raylib.SetAudioStreamBufferSizeDefault(BufferFrames);
            stream = Raylib.LoadAudioStream(CinematicAudioMixer.SampleRate, 16, 2);
            streamLoaded = Raylib.IsAudioStreamValid(stream);
            if (!streamLoaded)
            {
                Status = "Could not open cinematic audio stream.";
                return false;
            }
            nextFrame = (int)(time * CinematicAudioMixer.SampleRate);
            Raylib.PlayAudioStream(stream);
        }
        return true;
    }

    private unsafe void FillStream()
    {
        for (int block = 0; block < 2 && Raylib.IsAudioStreamProcessed(stream); block++)
        {
            Array.Clear(buffer);
            int sourceOffset = nextFrame * 2;
            int count = Math.Min(buffer.Length, mix!.Audio.Samples.Length - sourceOffset);
            if (count > 0) Array.Copy(mix.Audio.Samples, sourceOffset, buffer, 0, count);
            fixed (short* samples = buffer) Raylib.UpdateAudioStream(stream, samples, BufferFrames);
            nextFrame += BufferFrames;
        }
    }

    private void StopStream()
    {
        if (!streamLoaded) return;
        Raylib.StopAudioStream(stream);
        Raylib.UnloadAudioStream(stream);
        streamLoaded = false;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        StopStream();
        cancellation.Dispose();
    }
}
