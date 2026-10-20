using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private readonly Dictionary<(uint Clip, uint Receiver), int> cinematicCameraNodes = [];

    private void WriteRoutedCameras(WorldData world, List<SceneObject> objects)
    {
        foreach (var clip in SelectedCinematicClips(world, options))
            foreach (var pair in cinematicCameraBindings.Where(pair => pair.Key.Clip == clip.Key))
            {
                if (!objects.Contains(pair.Value.Source)) continue;
                WriteRoutedCamera(clip, pair.Value);
            }
    }

    private void WriteRoutedCamera(AnimationClip clip, CinematicCameraBinding binding)
    {
        float[] times = CameraSampleTimes(clip, binding);
        if (times.Length == 0) return;
        using var sampler = new CinematicCameraExportSampler(binding, clip);
        var node = D("name", $"Cinematic camera · {binding.Source.Name} · {clip.Key:X8} · {binding.ReceiverKey:X8}", "camera", cameras.Count,
            "extras", D("gaoKey", binding.Source.GaoKey.ToString("X8"), "cinematicClipKey", clip.Key.ToString("X8"),
                "cameraReceiverKey", binding.ReceiverKey.ToString("X8")));
        SetTrs(node, sampler.Evaluate(0));
        int nodeIndex = nodes.Count;
        nodes.Add(node);
        roots.Add(nodeIndex);
        cinematicCameraNodes.Add((clip.Key, binding.ReceiverKey), nodeIndex);
        cameras.Add(CameraDefinition(binding.Source, clip.Key, binding.ReceiverKey));
        var values = new ExportAnimationTrack();
        foreach (float time in times)
        {
            var (scale, rotation, translation) = Decompose(sampler.Evaluate(time));
            values.Append(scale, rotation, translation);
        }
        var channels = new List<Obj>();
        var samplers = new List<Obj>();
        int input = Floats(times, 1, "SCALAR", true);
        AddAnimationChannel("translation", values.Translation, 3, "VEC3", nodeIndex, input, channels, samplers);
        AddAnimationChannel("rotation", values.Rotation, 4, "VEC4", nodeIndex, input, channels, samplers);
        AddExportAnimation(D("name", clip.Name, "channels", channels, "samplers", samplers), clip);
    }

    private static float[] CameraSampleTimes(AnimationClip clip, CinematicCameraBinding binding)
    {
        var times = new SortedSet<float>(LensSampleTimes(clip, binding.Lens!));
        foreach (float boundary in binding.Routing!.Boundaries.Where(time => time >= 0 && time <= clip.Duration))
        {
            times.Add(boundary);
            if (boundary > 0) times.Add(MathF.BitDecrement(boundary));
        }
        return times.ToArray();
    }
}
