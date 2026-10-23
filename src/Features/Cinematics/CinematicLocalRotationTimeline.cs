using System.Numerics;

namespace JadeView;

internal readonly record struct CinematicLocalRotationCommand(
    uint TargetKey, float Start, float End, float Radians);

internal sealed class CinematicLocalRotationTimeline
{
    private const string RotationFunction = "OBJRotateLocalY";
    private readonly Dictionary<SceneObject, CinematicLocalRotationCommand[]> commandsByTarget;
    private readonly SceneObject[] targets;
    private readonly float[] changeTimes;
    private readonly string[] diagnostics;

    internal IReadOnlyCollection<SceneObject> Targets => targets;
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    internal IReadOnlyList<float> ChangeTimes => changeTimes;

    internal Matrix4x4 EvaluatePose(SceneObject target, Matrix4x4 pose, float time) =>
        commandsByTarget.TryGetValue(target, out var targetCommands)
            ? EvaluatePose(pose, targetCommands, time)
            : pose;

    internal CinematicLocalRotationTimeline(WorldData world,
        IEnumerable<CinematicLocalRotationCommand> commands)
    {
        var objectsByKey = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var unresolved = new List<string>();
        var resolved = new Dictionary<SceneObject, List<CinematicLocalRotationCommand>>();
        foreach (var command in commands.Where(command => float.IsFinite(command.Start)
                     && float.IsFinite(command.End) && command.End >= command.Start
                     && float.IsFinite(command.Radians)))
        {
            if (!objectsByKey.TryGetValue(command.TargetKey, out var matches) || matches.Length != 1)
            {
                unresolved.Add($"{RotationFunction} target {command.TargetKey:X8} is missing or ambiguous.");
                continue;
            }

            if (!resolved.TryGetValue(matches[0], out var targetCommands))
                resolved.Add(matches[0], targetCommands = []);
            targetCommands.Add(command);
        }

        commandsByTarget = resolved.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderBy(command => command.Start).ToArray());
        targets = commandsByTarget.Keys.ToArray();
        changeTimes = commandsByTarget.Values.SelectMany(targetCommands => targetCommands)
            .SelectMany(command => command.Start == command.End
                ? [command.Start] : new[] { command.Start, command.End })
            .Where(float.IsFinite).Distinct().Order().ToArray();
        diagnostics = unresolved.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static CinematicLocalRotationCommand[] ReadSourceCommands(AnimationClip clip)
    {
        return CinematicLiteralCall.ReadTimelineWithDuration(clip)
            .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == RotationFunction
                && item.Call.Arguments.Length >= 1)
            .Select(item => CreateCommand(item.Time, item.Duration,
                item.Call.Target, item.Call.Arguments[0]))
            .Where(command => command.HasValue)
            .Select(command => command!.Value)
            .ToArray();
    }

    internal static CinematicLocalRotationCommand? CreateCommand(float start, float duration,
        uint targetKey, uint angleBits)
    {
        float radians = BitConverter.UInt32BitsToSingle(angleBits);
        if (!float.IsFinite(start) || !float.IsFinite(radians) || targetKey is 0 or uint.MaxValue)
            return null;
        float validDuration = float.IsFinite(duration) ? Math.Max(0, duration) : 0;
        return new(targetKey, start, start + validDuration, radians);
    }

    internal void ResetTargets()
    {
        foreach (var target in targets) target.PreviewMatrix = null;
    }

    internal void Evaluate(float time)
    {
        foreach (var (target, targetCommands) in commandsByTarget)
        {
            Matrix4x4 pose = target.PreviewMatrix ?? AnimationMath.Trs(target.Transform);
            bool changed = false;
            foreach (var command in targetCommands)
            {
                if (time < command.Start) break;
                float progress = command.End <= command.Start
                    ? 1
                    : Math.Clamp((time - command.Start) / (command.End - command.Start), 0, 1);
                if (progress <= 0) continue;
                pose = RotateLocalYAxis(pose, command.Radians * progress);
                changed = true;
            }
            if (changed) target.PreviewMatrix = pose;
        }
    }

    internal static Matrix4x4 EvaluatePose(Matrix4x4 pose,
        IReadOnlyList<CinematicLocalRotationCommand> commands, float time)
    {
        foreach (var command in commands)
        {
            if (time < command.Start) break;
            float progress = command.End <= command.Start
                ? 1
                : Math.Clamp((time - command.Start) / (command.End - command.Start), 0, 1);
            if (progress > 0) pose = RotateLocalYAxis(pose, command.Radians * progress);
        }
        return pose;
    }

    internal static Matrix4x4 RotateLocalYAxis(Matrix4x4 pose, float radians)
    {
        if (!float.IsFinite(radians) || radians == 0) return pose;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        float firstX = pose.M11;
        float firstY = pose.M12;
        float firstZ = pose.M13;
        float thirdX = pose.M31;
        float thirdY = pose.M32;
        float thirdZ = pose.M33;
        pose.M11 = firstX * cosine - thirdX * sine;
        pose.M12 = firstY * cosine - thirdY * sine;
        pose.M13 = firstZ * cosine - thirdZ * sine;
        pose.M31 = thirdX * cosine + firstX * sine;
        pose.M32 = thirdY * cosine + firstY * sine;
        pose.M33 = thirdZ * cosine + firstZ * sine;
        return pose;
    }
}
