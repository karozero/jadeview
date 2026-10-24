using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using JadeView;
using Raylib_cs;

internal static class AssetsUiInteractionScenarios
{
    internal static void Write(EditorShell shell, WorldData world, SceneObject focus, Func<double> drawFrame)
    {
        var renderer = shell.Renderer;
        Vector3 wideTarget = renderer.Camera.Target;
        Vector3 wideOffset = renderer.Camera.Position - wideTarget;
        renderer.ShowGameHidden = true;
        Console.WriteLine($"ASSETS INTERACTION DISPLAY: hidden={renderer.ShowGameHidden}; colliders={renderer.ShowColliders}; selectionWire={renderer.ShowSelectionWireframe}; sceneWire={renderer.Wireframe}; grid={renderer.ShowGrid}; gizmo={renderer.ShowGizmo}; frustum={renderer.FrustumCullingEnabled}; focus={focus.GaoKey:X8}; sourcePanel=Properties");
        int previousUploads = renderer.StatGeometryUploadsThisFrame;
        float previousMeshTime = renderer.StatGeometryCacheMissMilliseconds;
        long started = Stopwatch.GetTimestamp();
        var coldHit = renderer.PickObject(new(800, 450), Vector2.Zero, new(1600, 900), world, shell.Resolver);
        double coldPick = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Console.WriteLine($"ASSETS COLD PICK: {coldPick:F2}ms; geometryUploads={renderer.StatGeometryUploadsThisFrame - previousUploads}; meshPreparation={renderer.StatGeometryCacheMissMilliseconds - previousMeshTime:F2}ms; hit={coldHit?.GaoKey:X8}/{coldHit?.Name}");
        shell.SelectedObject = focus;
        Measure("initial-view-after-pick", 0, 1, _ => { });
        Measure("wide-camera-orbit", 120, 240, frame => Orbit(wideTarget, wideOffset, frame));
        var geometry = renderer.GetOrUploadGeometry(focus.RenderGeometryKey)!;
        var (minimum, maximum) = SceneRenderer.TransformBoundingBox(geometry.LocalBounds, focus.RenderMatrix);
        Vector3 closeTarget = new((minimum.X + maximum.X) * .5f, minimum.Y + 1, (minimum.Z + maximum.Z) * .5f);
        Measure("close-camera-orbit", 120, 240, frame => Orbit(closeTarget, new(20, 8, 20), frame));
        renderer.Camera.Target = wideTarget;
        renderer.Camera.Position = wideTarget + wideOffset;
        var selection = (Action<SceneObject?>?)typeof(SceneRenderer)
            .GetField("OnSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
        Measure("repeated-selection-notification", 30, 90, _ => selection?.Invoke(focus));
        Measure("repeated-ray-picking", 5, 30, _ =>
        {
            renderer.PickObject(new(800, 450), Vector2.Zero, new(1600, 900), world, shell.Resolver);
            shell.SelectedObject = focus;
        });
        renderer.Wireframe = true;
        Measure("scene-wireframe", 30, 90, _ => { });
        renderer.Wireframe = false;

        void Orbit(Vector3 target, Vector3 offset, int frame)
        {
            float angle = frame * MathF.Tau / 120;
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
            renderer.Camera.Target = target;
            renderer.Camera.Position = target + Vector3.Transform(offset, rotation);
        }

        void Measure(string label, int warmup, int count, Action<int> beforeFrame)
        {
            int peakWarmUploads = 0;
            double maximumWarmup = 0;
            string warmupWorstDetails = "";
            for (int frame = 0; frame < warmup; frame++)
            {
                beforeFrame(frame);
                long frameStarted = Stopwatch.GetTimestamp();
                long frameBytesBefore = GC.GetAllocatedBytesForCurrentThread();
                int gen2Before = GC.CollectionCount(2);
                drawFrame();
                double elapsed = Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
                if (elapsed > maximumWarmup)
                {
                    maximumWarmup = elapsed;
                    warmupWorstDetails = $"frame={frame}; render={renderer.StatRenderMilliseconds:F2}; prep={renderer.StatGeometryPreparationMilliseconds:F2}; draw={renderer.StatDrawPassMilliseconds:F2}; "
                        + $"mesh={renderer.StatGeometryCacheMissMilliseconds:F2}/{renderer.StatGeometryUploadsThisFrame}; materials={renderer.StatMaterialPreparationMilliseconds:F2}/{renderer.StatMaterialSetsThisFrame}; "
                        + $"textureDecode={renderer.StatTextureDecodeMilliseconds:F2}; textureUpload={renderer.StatTextureUploadMilliseconds:F2}/{renderer.StatTextureUploadsThisFrame}; "
                        + $"plan={renderer.StatDiffuseLightingPlanMilliseconds:F2}/{renderer.StatDiffuseLightingPreparations}; lightEval={renderer.StatDiffuseLightingEvaluationMilliseconds:F2}; "
                        + $"skin={renderer.StatSkinningMilliseconds:F2}; gen2={GC.CollectionCount(2) - gen2Before}; allocated={GC.GetAllocatedBytesForCurrentThread() - frameBytesBefore}B";
                }
                peakWarmUploads = Math.Max(peakWarmUploads, renderer.StatGeometryUploadsThisFrame);
            }
            double[] times = new double[count];
            double[] renderTimes = new double[count];
            double[] skin = new double[count];
            double[] plans = new double[count];
            double[] selectionTimes = new double[count];
            int minimumVisible = int.MaxValue, maximumVisible = 0;
            int minimumTriangles = int.MaxValue, maximumTriangles = 0;
            int peakUploads = 0, slowestFrame = -1;
            string slowestDetails = "";
            double maximumFrame = -1;
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < count; frame++)
            {
                long frameStarted = Stopwatch.GetTimestamp();
                beforeFrame(frame + warmup);
                selectionTimes[frame] = Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
                drawFrame();
                times[frame] = Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
                renderTimes[frame] = renderer.StatRenderMilliseconds;
                skin[frame] = renderer.StatSkinningMilliseconds;
                plans[frame] = renderer.StatDiffuseLightingPlanMilliseconds;
                minimumVisible = Math.Min(minimumVisible, renderer.StatVisibleObjects);
                maximumVisible = Math.Max(maximumVisible, renderer.StatVisibleObjects);
                minimumTriangles = Math.Min(minimumTriangles, renderer.StatDrawnTriangles);
                maximumTriangles = Math.Max(maximumTriangles, renderer.StatDrawnTriangles);
                peakUploads = Math.Max(peakUploads, renderer.StatGeometryUploadsThisFrame);
                if (times[frame] <= maximumFrame) continue;
                maximumFrame = times[frame];
                slowestFrame = frame;
                slowestDetails = $"visible={renderer.StatVisibleObjects}; triangles={renderer.StatDrawnTriangles}; uploads={renderer.StatGeometryUploadsThisFrame}; "
                    + $"prep={renderer.StatGeometryPreparationMilliseconds:F2}; draw={renderer.StatDrawPassMilliseconds:F2}; plan={renderer.StatDiffuseLightingPlanMilliseconds:F2}; "
                    + $"lightEval={renderer.StatDiffuseLightingEvaluationMilliseconds:F2}; skin={renderer.StatSkinningMilliseconds:F2}; camera={renderer.Camera.Position}/{renderer.Camera.Target}";
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            Console.WriteLine($"ASSETS INTERACTION: {label}; frames={count}; total={Summary(times)}; render={Summary(renderTimes)}; "
                + $"inputCallback={Summary(selectionTimes)}; skin={Summary(skin)}; lightPlan={Summary(plans)}; "
                + $"visible={minimumVisible}..{maximumVisible}; triangles={minimumTriangles}..{maximumTriangles}; "
                + $"measuredPeakUploads={peakUploads}; warmupMax={maximumWarmup:F2}ms/warmupPeakUploads={peakWarmUploads}; allocation={bytes / count}B/frame; "
                + $"slowestFrame={slowestFrame}[{slowestDetails}]; warmupWorst=[{warmupWorstDetails}]");
        }
    }

    private static string Summary(double[] values)
    {
        Array.Sort(values);
        return $"avg {values.Average():F2}ms/p95 {values[Math.Min(values.Length - 1, (int)(values.Length * .95))]:F2}ms/max {values[^1]:F2}ms";
    }
}
