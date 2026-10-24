using System.Numerics;

namespace JadeView;

internal sealed class CinematicCameraExportSampler : IDisposable
{
    private readonly CinematicCameraBinding binding;
    private readonly Dictionary<SceneObject, AnimationTrack[]> tracks;
    private readonly Dictionary<SceneActor, ActorExportSnapshot> snapshots = [];
    private readonly Dictionary<SceneActor, float> evaluatedTimes = [];

    internal CinematicCameraExportSampler(CinematicCameraBinding binding, AnimationClip clip,
        AnimationTrack[] sourceTracks)
    {
        this.binding = binding;
        var dependencies = (binding.Routing?.Dependencies ?? new[] { binding.Source, binding.Target }.OfType<SceneObject>())
            .Append(binding.Source).Distinct();
        dependencies = CinematicHierarchyPose.IncludeParentChain(dependencies, binding.PoseObjects);
        tracks = dependencies.ToDictionary(item => item, item => sourceTracks
            .Where(track => track.Bone < 0 && track.Target == item.GaoKey).ToArray());
        try
        {
            foreach (var actor in dependencies.Select(item => item.Actor).OfType<SceneActor>().Distinct())
            {
                snapshots.Add(actor, new(actor));
                bool alreadySelected = actor.Clip is { Cinematic: true } current && current.Key == clip.Key;
                var selected = alreadySelected ? actor.Clip : actor.Clips.FirstOrDefault(item => item.Cinematic && item.Key == clip.Key);
                if (selected != null && !alreadySelected) actor.SelectClip(selected);
                actor.Playing = false;
                actor.Loop = false;
            }
        }
        catch { Dispose(); throw; }
    }

    internal Matrix4x4 Evaluate(float time)
    {
        if (binding.Routing != null)
            return binding.Routing.Evaluate(time, binding.Source,
                item => CinematicHierarchyPose.ResolveRenderMatrix(item, binding.PoseObjects, dependency => ReadPose(dependency, time)));
        var pose = CinematicHierarchyPose.ResolveRenderMatrix(binding.Source, binding.PoseObjects,
            dependency => ReadPose(dependency, time));
        return binding.Target == null ? pose
            : CinematicCameraBinding.AimAt(pose, CinematicHierarchyPose.ResolveRenderMatrix(binding.Target,
                binding.PoseObjects, dependency => ReadPose(dependency, time)).Translation);
    }

    private Matrix4x4 ReadPose(SceneObject item, float time)
    {
        if (item.Actor is { } actor)
        {
            if (!evaluatedTimes.TryGetValue(actor, out float previous) || previous != time)
            {
                actor.Time = time;
                actor.Evaluate(0);
                evaluatedTimes[actor] = time;
            }
            return item.RenderMatrix;
        }
        return AnimationMath.ToRender(CinematicTransformSampler.Evaluate(item.Transform,
            tracks.GetValueOrDefault(item) ?? [], time));
    }

    public void Dispose()
    {
        foreach (var snapshot in snapshots.Values) snapshot.Dispose();
    }
}
