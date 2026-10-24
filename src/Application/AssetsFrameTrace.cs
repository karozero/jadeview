using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raylib_cs;

namespace JadeView;

/// <summary>Opt-in, bounded wall-time capture; never synchronizes the GPU.</summary>
internal sealed class AssetsFrameTrace
{
    private readonly Queue<string> recent = new();
    private readonly Queue<string> slow = new();
    private readonly string header;
    private long sequence;
    internal static AssetsFrameTrace? Create() =>
        Environment.GetEnvironmentVariable("JADEVIEW_ASSETS_FRAME_TRACE") == "1" ? new() : null;

    private AssetsFrameTrace()
    {
        string assembly = typeof(App).Assembly.Location;
        header = $"exe={Environment.ProcessPath}; assembly={assembly}; modifiedUtc={File.GetLastWriteTimeUtc(assembly):O}; pid={Environment.ProcessId}; threshold=100ms; recentCapacity=300; slowCapacity=32; GPU synchronization=none\n"
            + "frame\ttotalMs\tshellMs\tuiOutsideRendererMs\timguiSubmitMs\tpresentMs\trenderMs\tprepMs\tdrawMs\tfinalizeMs\tuploads\tmeshMs\tvisible\ttriangles\tskinMs\tallocatedBytes\tgen2\townerHotspots\tcontext";
    }

    internal void Record(EditorShell shell, long start, double shellTime, double submission, double present,
        long allocatedBefore, int gen2Before)
    {
        var renderer = shell.Renderer;
        double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        string hotspots = renderer.PerformanceOwnerWork.FormatHotspots().Replace('\t', ' ');
        string row = FormattableString.Invariant($"{++sequence}\t{total:F2}\t{shellTime:F2}\t{Math.Max(0, shellTime - renderer.StatRenderMilliseconds):F2}\t{submission:F2}\t{present:F2}\t{renderer.StatRenderMilliseconds:F2}\t{renderer.StatGeometryPreparationMilliseconds:F2}\t{renderer.StatDrawPassMilliseconds:F2}\t{renderer.StatRenderFinalizeMilliseconds:F2}\t{renderer.StatGeometryUploadsThisFrame}\t{renderer.StatGeometryCacheMissMilliseconds:F2}\t{renderer.StatVisibleObjects}\t{renderer.StatDrawnTriangles}\t{renderer.StatSkinningMilliseconds:F2}\t{allocated}\t{GC.CollectionCount(2) - gen2Before}\t{hotspots}\tworld={shell.ActiveWorld?.Key:X8}/{shell.ActiveWorld?.Name};focus={shell.SelectedObject?.GaoKey:X8}/{shell.SelectedObject?.Name};camera={renderer.Camera.Position}/{renderer.Camera.Target};window={Raylib.GetScreenWidth()}x{Raylib.GetScreenHeight()};viewport={renderer.Target.Texture.Width}x{renderer.Target.Texture.Height};cinematicMaterials={renderer.CinematicMaterials != null};hidden={renderer.ShowGameHidden};colliders={renderer.ShowColliders};selectionWire={renderer.ShowSelectionWireframe};wire={renderer.Wireframe};grid={renderer.ShowGrid};frustum={renderer.FrustumCullingEnabled}");
        Append(recent, row, 300);
        if (total >= 100) Append(slow, row, 32);
    }

    private static void Append(Queue<string> rows, string row, int capacity)
    {
        if (rows.Count == capacity) rows.Dequeue();
        rows.Enqueue(row);
    }

    internal void Save()
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(directory);
            string text = header + "\nSLOW FRAMES (may also appear in recent)\n" + string.Join('\n', slow)
                + "\nRECENT FRAMES\n" + string.Join('\n', recent);
            File.WriteAllText(Path.Combine(directory, $"assets-frame-trace-{Environment.ProcessId}.tsv"), text, Encoding.UTF8);
        }
        catch (IOException exception) { Console.Error.WriteLine($"Frame trace save failed: {exception.Message}"); }
        catch (UnauthorizedAccessException exception) { Console.Error.WriteLine($"Frame trace save failed: {exception.Message}"); }
    }
}
