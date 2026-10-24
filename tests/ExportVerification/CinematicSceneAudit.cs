using System.Text.Json;
using JadeView;

internal static class CinematicSceneAudit
{
    internal static void Write(string archivePath, string container, string outputDirectory, uint? clipKey = null)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);

        var export = CinematicSceneExportAudit.Run(archive, entry, document, outputDirectory, clipKey);
        var sharedTracks = SummarizeSharedTracks(document);
        var report = new
        {
            Container = entry.Id,
            Worlds = document.Worlds.Select(world => new { world.Name, Actors = world.Actors.Count }),
            Actors = SummarizeActors(document),
            SharedTracks = sharedTracks,
            FacialEvents = SummarizeFacialEvents(document),
            ChannelFormEvents = SummarizeChannelFormEvents(document),
            MorphSignalTracks = CinematicMorphSignalAudit.Collect(document),
            MouthControllerDiagnostics = SummarizeControllerDiagnostics(document),
            ExportedTestGlb = export.Path,
            ExportedFacialMeshes = export.MorphMeshes,
            ExportedFacialKeyframes = export.MorphKeyframes,
            MorphBindings = export.MorphBindings,
            BodyBindings = export.BodyBindings,
            Limits = "This audit proves clip-key binding to parsed actors. It does not simulate the game's brain, event queue, dialogue/audio or camera runtime."
        };

        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, $"{entry.Id}-cinematic-playback.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        int actorCount = document.Worlds.Sum(world => world.Actors.Count);
        Console.WriteLine($"CINEMATIC AUDIT: {entry.Id}: {sharedTracks.Length} shared clips across {actorCount} actor rigs -> {reportPath}");
    }

    private static object[] SummarizeActors(SceneDocument document) =>
        document.Worlds.SelectMany(world => world.Actors.Select(actor => new
        {
            World = world.Name,
            Actor = actor.Root.Name,
            GaoKey = actor.Root.GaoKey.ToString("X8"),
            CinematicClips = actor.Clips.Where(clip => clip.Cinematic).Select(clip => new
            {
                Key = clip.Key.ToString("X8"),
                clip.Name,
                clip.Duration,
                TrackCount = clip.Tracks.Count,
                EventCount = CountOpaqueEvents(clip)
            }).ToArray()
        })).ToArray();

    private static object[] SummarizeSharedTracks(SceneDocument document) =>
        document.CinematicClips.Values
            .Select(clip => new
            {
                Clip = clip.Key.ToString("X8"),
                clip.Name,
                clip.Duration,
                Participants = FindParticipants(document, clip),
                Events = CountOpaqueEvents(clip)
            })
            .Where(clip => clip.Participants.Length > 1)
            .ToArray();

    private static string[] FindParticipants(SceneDocument document, AnimationClip clip)
    {
        var actors = document.Worlds.SelectMany(world => world.Actors)
            .Where(actor => clip.Tracks.Any(track => IsActorTrack(track, actor)))
            .Select(actor => actor.Root.Name);
        var objects = document.Worlds.SelectMany(world => world.Objects)
            .Where(sceneObject => sceneObject.Actor == null
                && clip.Tracks.Any(track => track.Bone < 0 && track.Target == sceneObject.GaoKey))
            .Select(sceneObject => sceneObject.Name);
        return actors.Concat(objects).Distinct().ToArray();
    }

    private static bool IsActorTrack(AnimationTrack track, SceneActor actor) =>
        track.Bone >= 0
            ? track.Target == actor.Root.GaoKey
            : actor.Members.Any(member => member.GaoKey == track.Target);

    private static int CountOpaqueEvents(AnimationClip clip) =>
        clip.Tracks.SelectMany(track => track.Keys).Sum(key => key.OpaquePayloads.Count);

    private static object SummarizeFacialEvents(SceneDocument document)
    {
        var events = document.CinematicClips.Values.SelectMany(clip => clip.Tracks
            .SelectMany(track => track.Keys.Select(key => new
            {
                Clip = clip.Key.ToString("X8"),
                Target = track.Target.ToString("X8"),
                Event = CinematicFacialEvent.Decode(key)
            })))
            .Where(item => item.Event != null)
            .OrderBy(item => item.Event!.Time)
            .ToArray();

        return new
        {
            Total = events.Length,
            MouthCycleStarts = events.Count(item => item.Event!.AutomaticMouth == true),
            MouthCycleStops = events.Count(item => item.Event!.AutomaticMouth == false),
            SourceTimes = events.Select(item => new
            {
                item.Clip,
                item.Target,
                item.Event!.ActorTargetKey,
                item.Event.Time,
                item.Event.AutomaticMouth,
                item.Event.MouthFactor,
                item.Event.MouthSpeed,
                item.Event.Commands
            }).ToArray()
        };
    }

    private static object SummarizeChannelFormEvents(SceneDocument document)
    {
        var events = document.CinematicClips.Values.SelectMany(clip => clip.Tracks
            .SelectMany(track => track.Keys.Select(key => new
            {
                Clip = clip.Key.ToString("X8"),
                Target = track.Target.ToString("X8"),
                Event = CinematicChannelFormEvent.Decode(key)
            })))
            .Where(item => item.Event != null)
            .OrderBy(item => item.Event!.Time)
            .ToArray();

        return new
        {
            Total = events.Length,
            SourceEvents = events.Select(item => new
            {
                item.Clip,
                item.Target,
                item.Event!.Time,
                item.Event.ModuleSelector,
                item.Event.Channel,
                item.Event.Slot,
                item.Event.Group
            }).ToArray()
        };
    }

    private static string[] SummarizeControllerDiagnostics(SceneDocument document) =>
        document.Diagnostics.Where(message =>
            message.Contains("Lip-sync controller", StringComparison.Ordinal)
            || message.Contains("Lip-sync type ", StringComparison.Ordinal)
            || message.Contains("Cinematic object", StringComparison.Ordinal)
            || message.Contains("No qualified facial controller", StringComparison.Ordinal))
            .ToArray();
}
