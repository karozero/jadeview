using System.Numerics;

namespace JadeView;

internal sealed class CinematicCameraRouting
{
    private sealed record Route(CinematicCameraEvent Command, SceneObject Position, SceneObject? Target);
    private readonly Route[] routes;
    internal IEnumerable<SceneObject> PositionSources => routes.Select(route => route.Position).Distinct();
    internal IEnumerable<float> Boundaries => routes.Select(route => route.Command.Time);
    internal IEnumerable<SceneObject> Dependencies => routes.SelectMany(route => route.Target == null
        ? new[] { route.Position } : new[] { route.Position, route.Target }).Distinct();
    internal CinematicCameraLens Lens { get; }

    internal CinematicCameraRouting(CinematicCameraEvent[] commands, SceneObject[] objects,
        IEnumerable<(float Time, float Angle)>? scriptViewSetters = null)
    {
        routes = ResolveRoutes(commands, objects).ToArray();
        Lens = new(commands, scriptViewSetters);
    }

    private static IEnumerable<Route> ResolveRoutes(IEnumerable<CinematicCameraEvent> commands, SceneObject[] objects)
    {
        foreach (var command in commands.OrderBy(command => command.Time))
        {
            bool valid = command.HasLiteralPosition && float.IsFinite(command.Time)
                && float.IsFinite(command.Transition) && IsFinite(command.PositionOffset) && IsFinite(command.TargetOffset);
            if (!valid) continue;
            var position = objects.FirstOrDefault(item => item.GaoKey == command.PositionKey);
            var target = objects.FirstOrDefault(item => item.GaoKey == command.TargetKey);
            if (position == null || (command.TargetKey != 0 && target == null)) continue;
            yield return new(command, position, target);
        }
    }

    internal Matrix4x4 Evaluate(float time, SceneObject fallback, Func<SceneObject, Matrix4x4> readPose)
    {
        var route = routes.LastOrDefault(route => route.Command.Time <= time);
        if (route == null) return readPose(fallback);
        var pose = readPose(route.Position);
        // Iris always resolves the message's local camera position against its
        // destination object. Float2 controls DYN movement speed; it does not
        // disable the authored offset while references change.
        pose.Translation += Offset(pose, route.Command.PositionOffset);
        if (route.Target == null) return pose;
        var target = readPose(route.Target);
        var point = target.Translation;
        point += Offset(target, route.Command.TargetOffset);
        return CinematicCameraBinding.AimAt(pose, point);
    }

    private static Vector3 Offset(Matrix4x4 pose, Vector3 offset) =>
        Matrix4x4.Decompose(pose, out _, out var rotation, out _)
            ? Vector3.Transform(TransformData.ToRender(offset), rotation) : Vector3.Zero;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
