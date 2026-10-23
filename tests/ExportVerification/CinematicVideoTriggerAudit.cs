using System.Text.Json;
using JadeView;

internal static class CinematicVideoTriggerAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        Entry sceneEntry = archive.Entries.Single(entry =>
            entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, sceneEntry);
        SceneDocument document = SceneLoader.Load(archive, sceneEntry);
        resolver.ResolveSceneDocument(document);
        AnimationClip clip = document.CinematicClips[clipKey];
        WorldData world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(candidate => candidate.Key == clip.CinematicWorldKey));
        CinematicPlayback playback = CinematicPlayback.Discover(document, world, resolver, archive.Entries)
            .Single(player => player.Key == clipKey);
        CinematicVideoPlaybackTimeline videoTimeline = playback.VideoPlayback;
        float[] eventTimes = CinematicLiteralCall.ReadTimeline(clip)
            .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == "OBJ_CapaSet")
            .SelectMany(item => new[] { item.Time, MathF.BitIncrement(item.Time) })
            .Append(0)
            .Append(clip.Duration)
            .Append(clip.Duration + 1)
            .Where(time => float.IsFinite(time) && time >= 0)
            .Distinct()
            .Order()
            .ToArray();
        var textures = videoTimeline.TextureKeys.Order().Select(textureKey => new
        {
            Key = textureKey.ToString("X8"),
            Descriptor = resolver.ProceduralTextures.TryGetValue(textureKey, out var descriptor)
                ? new
                {
                    descriptor.Scope,
                    descriptor.Subtype,
                    descriptor.Width,
                    descriptor.Height,
                    MediaKey = descriptor.MediaKey?.ToString("X8"),
                    descriptor.MediaName
                }
                : null,
            Samples = eventTimes.Select(time => new
            {
                Time = time,
                State = videoTimeline.Evaluate(textureKey, time)
            }).ToArray()
        }).ToArray();

        string reportPath = Path.Combine(output, $"{container}-{clipKey:X8}-video-triggers.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            Container = container,
            Clip = clipKey.ToString("X8"),
            clip.Duration,
            ControlledTextureCount = videoTimeline.ControlledTextureCount,
            Textures = textures,
            Diagnostics = document.Diagnostics
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"VIDEO TRIGGER AUDIT: {container}/{clipKey:X8}: "
            + $"{videoTimeline.ControlledTextureCount} controlled texture(s) -> {reportPath}");
    }
}
