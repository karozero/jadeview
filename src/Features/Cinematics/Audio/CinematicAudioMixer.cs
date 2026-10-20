namespace JadeView;

internal sealed record CinematicAudioMix(AudioAsset Audio, int ResolvedCues, string[] Diagnostics);

internal static class CinematicAudioMixer
{
    internal const int SampleRate = 48000;
    private const int Channels = 2;
    private const float MaximumDuration = 600;

    internal static CinematicAudioMix Load(Archive archive, CinematicAudioCue[] cues, float duration,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First());
        var cache = new Dictionary<uint, AudioAsset>();
        var diagnostics = new List<string>();
        AudioAsset? Resolve(uint key)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache.TryGetValue(key, out var audio)) return audio;
            if (!entries.TryGetValue(key, out var entry))
            {
                diagnostics.Add($"Missing audio resource {key:X8}.");
                return null;
            }
            try
            {
                audio = Preview.DecodeWave(archive.Read(entry));
                cache.Add(key, audio);
                return audio;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
            {
                diagnostics.Add($"{entry.Name}: {error.Message}");
                return null;
            }
        }
        return Mix(cues, duration, Resolve, diagnostics, cancellationToken);
    }

    internal static CinematicAudioMix Mix(CinematicAudioCue[] cues, float duration,
        Func<uint, AudioAsset?> resolve, List<string>? diagnostics = null, CancellationToken cancellationToken = default)
    {
        if (!float.IsFinite(duration) || duration <= 0 || duration > MaximumDuration)
            throw new InvalidDataException("Cinematic audio exceeds the supported duration (10 minutes).");
        int frameCount = checked((int)Math.Ceiling(duration * (double)SampleRate));
        var accumulated = new float[checked(frameCount * Channels)];
        int resolved = 0;
        foreach (var cue in cues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = resolve(cue.SoundKey);
            if (source == null || source.Samples.Length == 0) continue;
            MixCue(accumulated, source, cue, cancellationToken);
            resolved++;
        }
        var pcm = new short[accumulated.Length];
        for (int index = 0; index < pcm.Length; index++)
            pcm[index] = (short)Math.Clamp(MathF.Round(accumulated[index]), short.MinValue, short.MaxValue);
        return new(AudioAsset.FromSamples(pcm, Channels, SampleRate), resolved,
            diagnostics?.Distinct().ToArray() ?? []);
    }

    private static void MixCue(float[] output, AudioAsset source, CinematicAudioCue cue, CancellationToken cancellationToken)
    {
        int sourceFrames = source.Samples.Length / source.Channels;
        int first = Math.Max(0, (int)Math.Ceiling(cue.Start * SampleRate));
        int end = Math.Min(output.Length / Channels, (int)Math.Ceiling(cue.End * SampleRate));
        for (int frame = first; frame < end; frame++)
        {
            if (frame % SampleRate == 0) cancellationToken.ThrowIfCancellationRequested();
            double sourcePosition = Math.Max(0, (frame / (double)SampleRate - cue.Start) * source.Rate);
            if (sourcePosition >= sourceFrames && !cue.Loop) break;
            sourcePosition %= sourceFrames;
            int start = (int)sourcePosition;
            int next = Math.Min(start + 1, sourceFrames - 1);
            float fraction = (float)(sourcePosition - start);
            for (int channel = 0; channel < Channels; channel++)
            {
                int sourceChannel = Math.Min(channel, source.Channels - 1);
                float initial = source.Samples[start * source.Channels + sourceChannel];
                float final = source.Samples[next * source.Channels + sourceChannel];
                output[frame * Channels + channel] += (initial + (final - initial) * fraction) * cue.Volume;
            }
        }
    }
}
