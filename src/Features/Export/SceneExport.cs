using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

// glTF 2.0 / GLB, right-handed Y-up. No temporary image files or GPU context required.
public sealed partial class SceneExporter
{
    private readonly ResourceResolver resolver;
    private readonly SceneExportOptions options;
    private readonly MemoryStream binary = new();
    private readonly List<Obj> views = [], accessors = [], nodes = [], meshes = [], materials = [], images = [], textures = [], skins = [], animations = [], cameras = [];
    private readonly List<int> roots = [];
    private readonly Dictionary<string, int> imageCache = [];
    private readonly HashSet<string> warnings = [];
    private readonly Dictionary<SceneObject, int> nodeByObject = [];
    private readonly Dictionary<(WorldData World, uint ClipKey), AnimationTrack[]> cinematicTrackCache = [];
    private readonly Dictionary<(WorldData World, uint ClipKey), CinematicNestedEventTimeline> cinematicEventCache = [];
    private readonly Dictionary<(WorldData World, uint ClipKey), CinematicOrientToTimeline> cinematicOrientCache = [];
    private readonly Dictionary<(WorldData World, uint ClipKey), CinematicLocalRotationTimeline> cinematicRotationCache = [];
    private HashSet<SceneObject> animatedCinematicObjects = [];
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
        exporter.ResolveCinematicCameras(world);
        exporter.animatedCinematicObjects = exporter.FindCinematicObjects(world, objects);
        exporter.SetOrigin(objects, selected);
        exporter.ExportObjects(objects);
        exporter.WriteRoutedCameras(world, objects);
        if (exporter.cameras.Count > 0)
            exporter.warnings.Add("Cinematic camera motion uses the source camera transform; lens and projection are approximated for Blender export.");
        if (options.Animations) exporter.WriteCinematicObjectAnimations(world, objects);
        if (options.Animations) exporter.WriteCameraLensAnimations(world);
        exporter.AddCinematicSourceMetadata(world);
        if (options.ExperimentalMouthCycle && exporter.experimentalMouthClips == 0)
            exporter.warnings.Add("No active source-qualified mouth preview was included; no experimental mouth clip was exported.");
        if (exporter.nodes.Count == 0)
            throw new InvalidOperationException("Nothing matches the export options.");
        exporter.Write(path, world.Name);
        return new(exporter.meshes.Count, exporter.empties, exporter.images.Count,
            exporter.animations.Count, exporter.warnings.ToArray());
    }

    private HashSet<SceneObject> FindCinematicObjects(WorldData world, List<SceneObject> objects)
    {
        var clips = SelectedCinematicClips(world, options);
        var targetKeys = clips
            .SelectMany(clip => CinematicTracks(clip, world))
            .Where(track => track.Bone < 0).Select(track => track.Target).ToHashSet();
        var visibilityTargets = clips.SelectMany(clip =>
            new CinematicObjectState(clip, world, world.Actors).VisibilityTargets).ToHashSet();
        var orientTargets = clips.SelectMany(clip => CinematicOrientTo(clip, world).OrientedObjects).ToHashSet();
        var rotationTargets = clips.SelectMany(clip => CinematicLocalRotations(clip, world).Targets).ToHashSet();
        return objects.Where(sceneObject => sceneObject.Actor == null
            && (targetKeys.Contains(sceneObject.GaoKey) || visibilityTargets.Contains(sceneObject)
                || orientTargets.Contains(sceneObject) || rotationTargets.Contains(sceneObject))).ToHashSet();
    }

    private AnimationTrack[] CinematicTracks(AnimationClip clip, WorldData world)
    {
        if (cinematicTrackCache.TryGetValue((world, clip.Key), out var cached)) return cached;
        AnimationTrack[] nestedTracks = NestedCinematicEvents(clip, world).TransformTracks;
        var tracks = clip.Tracks.Concat(CinematicScriptTransformTracks.Read(clip))
            .Concat(nestedTracks).ToArray();
        cinematicTrackCache[(world, clip.Key)] = tracks;
        return tracks;
    }

    private CinematicNestedEventTimeline NestedCinematicEvents(AnimationClip clip, WorldData world)
    {
        if (cinematicEventCache.TryGetValue((world, clip.Key), out var cached)) return cached;
        var timeline = options.CinematicDocument == null
            ? new CinematicNestedEventTimeline([], [], [], [], [])
            : CinematicNestedEventTransformTracks.ReadTimeline(clip, world, options.CinematicDocument,
                options.DisabledCinematicCallbackOwners, options.PreviewedCinematicSceneEventOwners);
        cinematicEventCache[(world, clip.Key)] = timeline;
        return timeline;
    }

    private CinematicOrientToTimeline CinematicOrientTo(AnimationClip clip, WorldData world)
    {
        if (cinematicOrientCache.TryGetValue((world, clip.Key), out var cached)) return cached;
        var timeline = new CinematicOrientToTimeline(clip, world,
            NestedCinematicEvents(clip, world).Commands, CinematicTracks(clip, world));
        cinematicOrientCache[(world, clip.Key)] = timeline;
        return timeline;
    }

    private CinematicLocalRotationTimeline CinematicLocalRotations(AnimationClip clip, WorldData world)
    {
        if (cinematicRotationCache.TryGetValue((world, clip.Key), out var cached)) return cached;
        var timeline = new CinematicLocalRotationTimeline(world,
            NestedCinematicEvents(clip, world).RotationCommands);
        cinematicRotationCache[(world, clip.Key)] = timeline;
        return timeline;
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
