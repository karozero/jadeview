using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private const string CameraLensExtension = "KHR_animation_pointer";
    private bool animatedCameraLens;
    private Obj CameraDefinition(SceneObject sceneObject, uint? clipKey = null, uint? receiverKey = null)
    {
        var bindings = cinematicCameraBindings.Where(pair => pair.Value.Source == sceneObject
            && pair.Value.Lens != null && (clipKey == null || pair.Key.Clip == clipKey)
            && (receiverKey == null || pair.Key.Receiver == receiverKey)).ToArray();
        var lens = bindings.FirstOrDefault().Value?.Lens;
        var perspective = D("yfov", lens == null ? MathF.PI / 4
            : CinematicCameraLens.VerticalAngle(lens.Evaluate(0), CinematicCameraLens.ExportAspect),
            "znear", .1f, "zfar", 10000f);
        string name = receiverKey == null ? sceneObject.Name : $"{sceneObject.Name} · {clipKey:X8} · {receiverKey:X8}";
        var camera = D("name", name, "type", "perspective", "perspective", perspective);
        if (lens == null) return camera;
        perspective["aspectRatio"] = CinematicCameraLens.ExportAspect;
        camera["extras"] = D("jadeViewLensTracks", bindings.Select(pair => D(
            "clipKey", pair.Key.Clip.ToString("X8"), "angleUnit", "horizontal radians",
            "keys", pair.Value.Lens!.Keys.Select(key => D("time", key.Time, "angle", key.Angle)).ToArray())).ToArray());
        if (bindings.Any(pair => pair.Value.Lens!.Keys.Length > 0))
            warnings.Add("Animated camera FOV uses KHR_animation_pointer; importers without this extension retain the initial lens.");
        return camera;
    }

    private void WriteCameraLensAnimations(WorldData world)
    {
        foreach (var clip in SelectedCinematicClips(world, options))
            foreach (var pair in cinematicCameraBindings.Where(pair => pair.Key.Clip == clip.Key))
                WriteCameraLensAnimation(clip, pair.Value);
    }

    private void WriteCameraLensAnimation(AnimationClip clip, CinematicCameraBinding binding)
    {
        if (binding.Lens is not { } lens || lens.Keys.Length == 0
            || !cinematicCameraNodes.TryGetValue((clip.Key, binding.ReceiverKey), out int node)
            || nodes[node].GetValueOrDefault("camera") is not int cameraIndex) return;
        float[] times = LensSampleTimes(clip, lens);
        if (times.Length == 0) return;
        float[] values = times.Select(time => CinematicCameraLens.VerticalAngle(
            lens.Evaluate(time), CinematicCameraLens.ExportAspect)).ToArray();
        var sampler = D("input", Floats(times, 1, "SCALAR", true),
            "output", Floats(values, 1, "SCALAR"), "interpolation", "LINEAR");
        var target = D("path", "pointer", "extensions", D(CameraLensExtension,
            D("pointer", $"/cameras/{cameraIndex}/perspective/yfov")));
        AddExportAnimation(D("name", clip.Name, "samplers", new List<Obj> { sampler },
            "channels", new List<Obj> { D("sampler", 0, "target", target) }), clip);
        animatedCameraLens = true;
    }

    private static float[] LensSampleTimes(AnimationClip clip, CinematicCameraLens lens)
    {
        var times = new SortedSet<float>(AnimationExportSampling.Times(clip));
        foreach (var key in lens.Keys.Where(key => key.Time >= 0 && key.Time <= clip.Duration))
        {
            times.Add(key.Time);
            if (key.Time > 0) times.Add(MathF.BitDecrement(key.Time));
        }
        return times.ToArray();
    }
}
