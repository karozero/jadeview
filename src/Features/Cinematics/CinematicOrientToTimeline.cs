using System.Numerics;

namespace JadeView;

internal sealed class CinematicOrientToTimeline
{
    private const int OrientToSelector = 8151;
    private readonly SceneObject[] participants;
    private readonly OrientCommand[] commands;
    private readonly Dictionary<SceneObject, OrientCommand[]> commandsBySource;
    private readonly Dictionary<uint, float[]> authoredRotationTimesByTarget;
    private readonly string[] diagnostics;

    internal IReadOnlyCollection<SceneObject> Participants => participants;
    internal IReadOnlyCollection<SceneObject> OrientedObjects => commands.Select(command => command.Source)
        .Distinct().ToArray();
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    internal IEnumerable<float> ChangeTimes => commands.Select(command => command.Time).Distinct();

    internal CinematicOrientToTimeline(AnimationClip clip, WorldData world,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedCommands,
        IEnumerable<AnimationTrack> tracks)
    {
        AnimationTrack[] sourceTracks = tracks.ToArray();
        authoredRotationTimesByTarget = sourceTracks
            .Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys
                .Where(key => key.Rotation != null && float.IsFinite(key.Time))
                .Select(key => (track.Target, key.Time)))
            .GroupBy(item => item.Target)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Time)
                .Distinct().Order().ToArray());
        var objectsByKey = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var unresolved = new List<string>();
        var resolved = new List<OrientCommand>();

        var calls = CinematicLiteralCall.ReadTimeline(clip)
            .Concat(nestedCommands ?? [])
            .Where(item => item.Call.Selector == OrientToSelector && item.Call.Arguments.Length == 1)
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Arguments[0]))
            .OrderBy(item => item.Time);

        foreach (var (time, call) in calls)
        {
            uint referenceKey = call.Arguments[0];
            if (!TryUniqueObject(objectsByKey, call.Target, out var source)
                || !TryUniqueObject(objectsByKey, referenceKey, out var reference))
            {
                unresolved.Add($"OBJ_OrientTo at {time:F3}s on {call.Target:X8}: source or reference GAO is missing/ambiguous.");
                continue;
            }
            if (source.Actor != null || reference.Actor != null)
            {
                unresolved.Add($"OBJ_OrientTo at {time:F3}s on {source.Name}: actor-root orientation is not yet evaluated by this preview.");
                continue;
            }
            resolved.Add(new(time, source, reference));
        }

        commands = resolved.ToArray();
        commandsBySource = commands
            .GroupBy(command => command.Source)
            .ToDictionary(group => group.Key, group => group.OrderBy(command => command.Time).ToArray());
        participants = commands.SelectMany(command => new[] { command.Source, command.Reference })
            .Distinct().ToArray();
        diagnostics = unresolved.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal void Evaluate(float time)
    {
        foreach (var (source, sourceCommands) in commandsBySource)
        {
            int commandIndex = FindLastAtOrBefore(sourceCommands, time);
            if (commandIndex < 0) continue;
            OrientCommand command = sourceCommands[commandIndex];
            if (HasLaterAuthoredRotation(command, time)) continue;

            Matrix4x4 sourcePose = Pose(source);
            Matrix4x4 referencePose = Pose(command.Reference);
            source.PreviewMatrix = Orient(sourcePose, referencePose.Translation);
        }
    }

    private int FindLastAtOrBefore(OrientCommand[] sourceCommands, float time)
    {
        int low = 0;
        int high = sourceCommands.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (sourceCommands[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private bool HasLaterAuthoredRotation(OrientCommand command, float time)
    {
        if (!authoredRotationTimesByTarget.TryGetValue(command.Source.GaoKey, out float[]? keyTimes))
            return false;

        int low = 0;
        int high = keyTimes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (keyTimes[middle] <= command.Time) low = middle + 1;
            else high = middle;
        }
        return low < keyTimes.Length && keyTimes[low] <= time;
    }

    private static Matrix4x4 Pose(SceneObject sceneObject) =>
        sceneObject.PreviewMatrix ?? AnimationMath.Trs(sceneObject.Transform);

    private static Matrix4x4 Orient(Matrix4x4 sourcePose, Vector3 referencePosition)
    {
        Vector3 sight = referencePosition - sourcePose.Translation;
        if (!float.IsFinite(sight.LengthSquared()) || sight.LengthSquared() < 1e-10f) return sourcePose;
        sight = Vector3.Normalize(sight);

        Vector3 banking = Vector3.UnitZ;
        Vector3 xAxis = -Vector3.Cross(sight, banking);
        if (xAxis.LengthSquared() < 1e-10f)
        {
            banking = Vector3.UnitX;
            xAxis = -Vector3.Cross(sight, banking);
        }
        if (xAxis.LengthSquared() < 1e-10f) return sourcePose;
        xAxis = Vector3.Normalize(xAxis);
        Vector3 yAxis = -sight;
        Vector3 zAxis = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));
        var rotationMatrix = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            0, 0, 0, 1);
        Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix));
        Matrix4x4.Decompose(sourcePose, out var scale, out _, out var position);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(position);
    }

    private static bool TryUniqueObject(IReadOnlyDictionary<uint, SceneObject[]> objectsByKey,
        uint key, out SceneObject sceneObject)
    {
        sceneObject = null!;
        if (key is 0 or uint.MaxValue || !objectsByKey.TryGetValue(key, out var matches)
            || matches.Length != 1) return false;
        sceneObject = matches[0];
        return true;
    }

    private sealed record OrientCommand(float Time, SceneObject Source, SceneObject Reference);
}
