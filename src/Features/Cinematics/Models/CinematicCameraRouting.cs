using System.Numerics;

namespace JadeView;

internal sealed class CinematicCameraRouting
{
    private sealed record Route(CinematicCameraEvent Command, SceneObject Position, SceneObject? Target, bool AppliesOffsets);
    private readonly Route[] routes;
    internal IEnumerable<SceneObject> PositionSources => routes.Select(route => route.Position).Distinct();
    internal IEnumerable<float> Boundaries => routes.Select(route => route.Command.Time);
    internal IEnumerable<SceneObject> Dependencies => routes.SelectMany(route => route.Target == null
        ? new[] { route.Position } : new[] { route.Position, route.Target }).Distinct();
    internal CinematicCameraLens Lens { get; }

    internal CinematicCameraRouting(CinematicCameraEvent[] commands, SceneObject[] objects)
    {
        routes = ResolveRoutes(commands, objects).ToArray();
        Lens = new(commands);
    }

    private static IEnumerable<Route> ResolveRoutes(IEnumerable<CinematicCameraEvent> commands, SceneObject[] objects)
    {
        CinematicCameraEvent? previous = null;
        foreach (var command in commands.OrderBy(command => command.Time))
        {
            bool valid = command.HasLiteralPosition && float.IsFinite(command.Time)
                && float.IsFinite(command.Transition) && IsFinite(command.PositionOffset) && IsFinite(command.TargetOffset);
            if (!valid) continue;
            var position = objects.FirstOrDefault(item => item.GaoKey == command.PositionKey);
            var target = objects.FirstOrDefault(item => item.GaoKey == command.TargetKey);
            if (position == null || (command.TargetKey != 0 && target == null)) continue;
            // Native message 60 resets the positional delay unless either reference changes.
            bool sameReferences = previous?.PositionKey == command.PositionKey && previous.TargetKey == command.TargetKey;
            yield return new(command, position, target, command.Transition == 0 || sameReferences);
            previous = command;
        }
    }

    internal Matrix4x4 Evaluate(float time, SceneObject fallback, Func<SceneObject, Matrix4x4> readPose)
    {
        var route = routes.LastOrDefault(route => route.Command.Time <= time);
        if (route == null) return readPose(fallback);
        var pose = readPose(route.Position);
        // Native cuts transform offsets without the object's separate scale.
        // Nonzero-transition target offsets go through the camera-target DYN rig;
        // do not claim to reproduce those using a simple local-point transform.
        if (route.AppliesOffsets) pose.Translation += Offset(pose, route.Command.PositionOffset);
        if (route.Target == null) return pose;
        var target = readPose(route.Target);
        var point = target.Translation;
        if (route.AppliesOffsets) point += Offset(target, route.Command.TargetOffset);
        return CinematicCameraBinding.AimAt(pose, point);
    }

    private static Vector3 Offset(Matrix4x4 pose, Vector3 offset) =>
        Matrix4x4.Decompose(pose, out _, out var rotation, out _)
            ? Vector3.Transform(TransformData.ToRender(offset), rotation) : Vector3.Zero;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
