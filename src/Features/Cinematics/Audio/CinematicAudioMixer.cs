namespace JadeView;

internal sealed record CinematicAudioMix(AudioAsset Audio, int ResolvedCues, string[] Diagnostics);

internal static class CinematicAudioMixer
{
    internal const int SampleRate = 48000;
    private const int Channels = 2;
    private const float MaximumDuration = 600;
    private const int MixBlockFrames = 4096;
    private const int EdgeFadeFrames = SampleRate / 200;

    internal static CinematicAudioMix Load(Archive archive, CinematicAudioCue[] cues, float duration,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First());
        var cache = new Dictionary<uint, AudioAsset>();
        var fadeCache = new Dictionary<uint, CinematicSoundFadeEnvelope?>();
        var modifierCache = new Dictionary<uint, CinematicSoundModifierFile?>();
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

        CinematicSoundFadeEnvelope? ResolveFade(uint key)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fadeCache.TryGetValue(key, out var fade)) return fade;
            if (!entries.TryGetValue(key, out var entry))
            {
                diagnostics.Add($"Missing sound fade resource {key:X8}.");
                fadeCache.Add(key, null);
                return null;
            }
            try
            {
                fade = CinematicSoundFadeEnvelope.Read(archive.Read(entry));
                if (fade == null)
                    diagnostics.Add($"{entry.Name}: unsupported or malformed Jade sound fade insert.");
                fadeCache.Add(key, fade);
                return fade;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException
                or OverflowException)
            {
                diagnostics.Add($"{entry.Name}: {error.Message}");
                fadeCache.Add(key, null);
                return null;
            }
        }

        CinematicSoundModifierFile? ResolveModifier(uint key)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (modifierCache.TryGetValue(key, out var modifier)) return modifier;
            if (!entries.TryGetValue(key, out var entry)
                || !entry.Name.EndsWith(".smd", StringComparison.OrdinalIgnoreCase))
                return null;
            modifier = CinematicSoundModifierFile.Read(archive.Read(entry));
            modifierCache.Add(key, modifier);
            if (modifier == null)
                diagnostics.Add($"{entry.Name}: unsupported or malformed Jade sound modifier.");
            return modifier;
        }

        return Mix(cues, duration, Resolve, diagnostics, cancellationToken, ResolveFade, ResolveModifier);
    }

    internal static CinematicAudioMix Mix(CinematicAudioCue[] cues, float duration,
        Func<uint, AudioAsset?> resolve, List<string>? diagnostics = null,
        CancellationToken cancellationToken = default,
        Func<uint, CinematicSoundFadeEnvelope?>? resolveFade = null,
        Func<uint, CinematicSoundModifierFile?>? resolveModifier = null)
    {
        if (!float.IsFinite(duration) || duration <= 0 || duration > MaximumDuration)
            throw new InvalidDataException("Cinematic audio exceeds the supported duration (10 minutes).");
        int frameCount = checked((int)Math.Ceiling(duration * (double)SampleRate));
        var resolvedCues = new List<(CinematicAudioCue Cue, AudioAsset Source,
            CinematicSoundFadeEnvelope? FadeOut)>();
        int resolved = 0;
        foreach (var cue in cues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CinematicSoundModifierFile? modifier = resolveModifier?.Invoke(cue.SoundKey);
            if (modifier?.HasPlaylist == true)
            {
                diagnostics?.Add($"Sound modifier {cue.SoundKey:X8} uses a playlist; deterministic cue selection is unavailable.");
                continue;
            }
            uint sourceKey = modifier?.SoundKey ?? cue.SoundKey;
            var source = resolve(sourceKey);
            if (source == null || source.Samples.Length == 0) continue;

            CinematicSoundFadeEnvelope? fadeOut = cue.StopTime >= 0
                && modifier is { FadeOutKey: not (0 or uint.MaxValue) } soundModifier
                ? resolveFade?.Invoke(soundModifier.FadeOutKey)
                : null;
            float end = cue.StopTime >= 0
                ? Math.Min(duration, cue.StopTime + (fadeOut?.Duration ?? 0))
                : cue.End;
            var resolvedCue = cue with { End = end };
            if (resolvedCue.End <= resolvedCue.Start) continue;
            resolvedCues.Add((resolvedCue, source, fadeOut));
            resolved++;
        }

        var pcm = new short[checked(frameCount * Channels)];
        var accumulated = new float[Math.Min(frameCount, MixBlockFrames) * Channels];
        for (int blockStart = 0; blockStart < frameCount; blockStart += MixBlockFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockFrameCount = Math.Min(MixBlockFrames, frameCount - blockStart);
            int blockSampleCount = blockFrameCount * Channels;
            Array.Clear(accumulated, 0, blockSampleCount);
            foreach (var (cue, source, fadeOut) in resolvedCues)
                MixCue(accumulated, blockStart, blockFrameCount, source, cue, fadeOut, cancellationToken);

            int outputOffset = blockStart * Channels;
            for (int index = 0; index < blockSampleCount; index++)
                pcm[outputOffset + index] = (short)Math.Clamp(MathF.Round(accumulated[index]),
                    short.MinValue, short.MaxValue);
        }

        if (diagnostics != null)
            diagnostics.AddRange(CinematicAudioDiscontinuityDetector.DescribePossibleClicks(
                pcm, Channels, SampleRate));

        return new(AudioAsset.FromPcmSamples(pcm, Channels, SampleRate), resolved,
            diagnostics?.Distinct().ToArray() ?? []);
    }

    private static void MixCue(float[] output, int blockStartFrame, int blockFrameCount,
        AudioAsset source, CinematicAudioCue cue, CinematicSoundFadeEnvelope? fadeOut,
        CancellationToken cancellationToken)
    {
        int sourceFrames = source.Samples.Length / source.Channels;
        int loopStartFrame = cue.Loop && source.LoopEndFrame > source.LoopStartFrame
            ? Math.Clamp(source.LoopStartFrame, 0, sourceFrames)
            : 0;
        int loopEndFrame = cue.Loop && source.LoopEndFrame > loopStartFrame
            ? Math.Clamp(source.LoopEndFrame, loopStartFrame, sourceFrames)
            : sourceFrames;
        int loopFrameCount = loopEndFrame - loopStartFrame;
        int blockEndFrame = blockStartFrame + blockFrameCount;
        int first = Math.Max(blockStartFrame, Math.Max(0, (int)Math.Ceiling(cue.Start * SampleRate)));
        int end = Math.Min(blockEndFrame, (int)Math.Ceiling(cue.End * SampleRate));
        for (int frame = first; frame < end; frame++)
        {
            if ((frame - first) % SampleRate == 0) cancellationToken.ThrowIfCancellationRequested();
            double sourcePosition = Math.Max(0, (frame / (double)SampleRate - cue.Start) * source.Rate);
            if (sourcePosition >= sourceFrames && !cue.Loop) break;
            if (cue.Loop && loopFrameCount > 0 && sourcePosition >= loopEndFrame)
                sourcePosition = loopStartFrame + (sourcePosition - loopStartFrame) % loopFrameCount;
            int start = (int)sourcePosition;
            int next = cue.Loop && loopFrameCount > 0 && start + 1 >= loopEndFrame
                ? loopStartFrame
                : Math.Min(start + 1, sourceFrames - 1);
            float fraction = (float)(sourcePosition - start);
            float elapsedFrames = frame - cue.Start * SampleRate;
            float remainingFrames = cue.End * SampleRate - frame;
            float edgeFade = Math.Min(Math.Clamp(elapsedFrames / EdgeFadeFrames, 0, 1),
                Math.Clamp(remainingFrames / EdgeFadeFrames, 0, 1));
            float fadeFactor = fadeOut != null && cue.StopTime >= 0 && frame / (double)SampleRate >= cue.StopTime
                ? fadeOut.Evaluate((float)(frame / (double)SampleRate - cue.StopTime))
                : 1;
            for (int channel = 0; channel < Channels; channel++)
            {
                int sourceChannel = Math.Min(channel, source.Channels - 1);
                float initial = source.Samples[start * source.Channels + sourceChannel];
                float final = source.Samples[next * source.Channels + sourceChannel];
                int outputOffset = (frame - blockStartFrame) * Channels + channel;
                output[outputOffset] += (initial + (final - initial) * fraction) * cue.Volume * fadeFactor * edgeFade;
            }
        }
    }
}
