using System.Numerics;

namespace JadeView;

internal sealed class CinematicSightSetTimeline
{
    private readonly SceneObject[] worldObjects;
    private readonly HashSet<SceneObject> animatedObjects;
    private readonly Dictionary<uint, float[]> authoredRotationTimesByTarget;
    private readonly Dictionary<SceneObject, SightCommand[]> commandsByTarget;
    private readonly string[] diagnostics;

    internal IReadOnlyCollection<SceneObject> Participants => commandsByTarget.Keys;
    internal bool HasCommands => commandsByTarget.Count > 0;
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    internal IEnumerable<float> ChangeTimes => commandsByTarget.Values
        .SelectMany(commands => commands.Select(command => command.Time)).Distinct();

    internal CinematicSightSetTimeline(WorldData world,
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls,
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
        var resolved = new List<SightCommand>();

        foreach (var (item, order) in calls.Select((item, order) => (item, order))
                     .OrderBy(item => item.item.Time).ThenBy(item => item.order))
        {
            CinematicLiteralCall call = item.Call;
            if (CinematicEngineFunctionCatalog.NameFor(call.Selector) != "OBJ_SightSet") continue;
            if (call.Arguments.Length != 3)
            {
                unresolved.Add($"OBJ_SightSet at {item.Time:F3}s on {call.Target:X8}: expected one 3D sight vector.");
                continue;
            }

            Vector3 sight = ReadVector(call.Arguments);
            if (!IsValidSight(sight))
            {
                unresolved.Add($"OBJ_SightSet at {item.Time:F3}s on {call.Target:X8}: sight vector is zero or invalid.");
                continue;
            }
            if (!TryUniqueObject(objectsByKey, call.Target, out SceneObject? target))
            {
                unresolved.Add($"OBJ_SightSet at {item.Time:F3}s on {call.Target:X8}: receiver GAO is missing or ambiguous.");
                continue;
            }
            if (!float.IsFinite(item.Time)) continue;
            resolved.Add(new(item.Time, order, target!, sight));
        }

        commandsByTarget = resolved.GroupBy(command => command.Target)
            .ToDictionary(group => group.Key, group => group.OrderBy(command => command.Time)
                .ThenBy(command => command.Order).ToArray());
        diagnostics = unresolved.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal void Evaluate(float time) => Evaluate(time, null);

    internal bool HasCommandAfter(SceneObject target, float afterTime, float throughTime) =>
        commandsByTarget.TryGetValue(target, out SightCommand[]? targetCommands)
            && targetCommands.Any(command => command.Time > afterTime && command.Time <= throughTime);

    internal void Evaluate(float time, CinematicOrientToTimeline? orientTo)
    {
        foreach (var (target, targetCommands) in commandsByTarget)
        {
            int commandIndex = FindLastAtOrBefore(targetCommands, time);
            if (commandIndex < 0) continue;
            SightCommand command = targetCommands[commandIndex];
            if (orientTo?.HasCommandAfter(target, command.Time, time) == true) continue;
            EvaluateTarget(target, command, time);
        }
    }

    private void EvaluateTarget(SceneObject target, SightCommand command, float time)
    {
        if (!TryResolveWorldPose(target, out Matrix4x4 sourceWorldRender)) return;
        Matrix4x4 sourceWorldJade = AnimationMath.FromRender(sourceWorldRender);
        if (HasLaterAuthoredRotation(command, time)
            || !TryOrientToSight(sourceWorldJade, command.Sight, out Matrix4x4 orientedWorldJade))
        {
            target.PreviewMatrix = AnimationMath.FromRender(sourceWorldRender);
            return;
        }

        Matrix4x4 orientedWorldRender = AnimationMath.ToRender(orientedWorldJade);
        orientedWorldRender = PreserveSerializedLocalPosition(target, sourceWorldRender, orientedWorldRender);
        target.PreviewMatrix = AnimationMath.FromRender(orientedWorldRender);
    }

    private static Vector3 ReadVector(uint[] arguments) => new(
        BitConverter.UInt32BitsToSingle(arguments[0]),
        BitConverter.UInt32BitsToSingle(arguments[1]),
        BitConverter.UInt32BitsToSingle(arguments[2]));

    private static bool IsValidSight(Vector3 sight) => IsFinite(sight)
        && float.IsFinite(sight.LengthSquared()) && sight.LengthSquared() > 1e-10f;

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool TryOrientToSight(Matrix4x4 sourcePose, Vector3 sight, out Matrix4x4 result)
    {
        result = sourcePose;
        Vector3 sightAxis = -Vector3.Normalize(sight);
        Vector3 bankingAxis = new(sourcePose.M31, sourcePose.M32, sourcePose.M33);
        if (!IsFinite(bankingAxis) || bankingAxis.LengthSquared() < 1e-10f) return false;
        bankingAxis = Vector3.Normalize(bankingAxis);

        Vector3 firstAxis = Vector3.Cross(-sightAxis, bankingAxis);
        if (!IsFinite(firstAxis) || firstAxis.LengthSquared() < 1e-12f) return false;
        firstAxis = Vector3.Normalize(firstAxis);
        Vector3 thirdAxis = Vector3.Normalize(Vector3.Cross(sightAxis, firstAxis));
        if (!IsFinite(thirdAxis)) return false;

        var rotationMatrix = new Matrix4x4(
            firstAxis.X, firstAxis.Y, firstAxis.Z, 0,
            sightAxis.X, sightAxis.Y, sightAxis.Z, 0,
            thirdAxis.X, thirdAxis.Y, thirdAxis.Z, 0,
            0, 0, 0, 1);
        if (!Matrix4x4.Decompose(sourcePose, out Vector3 scale, out _, out Vector3 position)) return false;
        result = Matrix4x4.CreateScale(scale) * rotationMatrix * Matrix4x4.CreateTranslation(position);
        return true;
    }

    private int FindLastAtOrBefore(SightCommand[] targetCommands, float time)
    {
        int low = 0;
        int high = targetCommands.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (targetCommands[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private bool HasLaterAuthoredRotation(SightCommand command, float time)
    {
        if (!authoredRotationTimesByTarget.TryGetValue(command.Target.GaoKey, out float[]? keyTimes))
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

    private Matrix4x4 PreserveSerializedLocalPosition(SceneObject target,
        Matrix4x4 currentWorldRender, Matrix4x4 orientedWorldRender)
    {
        if (!uint.TryParse(target.ParentId, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint parentKey))
            return orientedWorldRender;
        SceneObject[] parents = worldObjects.Where(item => item.GaoKey == parentKey).Take(2).ToArray();
        if (parents.Length != 1 || !TryResolveWorldPose(parents[0], out Matrix4x4 parentRender)
            || !Matrix4x4.Invert(parentRender, out Matrix4x4 inverseParent))
            return orientedWorldRender;

        Matrix4x4 currentLocalRender = currentWorldRender * inverseParent;
        Matrix4x4 orientedLocalRender = orientedWorldRender * inverseParent;
        if (!Matrix4x4.Decompose(currentLocalRender, out _, out _, out Vector3 localPosition)
            || !Matrix4x4.Decompose(orientedLocalRender, out Vector3 scale,
                out Quaternion rotation, out _))
            return orientedWorldRender;

        Matrix4x4 preservedLocal = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(localPosition);
        return preservedLocal * parentRender;
    }

    private static bool TryUniqueObject(IReadOnlyDictionary<uint, SceneObject[]> objectsByKey,
        uint key, out SceneObject? sceneObject)
    {
        sceneObject = null;
        if (key is 0 or uint.MaxValue || !objectsByKey.TryGetValue(key, out SceneObject[]? matches)
            || matches.Length != 1) return false;
        sceneObject = matches[0];
        return true;
    }

    private sealed record SightCommand(float Time, int Order, SceneObject Target, Vector3 Sight);
}
