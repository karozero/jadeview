using System.Globalization;
using JadeView;

internal static class CinematicExportAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, float time,
        string output, bool allClips = false)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == source.CinematicWorldKey));
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        var playback = CinematicPlayback.Discover(document, world, resolver).Single(item => item.Key == clipKey);
        playback.Activate();
        try
        {
            playback.Seek(time);
            var poses = world.Objects.ToDictionary(owner => owner, owner => owner.PreviewMatrix);
            string timestamp = time.ToString("F3", CultureInfo.InvariantCulture);
            string clipScope = allClips ? "all-clips" : clipKey.ToString("X8", CultureInfo.InvariantCulture);
            string path = Path.Combine(output, $"{container}-{clipScope}-{timestamp}.glb");
            var result = SceneExporter.Export(path, world, resolver, new()
            {
                Animations = true,
                AllClips = allClips,
                CinematicDocument = document,
                CinematicClipKey = allClips ? null : clipKey,
                CinematicPlayback = playback,
                CinematicMorphTimelines = playback.SampleFacialMorphTimelines(),
                CinematicMaterialLayers = playback.MaterialState.CaptureLayers(),
                CinematicLights = playback.LightState
            });
            using var glb = new GlbReader(path);
            if (result.Meshes == 0 || !glb.Root.TryGetProperty("animations", out var animations)
                || animations.GetArrayLength() == 0)
                throw new InvalidDataException("The cinematic export contains no geometry or animations.");
            if (poses.Any(pair => pair.Key.PreviewMatrix != pair.Value))
                throw new InvalidDataException("Cinematic export did not restore the sampled preview matrices.");
            Console.WriteLine($"CINEMATIC EXPORT: {path}; scope={(allClips ? "all clips" : clipKey.ToString("X8"))}; meshes={result.Meshes}; textures={result.Textures}; "
                + $"animations={result.Animations}; warnings={result.Warnings.Length}; previewMatricesRestored=true");
            foreach (string warning in result.Warnings) Console.WriteLine("EXPORT WARNING: " + warning);
        }
        finally { playback.Deactivate(); }
    }
}
