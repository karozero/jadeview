using System.Numerics;
using JadeView;
using Raylib_cs;

internal static class CinematicShutterRenderChecks
{
    internal static void Verify(string output, string? archivePath = null, string? container = null, uint clipKey = 0)
    {
        var sourceStates = archivePath == null ? [] : SourceStates(archivePath, container!, clipKey);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(320, 180, "Shutter render verification");
        try
        {
            using var renderer = new SceneRenderer { ShowGrid = false, ShowGizmo = false };
            Check(null, "open", false, false);
            Check(new(.5f, new(.5f)), "half", true, false);
            Check(new(1, new(.5f)), "closed", true, true);
            foreach (var (name, state) in sourceStates)
                Check(state, name, state is { Closure: > 0 }, state is { Closure: >= .99f });
            renderer.CinematicScreenFade = Vector4.One;
            Check(null, "flash", false, false, white: true);
            float previousFov = renderer.Camera.FovY;
            renderer.CinematicHorizontalFov = .5f;
            foreach (var size in new[] { (Width: 320, Height: 180), (Width: 160, Height: 180) })
            {
                renderer.Render(size.Width, size.Height);
                float horizontal = 2 * MathF.Atan(MathF.Tan(renderer.Camera.FovY * MathF.PI / 360)
                    * size.Width / size.Height);
                if (Math.Abs(horizontal - .5f) > .0001f)
                    throw new InvalidDataException("Viewport resize changed the source horizontal lens.");
            }
            renderer.CinematicHorizontalFov = null;
            renderer.Render(320, 180);
            if (renderer.Camera.FovY != previousFov)
                throw new InvalidDataException("Leaving cinematic projection did not restore the editor lens.");

            void Check(CinematicShutterState? state, string name, bool cornerBlack, bool centerBlack, bool white = false)
            {
                renderer.CinematicShutter = state;
                renderer.Render(320, 180);
                var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
                try
                {
                    var corner = Raylib.GetImageColor(image, 1, 1);
                    var center = Raylib.GetImageColor(image, 160, 90);
                    if (white && (corner.R != 255 || corner.G != 255 || corner.B != 255
                        || center.R != 255 || center.G != 255 || center.B != 255))
                        throw new InvalidDataException("Screen flash did not cover the viewport.");
                    if ((corner.R == 0 && corner.G == 0 && corner.B == 0) != cornerBlack
                        || (center.R == 0 && center.G == 0 && center.B == 0) != centerBlack)
                        throw new InvalidDataException($"Shutter rasterization failed for {name}: corner {corner}, center {center}.");
                    if (!Raylib.ExportImage(image, Path.Combine(output, $"shutter-{name}.png")))
                        throw new IOException("Could not save shutter render fixture.");
                }
                finally { Raylib.UnloadImage(image); }
            }
            Console.WriteLine("PASS: hidden-window GPU shutter rasterization: open frame, opaque outer blades and closed aperture");
        }
        finally { Raylib.CloseWindow(); }
    }

    private static (string Name, CinematicShutterState? State)[] SourceStates(string archivePath,
        string container, uint clipKey)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var clip = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == clip.CinematicWorldKey));
        var timeline = new CinematicShutterTimeline(clip, world, document);
        if (timeline.TriggerCount == 0) throw new InvalidDataException("No source shutter trigger discovered.");
        float start = timeline.TriggerTimes[0];
        float duration = document.CinematicShutterControllers.Values
            .Max(profile => profile.CloseTime + profile.HoldTime + profile.OpenTime);
        var active = Enumerable.Range(1, 239).Select(frame => start + duration * frame / 240)
            .Where(time => timeline.Evaluate(time) is { Closure: > 0 }).ToArray();
        if (active.Length == 0) throw new InvalidDataException("Source shutter has no visible envelope.");
        float half = active.MinBy(time => Math.Abs(timeline.Evaluate(time)!.Value.Closure - .5f));
        float peak = active.MaxBy(time => timeline.Evaluate(time)!.Value.Closure);
        var states = new List<(string, CinematicShutterState?)>();
        foreach (float time in new[] { start - .01f, half, peak, start + duration, half })
        {
            var state = timeline.Evaluate(time);
            states.Add(($"source-{states.Count}", state));
            Console.WriteLine($"SOURCE SHUTTER {time:F6}: {state?.Closure}");
        }
        return states.ToArray();
    }
}
