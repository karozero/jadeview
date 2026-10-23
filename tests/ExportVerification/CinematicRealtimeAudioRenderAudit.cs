using System.Diagnostics;
using System.Numerics;
using JadeView;
using Raylib_cs;
using Color = Raylib_cs.Color;

internal static class CinematicRealtimeAudioRenderAudit
{
    private const int DefaultRenderWidth = 1600;
    private const int DefaultRenderHeight = 900;
    private const int MaximumRenderDimension = 8192;

    internal static void Run(string archivePath, string container, uint clipKey,
        float startTime, float duration, int renderWidth = DefaultRenderWidth,
        int renderHeight = DefaultRenderHeight)
    {
        if (!float.IsFinite(startTime) || !float.IsFinite(duration) || startTime < 0 || duration <= 0)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Start and duration must be finite and nonnegative.");
        if (renderWidth is < 1 or > MaximumRenderDimension
            || renderHeight is < 1 or > MaximumRenderDimension)
            throw new ArgumentOutOfRangeException(nameof(renderWidth),
                $"Render dimensions must be between 1 and {MaximumRenderDimension} pixels.");

        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        if (startTime + duration > source.Duration)
            throw new ArgumentOutOfRangeException(nameof(duration), "Audit range exceeds cinematic duration.");

        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == source.CinematicWorldKey));
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        var playback = CinematicPlayback.Discover(document, world, resolver, archive.Entries)
            .Single(item => item.Key == clipKey);

        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(renderWidth, renderHeight, "Cinematic real-time audio/render audit");
        if (!Raylib.IsWindowReady()) throw new InvalidOperationException("Could not initialize audit window.");

        CinematicAudioPreview? audio = null;
        bool audioDeviceReady = false;
        try
        {
            Raylib.InitAudioDevice();
            audioDeviceReady = Raylib.IsAudioDeviceReady();
            if (!audioDeviceReady)
            {
                Console.WriteLine("CINEMATIC AV AUDIT: audio device unavailable; live synchronized test skipped.");
                return;
            }

            CinematicAudioCue[] defaultAudioCues = CinematicAudioCueSelection.SelectDefault(playback.AudioCues);
            audio = new CinematicAudioPreview(archive, defaultAudioCues, playback.Duration) { Volume = 0 };
            Stopwatch loadingTime = Stopwatch.StartNew();
            while (audio.IsLoading && loadingTime.Elapsed < TimeSpan.FromMinutes(2))
            {
                audio.Update(startTime, false);
                Thread.Sleep(10);
            }
            if (audio.IsLoading)
                throw new TimeoutException("Timed out while preparing cinematic source audio.");
            audio.Update(startTime, false);

            using var renderer = new SceneRenderer
            {
                GameView = true,
                ShowGrid = false,
                ShowGizmo = false,
                ActiveWorld = world,
                Document = document,
                Resolver = resolver,
                VideoArchive = archive
            };
            renderer.FrameAll();
            playback.Activate();
            playback.Seek(startTime);
            ApplyCinematicState(renderer, playback);
            audio.Update(playback.Time, false);
            for (int warmupFrame = 0; warmupFrame < 30; warmupFrame++)
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                renderer.Render(renderWidth, renderHeight, document, world, null, resolver);
                Raylib.EndDrawing();
            }
            playback.Play();
            Raylib.SetTargetFPS(60);

            var frameMilliseconds = new List<double>(checked((int)(duration * 60) + 8));
            int longFrames = 0;
            int severeFrames = 0;
            int stalledTimelineFrames = 0;
            int largeTimelineSteps = 0;
            double maxClockLeadMilliseconds = 0;
            double maxClockLagMilliseconds = 0;
            double maxUpdateMilliseconds = 0;
            double maxAudioUpdateMilliseconds = 0;
            double maxRenderMilliseconds = 0;
            double maxBeginDrawingMilliseconds = 0;
            double maxEndDrawingMilliseconds = 0;
            double maxFrameMilliseconds = 0;
            double maxFrameTime = startTime;
            float maxFrameDelta = 0;
            double totalUpdateMilliseconds = 0;
            double totalRenderMilliseconds = 0;
            double totalAudioUpdateMilliseconds = 0;
            var severeFrameDetails = new List<string>();
            double slowestRenderTime = startTime;
            double slowestInternalRenderMilliseconds = 0;
            double slowestWorldUpdateMilliseconds = 0;
            int slowestWorldActorUpdates = 0;
            int slowestGeometryUploads = 0;
            int slowestVisibleObjects = 0;
            Stopwatch auditDuration = Stopwatch.StartNew();
            while (auditDuration.Elapsed.TotalSeconds < duration)
            {
                long frameStart = Stopwatch.GetTimestamp();
                double audioTime = 0;
                bool hasAudioClock = audio.TryGetTimelineClock(out audioTime);
                float frameDelta = CinematicAudioPlaybackClock.Delta(playback.Time,
                    audioTime, hasAudioClock, Raylib.GetFrameTime());
                if (frameDelta < 1f / 120) stalledTimelineFrames++;
                if (frameDelta > 1f / 30) largeTimelineSteps++;
                long updateStart = Stopwatch.GetTimestamp();
                playback.Advance(frameDelta);
                ApplyCinematicState(renderer, playback);
                long audioUpdateStart = Stopwatch.GetTimestamp();
                audio.Update(playback.Time, playback.Playing);
                double audioUpdateMilliseconds = Stopwatch.GetElapsedTime(audioUpdateStart).TotalMilliseconds;
                renderer.StatCinematicAudioMilliseconds = (float)audioUpdateMilliseconds;
                maxAudioUpdateMilliseconds = Math.Max(maxAudioUpdateMilliseconds, audioUpdateMilliseconds);
                double updateMilliseconds = Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds;
                maxUpdateMilliseconds = Math.Max(maxUpdateMilliseconds, updateMilliseconds);
                totalUpdateMilliseconds += updateMilliseconds;
                totalAudioUpdateMilliseconds += audioUpdateMilliseconds;

                long beginDrawingStartedAt = Stopwatch.GetTimestamp();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                double beginDrawingMilliseconds = Stopwatch.GetElapsedTime(beginDrawingStartedAt).TotalMilliseconds;
                maxBeginDrawingMilliseconds = Math.Max(maxBeginDrawingMilliseconds, beginDrawingMilliseconds);
                long renderStart = Stopwatch.GetTimestamp();
                renderer.Render(renderWidth, renderHeight, document, world, null, resolver);
                double renderMilliseconds = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
                totalRenderMilliseconds += renderMilliseconds;
                if (renderMilliseconds > maxRenderMilliseconds)
                {
                    maxRenderMilliseconds = renderMilliseconds;
                    slowestRenderTime = playback.Time;
                    slowestInternalRenderMilliseconds = renderer.StatRenderMilliseconds;
                    slowestWorldUpdateMilliseconds = renderer.StatWorldActorUpdateMilliseconds;
                    slowestWorldActorUpdates = renderer.StatWorldActorUpdates;
                    slowestGeometryUploads = renderer.StatGeometryUploadsThisFrame;
                    slowestVisibleObjects = renderer.StatVisibleObjects;
                }
                long endDrawingStartedAt = Stopwatch.GetTimestamp();
                Raylib.EndDrawing();
                double endDrawingMilliseconds = Stopwatch.GetElapsedTime(endDrawingStartedAt).TotalMilliseconds;
                maxEndDrawingMilliseconds = Math.Max(maxEndDrawingMilliseconds, endDrawingMilliseconds);

                double fullFrameMilliseconds = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                frameMilliseconds.Add(fullFrameMilliseconds);
                if (fullFrameMilliseconds > 16.67) longFrames++;
                if (fullFrameMilliseconds > maxFrameMilliseconds)
                {
                    maxFrameMilliseconds = fullFrameMilliseconds;
                    maxFrameTime = playback.Time;
                    maxFrameDelta = frameDelta;
                }
                if (fullFrameMilliseconds > 33.33)
                {
                    severeFrames++;
                    severeFrameDetails.Add($"{playback.Time:F2}s/frame={fullFrameMilliseconds:F2}ms"
                        + $" update={updateMilliseconds:F2}ms audio={audioUpdateMilliseconds:F2}ms"
                        + $" begin={beginDrawingMilliseconds:F2}ms render={renderMilliseconds:F2}ms"
                        + $" end={endDrawingMilliseconds:F2}ms"
                        + $" unaccounted={Math.Max(0, fullFrameMilliseconds - updateMilliseconds
                            - beginDrawingMilliseconds - renderMilliseconds - endDrawingMilliseconds):F2}ms"
                        + $" dt={frameDelta * 1000:F1}ms");
                }
                if (audio.TryGetTimelineClock(out double renderedAudioTime))
                {
                    double clockOffset = (renderedAudioTime - playback.Time) * 1000;
                    maxClockLeadMilliseconds = Math.Max(maxClockLeadMilliseconds, clockOffset);
                    maxClockLagMilliseconds = Math.Max(maxClockLagMilliseconds, -clockOffset);
                }
            }
            auditDuration.Stop();

            frameMilliseconds.Sort();
            double averageMilliseconds = frameMilliseconds.Average();
            double p95Milliseconds = frameMilliseconds[(int)(frameMilliseconds.Count * .95)];
            double maxMilliseconds = frameMilliseconds[^1];
            Console.WriteLine($"CINEMATIC AV AUDIT: {entry.Id}/{clipKey:X8}; "
                + $"range={startTime:F2}-{startTime + duration:F2}s; size={renderWidth}x{renderHeight}; "
                + $"elapsed={auditDuration.Elapsed.TotalSeconds:F2}s; "
                + $"frames={frameMilliseconds.Count}; avg={averageMilliseconds:F2}ms; "
                + $"p95={p95Milliseconds:F2}ms; max={maxMilliseconds:F2}ms; "
                + $"over16.67ms={longFrames}; over33.33ms={severeFrames}; "
                + $"timelineStalls={stalledTimelineFrames}; timelineStepsOver33ms={largeTimelineSteps}; "
                + $"maxUpdate={maxUpdateMilliseconds:F2}ms; maxRender={maxRenderMilliseconds:F2}ms; "
                + $"maxAudioUpdate={maxAudioUpdateMilliseconds:F2}ms; "
                + $"maxBeginDrawing={maxBeginDrawingMilliseconds:F2}ms; "
                + $"maxEndDrawing={maxEndDrawingMilliseconds:F2}ms; "
                + $"avgUpdate={totalUpdateMilliseconds / frameMilliseconds.Count:F2}ms; "
                + $"avgAudioUpdate={totalAudioUpdateMilliseconds / frameMilliseconds.Count:F2}ms; "
                + $"avgRender={totalRenderMilliseconds / frameMilliseconds.Count:F2}ms; "
                + $"slowFrameAt={slowestRenderTime:F2}s internalRender={slowestInternalRenderMilliseconds:F2}ms; "
                + $"worldAI={slowestWorldActorUpdates}/{slowestWorldUpdateMilliseconds:F2}ms; "
                + $"uploads={slowestGeometryUploads}; visible={slowestVisibleObjects}; "
                + $"audioLead={maxClockLeadMilliseconds:F0}ms; audioLag={maxClockLagMilliseconds:F0}ms; "
                + $"slowestFrameAt={maxFrameTime:F2}s frame={maxFrameMilliseconds:F2}ms"
                + $" dt={maxFrameDelta * 1000:F1}ms; "
                + $"queueDrains={audio.DrainedQueueObservations}; queueResyncs={audio.QueueResyncCount}; "
                + $"resolvedAudioCues={defaultAudioCues.Length}; muted=true");
            foreach (string detail in severeFrameDetails)
                Console.WriteLine($"CINEMATIC AV AUDIT SLOW FRAME: {detail}");
            playback.Deactivate();
        }
        finally
        {
            audio?.Dispose();
            if (audioDeviceReady) Raylib.CloseAudioDevice();
            Raylib.CloseWindow();
        }
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
        Matrix4x4.Decompose(pose, out _, out var rotation, out var position);
        renderer.Camera.Position = position;
        renderer.Camera.Target = position + Vector3.Transform(-Vector3.UnitZ, rotation);
        renderer.Camera.Up = Vector3.Transform(Vector3.UnitY, rotation);
    }
}
