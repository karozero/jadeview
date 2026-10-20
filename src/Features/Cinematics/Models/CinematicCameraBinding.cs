using System.Numerics;

namespace JadeView;

internal sealed record CinematicCameraBinding(SceneObject Source, SceneObject? Target, bool FromSourceEvent = false)
{
    internal uint ReceiverKey { get; init; }
    internal CinematicCameraLens? Lens { get; init; }
    internal CinematicCameraRouting? Routing { get; init; }
    internal static CinematicCameraBinding[] Discover(AnimationClip clip, IReadOnlyCollection<SceneObject> objects,
        IEnumerable<SceneObject> fallbackCandidates)
    {
        var commands = CinematicCameraEvent.Read(clip).Where(item => item.HasLiteralPosition).ToArray();
        var bindings = new List<CinematicCameraBinding>();
        foreach (var group in commands.GroupBy(item => item.ReceiverKey))
        {
            var source = group.Select(command => objects.FirstOrDefault(item => item.GaoKey == command.PositionKey))
                .FirstOrDefault(item => item != null);
            if (source != null) bindings.Add(Create(source, objects, clip, group.Key));
        }
        var routedSources = commands.Select(command => command.PositionKey).ToHashSet();
        bindings.AddRange(fallbackCandidates.Distinct().Where(item => !routedSources.Contains(item.GaoKey))
            .Select(item => Create(item, objects, clip)));
        return bindings
            .OrderByDescending(binding => binding.FromSourceEvent)
            .ThenByDescending(binding => binding.Target != null).ToArray();
    }

    internal static CinematicCameraBinding Create(SceneObject source, IEnumerable<SceneObject> objects,
        AnimationClip? clip = null, uint? receiverKey = null)
    {
        var candidates = objects.ToArray();
        var commands = clip == null ? [] : CinematicCameraEvent.Read(clip);
        var first = commands.FirstOrDefault(item => item.HasLiteralPosition && item.PositionKey == source.GaoKey
            && (receiverKey == null || item.ReceiverKey == receiverKey));
        if (first != null)
        {
            var events = commands.Where(item => item.ReceiverKey == first.ReceiverKey).ToArray();
            var routing = new CinematicCameraRouting(events, candidates);
            return new(source, candidates.FirstOrDefault(item => item.GaoKey == first.TargetKey), true)
            { ReceiverKey = first.ReceiverKey, Lens = routing.Lens, Routing = routing };
        }
        // Unresolved script expressions still use the inspectable helper fallback.
        string targetName = source.Name.Replace("Camera", "Cible", StringComparison.OrdinalIgnoreCase);
        var target = candidates.FirstOrDefault(candidate => candidate != source
            && candidate.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        return new(source, target);
    }

    internal Matrix4x4 RenderPose(float time = 0)
    {
        if (Routing != null) return Routing.Evaluate(time, Source, item => item.RenderMatrix);
        var pose = Source.RenderMatrix;
        return Target == null ? pose : AimAt(pose, Target.RenderMatrix.Translation);
    }

    internal static Matrix4x4 AimAt(Matrix4x4 pose, Vector3 target)
    {
        Vector3 position = pose.Translation;
        Vector3 direction = target - position;
        if (direction.LengthSquared() < 1e-8f) return pose;
        Vector3 forward = Vector3.Normalize(direction);
        Vector3 up = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > .999f
            ? Vector3.UnitZ : Vector3.UnitY;
        return Matrix4x4.Invert(Matrix4x4.CreateLookAt(position, target, up), out var cameraPose)
            ? cameraPose : pose;
    }
}
