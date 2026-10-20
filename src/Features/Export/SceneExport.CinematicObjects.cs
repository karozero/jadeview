using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteCinematicObjectAnimations(WorldData world, List<SceneObject> objects)
    {
        foreach (var clip in SelectedCinematicClips(world, options))
        {
            var times = AnimationExportSampling.Times(clip);
            if (times.Length == 0) continue;
            int input = Floats(times, 1, "SCALAR", true);
            var channels = new List<Obj>();
            var samplers = new List<Obj>();
            foreach (var sceneObject in objects.Where(item => item.Actor == null && nodeByObject.ContainsKey(item)))
            {
                var tracks = clip.Tracks.Where(track => track.Bone < 0 && track.Target == sceneObject.GaoKey).ToArray();
                if (tracks.Length == 0) continue;
                CinematicCameraBinding? cameraBinding = null;
                if (!sourceCameraObjects.Contains(sceneObject)
                    && sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
                    cameraBinding = CinematicCameraBinding.Create(sceneObject, objects, clip);
                var values = SampleCinematicObject(sceneObject, tracks, times, cameraBinding, clip);
                int node = nodeByObject[sceneObject];
                AddAnimationChannel("translation", values.Translation, 3, "VEC3", node, input, channels, samplers);
                AddAnimationChannel("rotation", values.Rotation, 4, "VEC4", node, input, channels, samplers);
                AddAnimationChannel("scale", values.Scale, 3, "VEC3", node, input, channels, samplers);
            }
            if (channels.Count == 0) continue;
            var animation = D("name", clip.Name, "samplers", samplers, "channels", channels);
            AddSourceAnimationRecords(animation, clip);
            AddExportAnimation(animation, clip);
        }
    }

    private static (List<float> Translation, List<float> Rotation, List<float> Scale) SampleCinematicObject(
        SceneObject sceneObject, AnimationTrack[] tracks, float[] times,
        CinematicCameraBinding? cameraBinding, AnimationClip clip)
    {
        var translation = new List<float>(times.Length * 3);
        var rotation = new List<float>(times.Length * 4);
        var scale = new List<float>(times.Length * 3);
        using var camera = cameraBinding == null ? null : new CinematicCameraExportSampler(cameraBinding, clip);
        foreach (float time in times)
        {
            var jadePose = CinematicTransformSampler.Evaluate(sceneObject.Transform, tracks, time);
            var renderPose = AnimationMath.ToRender(jadePose);
            if (camera != null) renderPose = camera.Evaluate(time);
            var (sampleScale, sampleRotation, samplePosition) = Decompose(renderPose);
            translation.AddRange([samplePosition.X, samplePosition.Y, samplePosition.Z]);
            rotation.AddRange([sampleRotation.X, sampleRotation.Y, sampleRotation.Z, sampleRotation.W]);
            scale.AddRange([sampleScale.X, sampleScale.Y, sampleScale.Z]);
        }
        return (translation, rotation, scale);
    }
}
