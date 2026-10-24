using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteCinematicActorOrientAnimations(SceneActor actor,
        List<(int Node, Func<Matrix4x4> Pose)> animated)
    {
        if (exportWorld == null || !nodeByObject.TryGetValue(actor.Root, out int rootNode)) return;
        var rootTrack = animated.FirstOrDefault(item => item.Node == rootNode);
        if (rootTrack.Pose == null) return;

        foreach (AnimationClip sourceClip in SelectedCinematicClips(exportWorld, options))
        {
            if (cinematicActorOrientationsWritten.Contains((actor, sourceClip.Key))) continue;
            CinematicOrientToTimeline orientTo = CinematicOrientTo(sourceClip, exportWorld);
            CinematicSightSetTimeline sightSet = CinematicSightSets(sourceClip, exportWorld);
            if (!orientTo.OrientedObjects.Contains(actor.Root) && !sightSet.Participants.Contains(actor.Root)) continue;

            AnimationTrack[] tracks = CinematicTracks(sourceClip, exportWorld);
            float[] times = AnimationExportSampling.Times(sourceClip, tracks)
                .Concat(orientTo.ChangeTimes.SelectMany(time => time > 0 && time <= sourceClip.Duration
                    ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Concat(sightSet.ChangeTimes.SelectMany(time => time > 0 && time <= sourceClip.Duration
                    ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Where(time => time >= 0 && time <= sourceClip.Duration).Distinct().Order().ToArray();
            if (times.Length == 0) continue;

            using var snapshot = new ActorExportSnapshot(actor);
            using var orientSampler = new CinematicActorOrientExportSampler(actor, exportWorld,
                tracks, orientTo, sightSet, sourceClip.Key);
            actor.Clip = null;
            actor.Playing = false;
            actor.Loop = false;
            var translation = new List<float>(times.Length * 3);
            var rotation = new List<float>(times.Length * 4);
            var scale = new List<float>(times.Length * 3);
            foreach (float time in times)
            {
                actor.Time = 0;
                actor.Evaluate(0);
                orientSampler.Evaluate(time);
                var (sampleScale, sampleRotation, samplePosition) = Decompose(rootTrack.Pose());
                translation.AddRange([samplePosition.X, samplePosition.Y, samplePosition.Z]);
                rotation.AddRange([sampleRotation.X, sampleRotation.Y, sampleRotation.Z, sampleRotation.W]);
                scale.AddRange([sampleScale.X, sampleScale.Y, sampleScale.Z]);
            }

            int input = Floats(times, 1, "SCALAR", true);
            var channels = new List<Obj>();
            var samplers = new List<Obj>();
            AddAnimationChannel("translation", translation, 3, "VEC3", rootNode, input, channels, samplers);
            AddAnimationChannel("rotation", rotation, 4, "VEC4", rootNode, input, channels, samplers);
            AddAnimationChannel("scale", scale, 3, "VEC3", rootNode, input, channels, samplers);
            AddExportAnimation(D("name", sourceClip.Name, "samplers", samplers, "channels", channels), sourceClip);
            cinematicActorOrientationsWritten.Add((actor, sourceClip.Key));
        }
    }
}
