using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteAnimations(SceneActor actor, List<(int Node, Func<Matrix4x4> Pose)> animated)
    {
        var clips = SelectedAnimationClips(actor);
        bool rootMotion = actor.RootMotion;
        foreach (var clip in clips)
        {
            clip.EnsureLoaded();
            if (clip.Error != null)
            {
                warnings.Add($"Animation {clip.Name}: {clip.Error}");
                continue;
            }
            if (clip.Unsupported > 0)
                warnings.Add($"Animation '{clip.Name}' contains {clip.Unsupported} unsupported records; export is partial.");
            var times = AnimationExportSampling.Times(clip);
            if (times.Length == 0)
                continue;
            actor.RootMotion = rootMotion;
            actor.SelectClip(clip);
            actor.Playing = false;
            actor.Loop = false;
            if (actor.ClipShapes.TryGetValue(clip, out uint shape) && shape != actor.DefaultShape)
                warnings.Add($"Animation '{clip.Name}' selects a different mesh appearance; the exported mesh remains the selected appearance.");
            WriteAnimation(actor, animated, clip, times);
        }
    }

    private void WriteAnimation(SceneActor actor, List<(int Node, Func<Matrix4x4> Pose)> animated,
        AnimationClip clip, float[] times)
    {
        var values = SampleAnimationTracks(actor, animated, times);
        int input = Floats(times, 1, "SCALAR", true);
        var samplers = new List<Obj>();
        var channels = new List<Obj>();
        for (int nodeIndex = 0; nodeIndex < animated.Count; nodeIndex++)
        {
            AddAnimationChannel("translation", values[nodeIndex].Translation, 3, "VEC3", animated[nodeIndex].Node, input, channels, samplers);
            AddAnimationChannel("rotation", values[nodeIndex].Rotation, 4, "VEC4", animated[nodeIndex].Node, input, channels, samplers);
            AddAnimationChannel("scale", values[nodeIndex].Scale, 3, "VEC3", animated[nodeIndex].Node, input, channels, samplers);
        }
        var animation = D("name", $"{actor.Root.Name} {clip.Name}", "samplers", samplers, "channels", channels);
        AddSourceAnimationRecords(animation, clip);
        animations.Add(animation);
    }

    private AnimationClip[] SelectedAnimationClips(SceneActor actor)
    {
        if (options.AllClips) return actor.Clips.ToArray();
        if (actor.Clip != null) return [actor.Clip];
        return actor.Clips.Take(1).ToArray();
    }

    private static ExportAnimationTrack[] SampleAnimationTracks(SceneActor actor,
        List<(int Node, Func<Matrix4x4> Pose)> animated, float[] times)
    {
        var tracks = animated.Select(_ => new ExportAnimationTrack()).ToArray();
        foreach (float time in times)
        {
            actor.Time = time;
            actor.Evaluate(0);
            for (int nodeIndex = 0; nodeIndex < animated.Count; nodeIndex++)
            {
                var (scale, rotation, translation) = Decompose(animated[nodeIndex].Pose());
                tracks[nodeIndex].Append(scale, rotation, translation);
            }
        }
        return tracks;
    }

    private void AddAnimationChannel(string path, List<float> values, int size, string type,
        int node, int input, List<Obj> channels, List<Obj> samplers)
    {
        channels.Add(D("sampler", samplers.Count, "target", D("node", node, "path", path)));
        samplers.Add(D("input", input, "output", Floats(values.ToArray(), size, type), "interpolation", "LINEAR"));
    }
}
