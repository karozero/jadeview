using System.Diagnostics;
using System.Runtime.InteropServices;
using JadeView;
using Raylib_cs;

internal static class AssetsRenderPerformanceAudit
{
    [DllImport("opengl32.dll", EntryPoint = "glFinish")]
    private static extern void FinishGpu();

    internal static void Write(string archivePath, string container, string worldName, string focusName, string output,
        int width = 1600, int height = 900, string camera = "bounds")
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var world = document.Worlds.Single(item => item.Name == worldName);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        var focus = world.Objects.Single(item => item.Name == focusName);
        Console.WriteLine($"ASSETS INVENTORY: world={world.Name}/{world.Key:X8}; objects={world.Objects.Count}; actors={world.Actors.Count}; focus={focus.GaoKey:X8}; geometry={focus.RenderGeometryKey:X8}");
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(width, height, "Assets render performance audit");
        try
        {
            using var renderer = new SceneRenderer { ActiveWorld = world, Document = document, Resolver = resolver,
                VideoArchive = archive, CapturePerformanceDiagnostics = true };
            renderer.FrameObject(focus);
            switch (camera)
            {
                case "bounds": break;
                case "all": renderer.FrameAll(); break;
                case "close":
                    renderer.Camera.Position = renderer.Camera.Target + (renderer.Camera.Position - renderer.Camera.Target) * .08f;
                    break;
                case "origin":
                    renderer.Camera.Target = focus.RenderMatrix.Translation;
                    renderer.Camera.Position = renderer.Camera.Target + new System.Numerics.Vector3(25, 15, 25);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(camera));
            }
            Console.WriteLine($"ASSETS CAMERA: preset={camera}; resolution={width}x{height}; position={renderer.Camera.Position}; target={renderer.Camera.Target}");
            for (int frame = 0; frame < 20; frame++) RenderFrame();
            var total = new double[60];
            var render = new double[total.Length];
            var actor = new double[total.Length];
            var skin = new double[total.Length];
            var prep = new double[total.Length];
            var draw = new double[total.Length];
            for (int frame = 0; frame < total.Length; frame++)
            {
                long started = Stopwatch.GetTimestamp();
                RenderFrame();
                total[frame] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                render[frame] = renderer.StatRenderMilliseconds;
                actor[frame] = renderer.StatWorldActorUpdateMilliseconds;
                skin[frame] = renderer.StatSkinningMilliseconds;
                prep[frame] = renderer.StatGeometryPreparationMilliseconds;
                draw[frame] = renderer.StatDrawPassMilliseconds;
            }
            Console.WriteLine($"ASSETS PERFORMANCE: frames={total.Length}; total={Summary(total)}; render={Summary(render)}; actor={Summary(actor)}; skin={Summary(skin)}; prep={Summary(prep)}; draw={Summary(draw)}; visible={renderer.StatVisibleObjects}; tris={renderer.StatDrawnTriangles}; skins={renderer.StatSkinUpdates}; lightPlans={renderer.StatDiffuseLightingPreparations}; lights={renderer.StatDiffuseLightingUpdates}; uploads={renderer.StatGeometryUploadsThisFrame}");
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            Raylib.ImageFlipVertical(ref image);
            Raylib.ExportImage(image, Path.Combine(output, $"assets-performance-{width}x{height}-{camera}.png"));
            Raylib.UnloadImage(image);
            void RenderFrame()
            {
                Raylib.BeginDrawing();
                renderer.Render(width, height, document, world, focus, resolver);
                Raylib.EndDrawing();
                FinishGpu();
            }
        }
        finally { Raylib.CloseWindow(); }
    }

    private static string Summary(double[] values)
    {
        Array.Sort(values);
        return $"avg {values.Average():F2}ms/p95 {values[(int)(values.Length * .95)]:F2}ms/max {values[^1]:F2}ms";
    }
}
