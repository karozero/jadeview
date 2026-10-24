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
            AddAnimationRecordCoverageWarning(clip);
            var times = AnimationExportSampling.Times(clip);
            if (times.Length == 0)
                continue;
            actor.RootMotion = rootMotion;
            actor.SelectClip(clip);
            actor.Playing = false;
            actor.Loop = false;
            if (actor.ClipShapes.TryGetValue(clip, out uint shape) && shape != actor.DefaultShape)
                warnings.Add($"Animation '{clip.Name}' selects a different mesh appearance; the exported mesh remains the selected appearance.");
            using CinematicActorOrientExportSampler? orientSampler = CreateActorOrientSampler(actor, clip);
            if (orientSampler != null) cinematicActorOrientationsWritten.Add((actor, clip.Key));
            WriteAnimation(actor, animated, clip, times, orientSampler);
        }
    }

    private CinematicActorOrientExportSampler? CreateActorOrientSampler(SceneActor actor, AnimationClip actorClip)
    {
        if (exportWorld == null || options.CinematicClipKey is uint selectedKey
            && selectedKey != actorClip.Key) return null;
        AnimationClip? sourceClip = SelectedCinematicClips(exportWorld, options)
            .FirstOrDefault(clip => clip.Key == actorClip.Key);
        if (sourceClip == null) return null;

        CinematicOrientToTimeline orientTo = CinematicOrientTo(sourceClip, exportWorld);
        CinematicSightSetTimeline sightSet = CinematicSightSets(sourceClip, exportWorld);
        if (!orientTo.OrientedObjects.Contains(actor.Root) && !sightSet.Participants.Contains(actor.Root)) return null;
        return new(actor, exportWorld, CinematicTracks(sourceClip, exportWorld), orientTo, sightSet,
            sourceClip.Key);
    }

    private void AddAnimationRecordCoverageWarning(AnimationClip clip)
    {
        var inventory = CinematicEventExportInventoryBuilder.Build(clip);
        if (inventory.RawRecordCount == 0)
        {
            if (clip.Unsupported > 0)
                warnings.Add($"Animation '{clip.Name}' reports {clip.Unsupported} unsupported records without retained payload metadata; export coverage is incomplete.");
            return;
        }
        if (inventory.UnknownRecordCount > 0)
        {
            warnings.Add($"Animation '{clip.Name}' has {inventory.UnknownRecordCount} unclassified opaque records "
                + $"({inventory.DecodedRecordCount} decoded, {inventory.NonEventRecordCount} transform-control records); "
                + "raw payloads are retained in GLB extras, but extras do not execute game behavior.");
            return;
        }

        warnings.Add($"Animation '{clip.Name}' has {inventory.DecodedRecordCount} decoded source records and "
            + $"{inventory.NonEventRecordCount} transform-control records; raw payloads are retained in GLB extras, "
            + "but extras do not execute game behavior.");
    }

    private void WriteAnimation(SceneActor actor, List<(int Node, Func<Matrix4x4> Pose)> animated,
        AnimationClip clip, float[] times, CinematicActorOrientExportSampler? orientSampler)
    {
        var values = SampleAnimationTracks(actor, animated, times, orientSampler);
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
        AddExportAnimation(animation, clip);
    }

    private AnimationClip[] SelectedAnimationClips(SceneActor actor)
    {
        if (options.CinematicClipKey is { } key)
            return actor.Clip is { Cinematic: true } clip && clip.Key == key ? [clip] : [];
        if (options.AllClips) return actor.Clips.ToArray();
        if (actor.Clip != null) return [actor.Clip];
        return actor.Clips.Take(1).ToArray();
    }

    private static ExportAnimationTrack[] SampleAnimationTracks(SceneActor actor,
        List<(int Node, Func<Matrix4x4> Pose)> animated, float[] times,
        CinematicActorOrientExportSampler? orientSampler)
    {
        var tracks = animated.Select(_ => new ExportAnimationTrack()).ToArray();
        foreach (float time in times)
        {
            actor.Time = time;
            actor.Evaluate(0);
            orientSampler?.Evaluate(time);
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
