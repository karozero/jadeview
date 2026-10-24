using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;
using JadeView;
using Raylib_cs;
using rlImGui_cs;

internal static class AssetsUiPerformanceAudit
{
    [DllImport("opengl32.dll", EntryPoint = "glFinish")]
    private static extern void FinishGpu();

    internal static unsafe void Write(string archivePath, string container, string worldName,
        string focusName, string output, string requestedProfile = "all")
    {
        if (requestedProfile is not ("all" or "default" or "show-hidden" or "show-hidden-close" or "interactions"))
            throw new ArgumentOutOfRangeException(nameof(requestedProfile));
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive, entry);
        var world = document.Worlds.Single(item => item.Name == worldName);
        var focus = world.Objects.Single(item => item.Name == focusName);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow | ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(3440, 1440, "Assets full UI performance audit");
        rlImGui.LoadFontAwesome = false;
        rlImGui.LoadDefaultFont = false;
        rlImGui.SetupUserFonts = io => EditorTheme.LoadFonts(io, 1);
        rlImGui.Setup(true, false);
        ImGui.GetIO().NativePtr->IniFilename = null;
        EditorTheme.Apply(1);
        var frameTrace = AssetsFrameTrace.Create();
        try
        {
            using var shell = new EditorShell(1);
            SetProperty(shell, nameof(EditorShell.Archive), archive);
            SetProperty(shell, nameof(EditorShell.Document), document);
            SetProperty(shell, nameof(EditorShell.ActiveWorld), world);
            SetProperty(shell, nameof(EditorShell.Resolver), resolver);
            shell.Renderer.Document = document;
            shell.Renderer.ActiveWorld = world;
            shell.Renderer.Resolver = resolver;
            shell.Renderer.VideoArchive = archive;
            shell.Renderer.CapturePerformanceDiagnostics = true;
            shell.Renderer.CacheShaderUniformValues = Environment.GetEnvironmentVariable("JADEVIEW_AUDIT_UNCACHED_UNIFORMS") != "1";
            shell.SelectedObject = focus;
            shell.Renderer.FrameObject(focus);
            object scenes = typeof(EditorShell).GetMethod("BuildArchiveSceneIndex", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [archive, CancellationToken.None])!;
            typeof(EditorShell).GetField("_archiveScenes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, scenes);
            Invoke(shell, "BuildCatalogFolders");
            Invoke(shell, "FilterCatalog");
            Console.WriteLine($"ASSETS UI INVENTORY: world={world.Key:X8}; objects={world.Objects.Count}; archiveFiles={archive.Entries.Count}; focus={focus.GaoKey:X8}; resolution=3440x1440; scale=1; panels=Outliner/Inspector(Properties)/ContentBrowser(AllAssets); mode=Assets");
            double gpuFinishTail = 0;
            float shaderInitializationThisFrame = 0;
            if (requestedProfile == "interactions")
            {
                AssetsUiInteractionScenarios.Write(shell, world, focus, DrawFrame);
                return;
            }
            MeasureWorkspace("default");
            shell.Renderer.ShowGameHidden = true;
            MeasureWorkspace("show-hidden");
            var geometry = shell.Renderer.GetOrUploadGeometry(focus.RenderGeometryKey)!;
            var (minimum, maximum) = SceneRenderer.TransformBoundingBox(geometry.LocalBounds, focus.RenderMatrix);
            shell.Renderer.Camera.Target = new((minimum.X + maximum.X) * .5f, minimum.Y + 1, (minimum.Z + maximum.Z) * .5f);
            shell.Renderer.Camera.Position = shell.Renderer.Camera.Target + new System.Numerics.Vector3(15, 8, 15);
            MeasureWorkspace("show-hidden-close");
            shell.Renderer.ShowGameHidden = false;
            void MeasureWorkspace(string profile)
            {
                if (requestedProfile != "all" && requestedProfile != profile) return;
                double warmupMaximum = 0;
                int warmupUploads = 0;
                int uploadsAtMaximum = 0;
                int maximumFrame = -1;
                float maximumRender = 0;
                float maximumGeometryMissTime = 0;
                string resourceBreakdownAtMaximum = "";
                string ownerHotspotsAtMaximum = "none";
                for (int frame = 0; frame < (profile.StartsWith("show-hidden", StringComparison.Ordinal) ? 300 : 30); frame++)
                {
                    long started = Stopwatch.GetTimestamp();
                    DrawFrame();
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (elapsed > warmupMaximum)
                    {
                        warmupMaximum = elapsed;
                        uploadsAtMaximum = shell.Renderer.StatGeometryUploadsThisFrame;
                        maximumRender = shell.Renderer.StatRenderMilliseconds;
                        maximumGeometryMissTime = shell.Renderer.StatGeometryCacheMissMilliseconds;
                        maximumFrame = frame;
                        resourceBreakdownAtMaximum = ResourceBreakdown(shell.Renderer, shaderInitializationThisFrame);
                        ownerHotspotsAtMaximum = shell.Renderer.PerformanceOwnerWork.FormatHotspots();
                    }
                    warmupUploads = Math.Max(warmupUploads, shell.Renderer.StatGeometryUploadsThisFrame);
                }
                Console.WriteLine($"ASSETS UI WARMUP: profile={profile}; max={warmupMaximum:F2}ms; frame={maximumFrame}; "
                    + $"renderAtMaximum={maximumRender:F2}ms; geometryCacheMissAtMaximum={maximumGeometryMissTime:F2}ms; uploadsAtMaximum={uploadsAtMaximum}; "
                    + $"peakGeometryUploads={warmupUploads}; resources=[{resourceBreakdownAtMaximum}]; ownerHotspots=[{ownerHotspotsAtMaximum}]; "
                    + $"camera={shell.Renderer.Camera.Position}/{shell.Renderer.Camera.Target}");
                double[] total = new double[90];
                double[] shellTimes = new double[total.Length];
                double[] nonRender = new double[total.Length];
                double[] render = new double[total.Length];
                double[] prep = new double[total.Length];
                double[] drawPass = new double[total.Length];
                double[] skin = new double[total.Length];
                double[] actors = new double[total.Length];
                double[] gpuTail = new double[total.Length];
                double[] procedural = new double[total.Length];
                double[] target = new double[total.Length];
                double[] lighting = new double[total.Length];
                double[] clipping = new double[total.Length];
                double[] overlay = new double[total.Length];
                double[] finalize = new double[total.Length];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < total.Length; frame++)
                {
                    long started = Stopwatch.GetTimestamp();
                    shellTimes[frame] = DrawFrame();
                    total[frame] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    render[frame] = shell.Renderer.StatRenderMilliseconds;
                    prep[frame] = shell.Renderer.StatGeometryPreparationMilliseconds;
                    drawPass[frame] = shell.Renderer.StatDrawPassMilliseconds;
                    skin[frame] = shell.Renderer.StatSkinningMilliseconds;
                    actors[frame] = shell.Renderer.StatWorldActorUpdateMilliseconds;
                    gpuTail[frame] = gpuFinishTail;
                    procedural[frame] = shell.Renderer.StatProceduralTextureMilliseconds;
                    target[frame] = shell.Renderer.StatRenderTargetMilliseconds;
                    lighting[frame] = shell.Renderer.StatLightingPreparationMilliseconds;
                    clipping[frame] = shell.Renderer.StatClipPreparationMilliseconds;
                    overlay[frame] = shell.Renderer.StatOverlayMilliseconds;
                    finalize[frame] = shell.Renderer.StatRenderFinalizeMilliseconds;
                    nonRender[frame] = Math.Max(0, shellTimes[frame] - render[frame]);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Console.WriteLine($"ASSETS FULL UI PERFORMANCE: profile={profile}; hidden={shell.Renderer.ShowGameHidden}; total={Summary(total)}; shell={Summary(shellTimes)}; render={Summary(render)}; "
                    + $"shellWithoutRender={Summary(nonRender)}; prep={Summary(prep)}; draw={Summary(drawPass)}; skin={Summary(skin)}; "
                    + $"actors={Summary(actors)}; gpuFinishTail={Summary(gpuTail)}; "
                    + $"video={Summary(procedural)}; target={Summary(target)}; lightFrame={Summary(lighting)}; clip={Summary(clipping)}; "
                    + $"overlay={Summary(overlay)}; finalize={Summary(finalize)}; lightingEvaluations={shell.Renderer.StatDiffuseLightingUpdates}; uniforms={shell.Renderer.StatShaderUniformUploads}/{shell.Renderer.StatShaderUniformRequests}; cachedUniforms={shell.Renderer.CacheShaderUniformValues}; "
                    + $"allocation={allocated / total.Length}B/frame; visible={shell.Renderer.StatVisibleObjects}; tris={shell.Renderer.StatDrawnTriangles}");
                var screen = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(screen, Path.Combine(output, $"assets-full-ui-performance-{profile}.png"));
                Raylib.UnloadImage(screen);

            }
            MeasurePanel("DrawOutliner");
            MeasurePanel("DrawAssetInspector");
            MeasurePanel("DrawContentBrowser");
            MeasurePanel("DrawTexturesTab");
            MeasurePanel("DrawDiagnosticsTab");
            MeasurePanel("DrawAudioTab");
            MeasurePanel("DrawTextTab");

            double DrawFrame()
            {
                long traceStart = Stopwatch.GetTimestamp();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                int gen2Before = GC.CollectionCount(2);
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Raylib_cs.Color(14, 18, 16, 255));
                rlImGui.Begin();
                long started = Stopwatch.GetTimestamp();
                float previousInitializationTime = shell.Renderer.TotalShaderInitializationMilliseconds;
                shell.Draw();
                shaderInitializationThisFrame = shell.Renderer.TotalShaderInitializationMilliseconds - previousInitializationTime;
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                long submitStart = Stopwatch.GetTimestamp();
                rlImGui.End();
                double submitTime = Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds;
                long presentStart = Stopwatch.GetTimestamp();
                Raylib.EndDrawing();
                double presentTime = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
                frameTrace?.Record(shell, traceStart, elapsed, submitTime, presentTime, allocatedBefore, gen2Before);
                long gpuWaitStarted = Stopwatch.GetTimestamp();
                FinishGpu();
                gpuFinishTail = Stopwatch.GetElapsedTime(gpuWaitStarted).TotalMilliseconds;
                return elapsed;
            }

            void MeasurePanel(string method)
            {
                var draw = (Action)typeof(EditorShell).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate(typeof(Action), shell);
                double[] times = new double[90];
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < times.Length; frame++)
                {
                    Raylib.BeginDrawing();
                    rlImGui.Begin();
                    ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero);
                    ImGui.SetNextWindowSize(new(900, 900));
                    ImGui.Begin(method);
                    long started = Stopwatch.GetTimestamp();
                    draw();
                    times[frame] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    ImGui.End();
                    rlImGui.End();
                    Raylib.EndDrawing();
                    FinishGpu();
                }
                Console.WriteLine($"ASSETS UI PANEL: {method}; {Summary(times)}; allocation={(GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / times.Length}B/frame");
            }
        }
        finally
        {
            frameTrace?.Save();
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }

    private static string ResourceBreakdown(SceneRenderer renderer, float shaderInitializationMilliseconds) =>
        $"shaderInit={shaderInitializationMilliseconds:F2}ms; "
        + $"materialInclusive={renderer.StatMaterialPreparationMilliseconds:F2}ms/sets={renderer.StatMaterialSetsThisFrame}/passes={renderer.StatMaterialPassesThisFrame}; "
        + $"sourceResolve={renderer.StatMaterialSourceResolutionMilliseconds:F2}ms; nativeMaterialAlloc={renderer.StatMaterialAllocationMilliseconds:F2}ms; "
        + $"textureDecode={renderer.StatTextureDecodeMilliseconds:F2}ms; textureUpload={renderer.StatTextureUploadMilliseconds:F2}ms/count={renderer.StatTextureUploadsThisFrame}; "
        + $"renderPrepInclusive={renderer.StatGeometryPreparationMilliseconds:F2}ms; renderDrawInclusive={renderer.StatDrawPassMilliseconds:F2}ms; "
        + $"lightPlan={renderer.StatDiffuseLightingPlanMilliseconds:F2}ms/{renderer.StatDiffuseLightingPreparations}; "
        + $"lightEval={renderer.StatDiffuseLightingEvaluationMilliseconds:F2}ms/{renderer.StatDiffuseLightingUpdates}; lightUpload={renderer.StatDiffuseLightingUploadMilliseconds:F2}ms; "
        + $"actors={renderer.StatWorldActorUpdateMilliseconds:F2}ms; lightFrame={renderer.StatLightingPreparationMilliseconds:F2}ms; target={renderer.StatRenderTargetMilliseconds:F2}ms; "
        + $"clip={renderer.StatClipPreparationMilliseconds:F2}ms; overlay={renderer.StatOverlayMilliseconds:F2}ms; finalize={renderer.StatRenderFinalizeMilliseconds:F2}ms";
    private static void SetProperty(EditorShell shell, string name, object value) =>
        typeof(EditorShell).GetProperty(name)!.SetValue(shell, value);
    private static void Invoke(EditorShell shell, string name) =>
        typeof(EditorShell).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
    private static string Summary(double[] values)
    {
        Array.Sort(values);
        return $"avg {values.Average():F2}ms/p95 {values[(int)(values.Length * .95)]:F2}ms/max {values[^1]:F2}ms";
    }
}
