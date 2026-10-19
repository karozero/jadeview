using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

// glTF 2.0 / GLB, right-handed Y-up. No temporary image files or GPU context required.
public sealed partial class SceneExporter
{
    private readonly ResourceResolver resolver;
    private readonly SceneExportOptions options;
    private readonly MemoryStream binary = new();
    private readonly List<Obj> views = [], accessors = [], nodes = [], meshes = [], materials = [], images = [], textures = [], skins = [], animations = [];
    private readonly List<int> roots = [];
    private readonly Dictionary<string, int> imageCache = [];
    private readonly HashSet<string> warnings = [];
    private int empties;
    private Matrix4x4 originOffset = Matrix4x4.Identity;
    private SceneExporter(ResourceResolver resolver, SceneExportOptions options)
    {
        ValidateMouthCycleOptions(options);
        this.resolver = resolver;
        this.options = options;
    }

    private static Obj D(params object[] pairs)
    {
        var result = new Obj();
        for (int index = 0; index < pairs.Length; index += 2)
            result[(string)pairs[index]] = pairs[index + 1];
        return result;
    }
    public static SceneExportResult Export(string path, WorldData world, ResourceResolver resolver,
        SceneExportOptions options, SceneObject? selected = null)
    {
        using var lifetime = new ExportLifetime(new SceneExporter(resolver, options));
        var exporter = lifetime.Value;
        var objects = SelectObjects(world, options, selected);
        exporter.SetOrigin(objects, selected);
        exporter.ExportObjects(objects);
        if (options.ExperimentalMouthCycle && exporter.experimentalMouthClips == 0)
            exporter.warnings.Add("No active source-qualified mouth preview was included; no experimental mouth clip was exported.");
        if (exporter.nodes.Count == 0)
            throw new InvalidOperationException("Nothing matches the export options.");
        exporter.Write(path, world.Name);
        return new(exporter.meshes.Count, exporter.empties, exporter.images.Count,
            exporter.animations.Count, exporter.warnings.ToArray());
    }

    private static List<SceneObject> SelectObjects(WorldData world, SceneExportOptions options, SceneObject? selected)
    {
        if (selected != null)
        {
            bool selectedActorRoot = selected.Actor is { } && selected == selected.Actor.Root;
            return selectedActorRoot ? selected.Actor!.Members.ToList() : [selected];
        }
        return options.Actors ? world.Objects.ToList()
            : world.Objects.Where(sceneObject => sceneObject.Actor == null && (sceneObject.Flags & 2) == 0).ToList();
    }

    private void SetOrigin(List<SceneObject> objects, SceneObject? selected)
    {
        if (options.Placement != ExportPlacement.AtOrigin) return;
        var points = objects.Where(sceneObject => sceneObject.Actor == null || sceneObject == sceneObject.Actor.Root)
            .Select(sceneObject => sceneObject.RenderMatrix.Translation).ToArray();
        var center = points.Length > 0
            ? (points.Aggregate(Vector3.Min) + points.Aggregate(Vector3.Max)) * .5f : Vector3.Zero;
        originOffset = Matrix4x4.CreateTranslation(-(selected?.RenderMatrix.Translation ?? center));
    }

    private void ExportObjects(List<SceneObject> objects)
    {
        var seenActors = new HashSet<SceneActor>();
        foreach (var sceneObject in objects)
        {
            if (sceneObject.Actor is not { } actor)
            {
                Static(sceneObject, null, Placement(sceneObject));
                continue;
            }
            if (!seenActors.Add(actor)) continue;
            Actor(actor, objects.Where(member => member.Actor == actor).ToList());
        }
    }
    private sealed class ExportLifetime(SceneExporter value) : IDisposable
    {
        public SceneExporter Value { get; } = value;
        public void Dispose() => Value.binary.Dispose();
    }

    private readonly List<Obj> samplers = [];
}
