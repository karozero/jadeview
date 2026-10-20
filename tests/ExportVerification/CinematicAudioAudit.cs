using System.Text.Json;
using JadeView;

internal static class CinematicAudioAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(world => world.Key == source.CinematicWorldKey));
        var cues = CinematicAudioTimeline.Build(source, world, document);
        var playback = CinematicPlayback.Discover(document, world, new()).Single(player => player.Key == clipKey);
        playback.Activate();
        var initiallyHidden = world.Objects.Where(sceneObject => sceneObject.IsControlHidden)
            .Select(sceneObject => sceneObject.Name).ToArray();
        var changes = source.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.SelectMany(payload =>
                CinematicOwnerContext.ForTrack(source, track).Select(context => (
                    key.Time, Call: CinematicLiteralCall.Decode(payload, track.Target, context))))))
            .Where(item => item.Call?.Selector is 8115 or 8152 or 8153).ToArray();
        var changedKeys = changes.Select(change => change.Call!.Target).ToHashSet();
        var times = changes.Select(change => Math.Min(MathF.BitIncrement(change.Time), source.Duration))
            .Append(0).Distinct().Order().ToArray();
        var stateSamples = new List<object>();
        foreach (float time in times)
        {
            playback.Seek(time);
            stateSamples.Add(new
            {
                Time = time,
                Objects = world.Objects.Where(sceneObject => changedKeys.Contains(sceneObject.GaoKey)).Select(sceneObject => new
                {
                    sceneObject.Name,
                    Key = sceneObject.GaoKey.ToString("X8"),
                    sceneObject.IsControlHidden,
                    sceneObject.ControlFlags,
                    sceneObject.PreviewControlFlags
                }).ToArray(),
                ExcludedActors = world.Actors.Where(actor => actor.Root.CinematicExcluded).Select(actor => actor.Root.Name).ToArray()
            });
        }
        playback.Deactivate();
        var mix = CinematicAudioMixer.Load(archive, cues, source.Duration, CancellationToken.None);
        if (mix.ResolvedCues == 0 || !mix.Audio.Samples.Any(sample => Math.Abs((int)sample) > 100))
            throw new InvalidDataException("No non-silent source audio was resolved.");
        string prefix = Path.Combine(output, $"{container}-{clipKey:X8}");
        File.WriteAllBytes(prefix + "-cinematic-audio.wav", mix.Audio.Wav);
        File.WriteAllText(prefix + "-cinematic-audio.json", JsonSerializer.Serialize(new
        {
            Clip = clipKey.ToString("X8"),
            Owner = source.CinematicOwnerKey.ToString("X8"),
            source.Duration,
            mix.ResolvedCues,
            mix.Diagnostics,
            Cues = cues.Select(cue => new
            {
                cue.Start,
                cue.End,
                cue.OwnerKey,
                cue.ModifierId,
                cue.SoundKey,
                Asset = archive.Entries.FirstOrDefault(entry => entry.Key == cue.SoundKey)?.ExportName,
                cue.Volume,
                cue.Loop
            }),
            InvisibleAtStart = initiallyHidden,
            StateSamples = stateSamples,
            Limits = "Source-time PCM mix, loaded bank language, without native spatialization, reverb or phoneme reconstruction."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AUDIO AUDIT: {container}/{clipKey:X8}: owner {source.CinematicOwnerKey:X8}, {mix.ResolvedCues} resolved cues -> {prefix}-cinematic-audio.wav");
    }
}
