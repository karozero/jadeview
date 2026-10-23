using System.Diagnostics;
using System.Numerics;
using JadeView;
using Raylib_cs;

internal static class CinematicPlaybackPerformanceAudit
{
    private const int WarmupFrames = 60;
    private const int MeasuredFrames = 600;
    private const int RenderWidth = 1920;
    private const int RenderHeight = 1080;
    private const int RenderWarmupFrames = 30;
    private const int MeasuredRenderFrames = 120;

    internal static void Write(string archivePath, string container, uint clipKey,
        IReadOnlyList<float>? requestedRenderTimes = null)
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
        var playback = CinematicPlayback.Discover(document, world, resolver, archive.Entries)
            .Single(item => item.Key == clipKey);
        var visibilityGeometryKeys = playback.VisibilityTargets
            .Select(sceneObject => sceneObject.RenderGeometryKey).ToHashSet();

        playback.CaptureAllocationDiagnostics = true;
        playback.Activate();
        playback.Play();
        for (int frame = 0; frame < WarmupFrames; frame++) playback.Advance(1f / 60);
        playback.ResetAllocationDiagnostics();

        var frameTimes = new double[MeasuredFrames];
        long allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < frameTimes.Length; frame++)
        {
            long startedAt = Stopwatch.GetTimestamp();
            playback.Advance(1f / 60);
            frameTimes[frame] = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        }
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
        Array.Sort(frameTimes);
        double average = frameTimes.Average();
        double p95 = frameTimes[(int)(frameTimes.Length * .95)];
        double maximum = frameTimes[^1];
        Console.WriteLine($"CINEMATIC PLAYBACK PERFORMANCE: {entry.Id}/{clipKey:X8}; "
            + $"objects={world.Objects.Count}; actors={playback.ParticipantCount}; "
            + $"frames={frameTimes.Length}; avg={average:F3}ms; p95={p95:F3}ms; "
            + $"max={maximum:F3}ms; allocated={allocatedBytes / (double)frameTimes.Length:F0}B/frame; "
            + $"effectiveUpdateFps={1000 / average:F1}; allocations={playback.AllocationDiagnostics}; "
            + $"topActors=[{playback.ActorAllocationDiagnostics}]; "
            + $"topActorPhases=[{playback.ActorPhaseAllocationDiagnostics}]; "
            + $"bodyBaselineRefreshes={playback.BodyBaselineRefreshes}");

        float[] renderTimesToSample = ResolveRenderTimes(source.Duration, requestedRenderTimes);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(RenderWidth, RenderHeight, "Cinematic render performance audit");
        try
        {
            using var renderer = new SceneRenderer
            {
                GameView = true,
                ShowGrid = false,
                ShowGizmo = false,
                ActiveWorld = world,
                Document = document,
                Resolver = resolver,
                VideoArchive = archive,
                CaptureSkinAllocationDiagnostics = true,
                CapturePerformanceDiagnostics = true
            };
            renderer.FrameAll();
            if (renderer.StatCachedGeometries != 0)
                throw new InvalidDataException("Framing a scene must use CPU bounds without uploading GPU geometry.");
            foreach (float sampleTime in renderTimesToSample)
            {
                playback.Seek(sampleTime);
                playback.Play();
                for (int frame = 0; frame < RenderWarmupFrames; frame++)
                {
                    playback.Advance(1f / 60);
                    ApplyCinematicState(renderer, playback);
                    renderer.Render(RenderWidth, RenderHeight, document, world, null, resolver);
                }

                renderer.ResetSkinAllocationDiagnostics();
                var frameRenderTimes = new double[MeasuredRenderFrames];
                int peakVisibleObjects = 0;
                int peakTriangles = 0;
                int peakSkins = 0;
                int peakUploads = 0;
                float peakUploadTime = 0;
                uint[] peakUploadKeys = [];
                Vector3 peakUploadCameraPosition = default;
                int peakActiveFlashes = 0;
                int peakActiveLightnings = 0;
                int peakWorldActorUpdates = 0;
                var worldActorUpdateTimes = new double[MeasuredRenderFrames];
                var worldActorAllocatedBytes = new long[MeasuredRenderFrames];
                var geometryPreparationAllocatedBytes = new long[MeasuredRenderFrames];
                var drawPassAllocatedBytes = new long[MeasuredRenderFrames];
                var skinningTimes = new double[MeasuredRenderFrames];
                var skinningAllocatedBytes = new long[MeasuredRenderFrames];
                long renderAllocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < frameRenderTimes.Length; frame++)
                {
                    playback.Advance(1f / 60);
                    ApplyCinematicState(renderer, playback);
                    renderer.Render(RenderWidth, RenderHeight, document, world, null, resolver);
                    long postEffectStartedAt = Stopwatch.GetTimestamp();
                    renderer.RenderCinematicAfterEffects();
                    double postEffectMilliseconds = Stopwatch.GetElapsedTime(postEffectStartedAt).TotalMilliseconds;
                    frameRenderTimes[frame] = renderer.StatRenderMilliseconds + postEffectMilliseconds;
                    peakVisibleObjects = Math.Max(peakVisibleObjects, renderer.StatVisibleObjects);
                    peakTriangles = Math.Max(peakTriangles, renderer.StatDrawnTriangles);
                    peakSkins = Math.Max(peakSkins, renderer.StatSkinUpdates);
                    if (renderer.StatGeometryUploadsThisFrame > peakUploads)
                    {
                        peakUploads = renderer.StatGeometryUploadsThisFrame;
                        peakUploadTime = playback.Time;
                        peakUploadKeys = renderer.GeometryUploadKeysThisFrame.ToArray();
                        peakUploadCameraPosition = renderer.Camera.Position;
                    }
                    peakActiveFlashes = Math.Max(peakActiveFlashes, playback.Flashes.Count);
                    peakActiveLightnings = Math.Max(peakActiveLightnings, playback.Lightnings.Count);
                    peakWorldActorUpdates = Math.Max(peakWorldActorUpdates, renderer.StatWorldActorUpdates);
                    worldActorUpdateTimes[frame] = renderer.StatWorldActorUpdateMilliseconds;
                    worldActorAllocatedBytes[frame] = renderer.StatWorldActorUpdateAllocatedBytes;
                    geometryPreparationAllocatedBytes[frame] = renderer.StatGeometryPreparationAllocatedBytes;
                    drawPassAllocatedBytes[frame] = renderer.StatDrawPassAllocatedBytes;
                    skinningTimes[frame] = renderer.StatSkinningMilliseconds;
                    skinningAllocatedBytes[frame] = renderer.StatSkinningAllocatedBytes;
                }
                long renderAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - renderAllocatedBytesBefore;
                Array.Sort(frameRenderTimes);
                Array.Sort(worldActorUpdateTimes);
                Array.Sort(worldActorAllocatedBytes);
                Array.Sort(geometryPreparationAllocatedBytes);
                Array.Sort(drawPassAllocatedBytes);
                Array.Sort(skinningTimes);
                Array.Sort(skinningAllocatedBytes);
                double renderAverage = frameRenderTimes.Average();
                double renderP95 = frameRenderTimes[(int)(frameRenderTimes.Length * .95)];
                double worldActorUpdateAverage = worldActorUpdateTimes.Average();
                double worldActorUpdateP95 = worldActorUpdateTimes[(int)(worldActorUpdateTimes.Length * .95)];
                long worldActorAllocationP95 = worldActorAllocatedBytes[(int)(worldActorAllocatedBytes.Length * .95)];
                long geometryPreparationAllocationP95 = geometryPreparationAllocatedBytes[
                    (int)(geometryPreparationAllocatedBytes.Length * .95)];
                long drawPassAllocationP95 = drawPassAllocatedBytes[(int)(drawPassAllocatedBytes.Length * .95)];
                double skinningTimeP95 = skinningTimes[(int)(skinningTimes.Length * .95)];
                long skinningAllocationP95 = skinningAllocatedBytes[(int)(skinningAllocatedBytes.Length * .95)];
                string uploadedGeometries = string.Join(", ", peakUploadKeys.Select(geometryKey =>
                {
                    var matchingObjects = world.Objects.Where(sceneObject =>
                        sceneObject.RenderGeometryKey == geometryKey).Take(2);
                    Geometry? sourceGeometry = resolver.Geometries.GetValueOrDefault(geometryKey);
                    if (sourceGeometry == null && document.Resources.TryGetValue(geometryKey, out var resource))
                        sourceGeometry = resource.Payload as Geometry;
                    string objectDetails = string.Join("/", matchingObjects.Select(sceneObject =>
                        $"{sceneObject.Name}@{sceneObject.Transform.Translation}"
                        + $" flags={sceneObject.DrawFlags:X8} hidden={sceneObject.IsControlHidden}"
                        + $" actor={sceneObject.Actor != null}"));
                    return $"{geometryKey:X8}:{objectDetails}"
                        + $" source=(billboards={sourceGeometry?.Billboards.Count},"
                        + $"particles={sourceGeometry?.Particles != null})";
                }));
                int stateTargetedUploads = peakUploadKeys.Count(visibilityGeometryKeys.Contains);
                Console.WriteLine($"CINEMATIC RENDER PERFORMANCE: {entry.Id}/{clipKey:X8} at {sampleTime:F2}s; "
                    + $"resolution={RenderWidth}x{RenderHeight}; frames={frameRenderTimes.Length}; "
                    + $"avg={renderAverage:F2}ms; p95={renderP95:F2}ms; max={frameRenderTimes[^1]:F2}ms; "
                    + $"effectiveRenderFps={1000 / renderAverage:F1}; peakVisible={peakVisibleObjects}; "
                    + $"peakTriangles={peakTriangles:N0}; peakSkins={peakSkins}; peakUploads={peakUploads}; "
                    + $"peakUploadTime={peakUploadTime:F2}s; uploadCamera={peakUploadCameraPosition}; "
                    + $"uploadedGeometries=[{uploadedGeometries}]; "
                    + $"stateTargetedPeakUploads={stateTargetedUploads}; "
                    + $"allocated={renderAllocatedBytes / (double)frameRenderTimes.Length:F0}B/frame; "
                    + $"worldAI={peakWorldActorUpdates}/{worldActorUpdateAverage:F2}ms avg/{worldActorUpdateP95:F2}ms p95 "
                    + $"/{worldActorAllocationP95}B/frame p95; "
                    + $"allocPrep={geometryPreparationAllocationP95}B/frame p95; "
                    + $"allocDraw={drawPassAllocationP95}B/frame p95; "
                    + $"skin={skinningTimeP95:F2}ms/{skinningAllocationP95}B p95; "
                    + $"skinOwners=[{renderer.SkinAllocationDiagnostics}]; "
                    + $"peakActiveFlashes={peakActiveFlashes}; peakActiveLightnings={peakActiveLightnings}");
            }
        }
        finally
        {
            Raylib.CloseWindow();
            playback.Deactivate();
        }
    }

    private static float[] ResolveRenderTimes(float duration, IReadOnlyList<float>? requestedTimes)
    {
        float[] candidateTimes = requestedTimes is { Count: > 0 }
            ? requestedTimes.ToArray()
            : [0, duration * .25f, duration * .5f, duration * .75f, Math.Max(0, duration - 1f / 60)];
        float[] validTimes = candidateTimes
            .Where(time => float.IsFinite(time) && time >= 0 && time < duration)
            .Distinct()
            .Order()
            .ToArray();
        if (validTimes.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(requestedTimes), "No render sample time falls inside the clip.");
        return validTimes;
    }

    private static void ApplyCinematicState(SceneRenderer renderer, CinematicPlayback playback)
    {
        renderer.CinematicVideoPlayback = playback.VideoPlayback;
        renderer.CinematicVideoTime = playback.Time;
        renderer.CinematicShutter = playback.Shutter;
        renderer.CinematicFlashes = playback.Flashes;
        renderer.CinematicLightnings = playback.Lightnings;
        renderer.CinematicScreenFade = playback.ScreenFade;
        renderer.CinematicMaterials = playback.MaterialState;
        renderer.CinematicOffsetEffect = playback.OffsetEffect;
        renderer.CinematicColorBalance = playback.ColorBalance;
        renderer.CinematicHorizontalFov = playback.Camera?.Lens?.Evaluate(playback.Time);
        if (playback.Camera?.RenderPose(playback.Time) is not { } pose) return;
        System.Numerics.Matrix4x4.Decompose(pose, out _, out var rotation, out var position);
        renderer.Camera.Position = position;
        renderer.Camera.Target = position + System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, rotation);
        renderer.Camera.Up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, rotation);
    }
}
