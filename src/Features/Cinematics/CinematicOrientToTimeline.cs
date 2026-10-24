using System.Numerics;

namespace JadeView;

internal sealed class CinematicOrientToTimeline
{
    private const int OrientToSelector = 8151;
    private readonly SceneObject[] worldObjects;
    private readonly HashSet<SceneObject> animatedObjects;
    private readonly SceneObject[] participants;
    private readonly OrientCommand[] commands;
    private readonly Dictionary<SceneObject, OrientCommand[]> commandsBySource;
    private readonly Dictionary<uint, float[]> authoredRotationTimesByTarget;
    private readonly string[] diagnostics;

    internal IReadOnlyCollection<SceneObject> Participants => participants;
    internal IReadOnlyCollection<SceneObject> OrientedObjects => commands.Select(command => command.Source)
        .Distinct().ToArray();
    internal bool HasCommands => commands.Length > 0;
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    internal IEnumerable<float> ChangeTimes => commands.Select(command => command.Time).Distinct();

    internal CinematicOrientToTimeline(AnimationClip clip, WorldData world,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedCommands,
        IEnumerable<AnimationTrack> tracks)
    {
        worldObjects = world.Objects.ToArray();
        AnimationTrack[] sourceTracks = tracks.ToArray();
        animatedObjects = sourceTracks.Where(track => track.Bone < 0)
            .SelectMany(track => worldObjects.Where(sceneObject => sceneObject.GaoKey == track.Target))
            .ToHashSet();
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

    internal bool HasCommandAfter(SceneObject source, float afterTime, float throughTime) =>
        commandsBySource.TryGetValue(source, out OrientCommand[]? sourceCommands)
            && sourceCommands.Any(command => command.Time > afterTime && command.Time <= throughTime);

    internal void Evaluate(float time, CinematicSightSetTimeline? sightSet = null)
    {
        foreach (var (source, sourceCommands) in commandsBySource)
        {
            int commandIndex = FindLastAtOrBefore(sourceCommands, time);
            if (!TryResolveWorldPose(source, out Matrix4x4 sourceWorldRender)) continue;

            Matrix4x4 resultWorldRender = sourceWorldRender;
            if (commandIndex >= 0)
            {
                OrientCommand command = sourceCommands[commandIndex];
                if (sightSet?.HasCommandAfter(source, command.Time, time) != true
                    && !HasLaterAuthoredRotation(command, time)
                    && TryResolveWorldPose(command.Reference, out Matrix4x4 referenceWorldRender))
                {
                    Matrix4x4 sourceWorldJade = AnimationMath.FromRender(sourceWorldRender);
                    Vector3 referencePositionJade = TransformData.FromRender(referenceWorldRender.Translation);
                    Matrix4x4 orientedWorldJade = Orient(sourceWorldJade, referencePositionJade);
                    resultWorldRender = PreserveSerializedLocalPosition(source, sourceWorldRender,
                        AnimationMath.ToRender(orientedWorldJade));
                }
            }

            source.PreviewMatrix = AnimationMath.FromRender(resultWorldRender);
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

    private bool TryResolveWorldPose(SceneObject sceneObject, out Matrix4x4 renderPose)
    {
        Matrix4x4 ReadWorldPose(SceneObject item) => item.PreviewMatrix is Matrix4x4 preview
            && (item.Actor != null || animatedObjects.Contains(item))
                ? AnimationMath.ToRender(preview)
                : item.Transform.ToRenderMatrix();
        bool HasAuthoritativePreview(SceneObject item) => item.Actor != null || animatedObjects.Contains(item);

        return CinematicHierarchyPose.TryResolveRenderMatrix(sceneObject, worldObjects,
            ReadWorldPose, out renderPose, out _, HasAuthoritativePreview);
    }

    private Matrix4x4 PreserveSerializedLocalPosition(SceneObject source, Matrix4x4 currentWorldRender,
        Matrix4x4 orientedWorldRender)
    {
        if (!HasParent(source)
            || !TryResolveParentPose(source, out Matrix4x4 parentRender)
            || !Matrix4x4.Invert(parentRender, out Matrix4x4 inverseParent))
            return orientedWorldRender;

        Matrix4x4 currentLocalRender = currentWorldRender * inverseParent;
        Matrix4x4 localRender = orientedWorldRender * inverseParent;
        if (!Matrix4x4.Decompose(currentLocalRender, out _, out _, out Vector3 localPosition)
            || !Matrix4x4.Decompose(localRender, out Vector3 scale, out Quaternion rotation, out _))
            return orientedWorldRender;

        Matrix4x4 preservedLocal = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(localPosition);
        return preservedLocal * parentRender;
    }

    private bool TryResolveParentPose(SceneObject source, out Matrix4x4 parentRender)
    {
        parentRender = default;
        if (!uint.TryParse(source.ParentId, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint parentKey)) return false;
        SceneObject[] parents = worldObjects.Where(item => item.GaoKey == parentKey).Take(2).ToArray();
        return parents.Length == 1 && TryResolveWorldPose(parents[0], out parentRender);
    }

    private static bool HasParent(SceneObject sceneObject) => !string.IsNullOrWhiteSpace(sceneObject.ParentId);

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
