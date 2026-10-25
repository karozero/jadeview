using System.Diagnostics;
using JadeView;

internal static class CinematicLoadingPerformanceAudit
{
    internal static void Run(string archivePath, string container, uint clipKey)
    {
        var archive = Measure("archive", () => new Archive(archivePath));
        var entry = archive.Entries.Single(item => item.Id == container);
        var resolver = new ResourceResolver();
        Measure("fix resources", () => resolver.LoadFixContainer(archive));
        Measure("scene resources", () => resolver.LoadSceneContainer(archive, entry));
        var document = Measure("scene document", () => SceneLoader.Load(archive, entry));
        var referenceDocument = Measure("resource references only", () => SceneLoader.Load(
            archive.Read(entry), entry.Name, archive.PathName, resourceReferencesOnly: true));
        var fullReferences = References(document);
        if (!fullReferences.SetEquals(References(referenceDocument)))
            throw new InvalidDataException("Resource-only loading changed the scene's GAO geometry/material references.");
        Console.WriteLine($"PASS resource reference parity: {fullReferences.Count} entries");
        Measure("resolve document", () => resolver.ResolveSceneDocument(document));
        var clip = document.CinematicClips[clipKey];
        var sourceWorld = document.Worlds.Single(world => world.Key == clip.CinematicWorldKey);
        var world = Measure("assemble world", () => CinematicWorldAssembler.Assemble(document, sourceWorld));
        var playback = Measure("discover selected clip", () => CinematicPlayback.DiscoverClip(
            clip, document, world, resolver, null, null, null, archive.Entries))
            ?? throw new InvalidDataException("No playback for the selected clip.");
        long previousStageAt = Stopwatch.GetTimestamp();
        playback.DiagnosticTrace = stage =>
        {
            Console.WriteLine($"TRACE +{Stopwatch.GetElapsedTime(previousStageAt).TotalMilliseconds:F1} ms: {stage}");
            previousStageAt = Stopwatch.GetTimestamp();
        };
        Measure("activate", playback.Activate);
        playback.Play();
        for (int frame = 0; frame < 5; frame++)
            Measure($"advance {frame}", () => playback.Advance(1f / 60));
        playback.DiagnosticTrace = null;
        foreach (float fraction in new[] { .25f, .5f, .75f })
        {
            float time = playback.Duration * fraction;
            Measure($"seek {time:F2}s", () => playback.Seek(time));
            playback.Play();
            Measure($"advance at {time:F2}s", () => playback.Advance(1f / 60));
        }
    }

    private static HashSet<(uint Gao, uint Geometry, uint Material)> References(SceneDocument document) =>
        document.Worlds.SelectMany(world => world.Objects)
            .Select(sceneObject => (sceneObject.GaoKey, sceneObject.GeometryKey, sceneObject.MaterialKey))
            .ToHashSet();

    private static void Measure(string label, Action action) => Measure(label, () =>
    {
        action();
        return true;
    });

    private static T Measure<T>(string label, Func<T> action)
    {
        Console.WriteLine($"START {label}");
        long startedAt = Stopwatch.GetTimestamp();
        T result = action();
        Console.WriteLine($"DONE {label}: {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1} ms");
        return result;
    }
}
