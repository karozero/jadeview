using System.Globalization;
using System.Numerics;

namespace JadeView;

public sealed partial class CinematicPlayback
{
    private SceneObject[] scriptHierarchyPreviewObjects = [];
    private uint[] scriptHierarchySampleKeys = [];

    internal IReadOnlyCollection<uint> ScriptHierarchyAffectedKeys => scriptHierarchyAffectedKeys;

    /// <summary>Samples the same script-driven world matrices used by preview and restores the current playhead.</summary>
    internal Dictionary<uint, Matrix4x4?[]> SampleScriptHierarchyWorldMatrices(IReadOnlyList<float> times)
    {
        var samples = scriptHierarchyAffectedKeys.ToDictionary(key => key,
            _ => new Matrix4x4?[times.Count]);
        if (samples.Count == 0 || scriptHierarchyPoseFrames.Length == 0) return samples;

        float previousTime = Time;
        try
        {
            for (int sampleIndex = 0; sampleIndex < times.Count; sampleIndex++)
            {
                Seek(times[sampleIndex]);
                foreach (uint key in scriptHierarchyAffectedKeys)
                    if (scriptHierarchyUniqueObjects.TryGetValue(key, out SceneObject? target))
                        samples[key][sampleIndex] = target.CinematicScriptWorldMatrix;
            }
        }
        finally
        {
            Seek(previousTime);
        }
        return samples;
    }

    private void BuildScriptHierarchyPoseFrames()
    {
        ClearScriptHierarchyWorldMatrices();
        scriptHierarchyPoseCommands = ScriptStateCalls
            .Select(item => CinematicScriptHierarchyCommand.Decode(item.Time, item.Call))
            .OfType<CinematicScriptHierarchyCommand>()
            .OrderBy(command => command.Time)
            .ToArray();
        if (scriptHierarchyPoseCommands.Length == 0)
        {
            scriptHierarchyEndpointCounts = [];
            scriptHierarchyUniqueObjects = [];
            scriptHierarchySerializedParents = [];
            scriptHierarchySampleKeys = [];
            scriptHierarchyPreviewObjects = [];
            scriptHierarchyAffectedKeys = [];
            scriptHierarchyPoseFrames = [];
            scriptHierarchyPoseStatuses = new Dictionary<uint, CinematicScriptHierarchyPoseStatus>();
            return;
        }

        scriptHierarchyEndpointCounts = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.Count());
        scriptHierarchyUniqueObjects = world.Objects
            .Where(sceneObject => scriptHierarchyEndpointCounts.GetValueOrDefault(sceneObject.GaoKey) == 1)
            .ToDictionary(sceneObject => sceneObject.GaoKey);
        scriptHierarchySerializedParents = BuildScriptHierarchySerializedParents();

        scriptHierarchyAffectedKeys = CinematicScriptHierarchyPoseTimeline
            .FindAffectedKeys(scriptHierarchyPoseCommands, scriptHierarchySerializedParents)
            .Where(key => scriptHierarchyEndpointCounts.GetValueOrDefault(key) == 1)
            .Order()
            .ToArray();
        var sampleKeys = scriptHierarchyAffectedKeys.ToHashSet();
        AddSerializedHierarchyAncestors(sampleKeys);
        scriptHierarchySampleKeys = sampleKeys.Order().ToArray();
        scriptHierarchyPreviewObjects = scriptHierarchyAffectedKeys
            .Where(scriptHierarchyUniqueObjects.ContainsKey)
            .Select(key => scriptHierarchyUniqueObjects[key])
            .ToArray();

        float previousTime = Time;
        var frames = new List<CinematicScriptHierarchyPoseFrame>();
        try
        {
            foreach (float sampleTime in scriptHierarchyPoseCommands
                         .Select(command => command.Time).Distinct().Order())
            {
                Time = sampleTime;
                ClearScriptHierarchyWorldMatrices();
                EvaluateScriptHierarchySample();
                var matrices = scriptHierarchySampleKeys.ToDictionary(key => key,
                    key => scriptHierarchyUniqueObjects[key].RenderMatrix);
                frames.Add(new(sampleTime, matrices));
            }
        }
        finally
        {
            Time = previousTime;
            ClearScriptHierarchyWorldMatrices();
            EvaluateActorsBase();
        }

        scriptHierarchyPoseFrames = frames.ToArray();
    }

    private Dictionary<uint, CinematicScriptHierarchyParent> BuildScriptHierarchySerializedParents()
    {
        var animatedObjects = objectTracks.Select(track => track.Object).ToHashSet();
        var parents = new Dictionary<uint, CinematicScriptHierarchyParent>();
        foreach (SceneObject child in world.Objects)
        {
            if (scriptHierarchyEndpointCounts.GetValueOrDefault(child.GaoKey) != 1
                || child.Actor is not null || animatedObjects.Contains(child)
                || child.SerializedHierarchyLocalMatrix is not Matrix4x4 localJade
                || !TryReadParentKey(child, out uint parentKey)
                || scriptHierarchyEndpointCounts.GetValueOrDefault(parentKey) != 1)
                continue;

            Matrix4x4 localRender = AnimationMath.ToRender(localJade);
            if (IsFinite(localRender))
                parents[child.GaoKey] = new(parentKey, localRender);
        }
        return parents;
    }

    private void AddSerializedHierarchyAncestors(HashSet<uint> keys)
    {
        var pending = new Queue<uint>(keys);
        while (pending.TryDequeue(out uint childKey))
        {
            if (!scriptHierarchySerializedParents.TryGetValue(childKey,
                    out CinematicScriptHierarchyParent? parent)) continue;
            if (scriptHierarchyEndpointCounts.GetValueOrDefault(parent.ParentKey) != 1) continue;
            if (keys.Add(parent.ParentKey)) pending.Enqueue(parent.ParentKey);
        }
    }

    private void EvaluateScriptHierarchySample()
    {
        var sampleKeys = scriptHierarchySampleKeys.ToHashSet();
        foreach (var (actor, clip) in actorClips)
        {
            if (actor.Members.Any(member => sampleKeys.Contains(member.GaoKey))
                || sampleKeys.Contains(actor.Root.GaoKey))
            {
                actor.Clip = clip;
                actor.Playing = false;
                actor.Time = Math.Min(Time, clip.Duration);
                actor.Evaluate(0);
            }
        }

        foreach (var track in objectTracks)
            if (sampleKeys.Contains(track.Object.GaoKey))
                EvaluateObject(track.Object, track.Tracks, Time);

        eventTrackPreview.Evaluate(Time, world.Objects, sceneObjectTracksByTarget);
        orientTo.Evaluate(Time);
        localRotations.ResetTargets();
        localRotations.Evaluate(Time);
        gameScriptMoves.ResetTargets();
        gameScriptMoves.Evaluate(Time);
    }

    private void ApplyScriptHierarchyWorldMatrices()
    {
        if (scriptHierarchyPoseCommands.Length == 0 || scriptHierarchyPoseFrames.Length == 0) return;

        var currentBaseMatrices = scriptHierarchySampleKeys.ToDictionary(key => key,
            key => scriptHierarchyUniqueObjects[key].RenderMatrix);
        CinematicScriptHierarchyPoseResult result = CinematicScriptHierarchyPoseTimeline.Evaluate(
            scriptHierarchyPoseCommands, scriptHierarchySerializedParents,
            scriptHierarchyEndpointCounts, scriptHierarchyPoseFrames, currentBaseMatrices, Time,
            scriptHierarchyAffectedKeys);
        scriptHierarchyPoseStatuses = result.Statuses;

        foreach (SceneObject sceneObject in scriptHierarchyPreviewObjects)
            if (result.WorldMatrices.TryGetValue(sceneObject.GaoKey, out Matrix4x4 worldMatrix))
                sceneObject.CinematicScriptWorldMatrix = worldMatrix;
    }

    private void ClearScriptHierarchyWorldMatrices()
    {
        foreach (SceneObject sceneObject in scriptHierarchyPreviewObjects)
            sceneObject.CinematicScriptWorldMatrix = null;
    }

    private static bool TryReadParentKey(SceneObject sceneObject, out uint parentKey)
    {
        if (uint.TryParse(sceneObject.ParentId, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out parentKey)
            && parentKey is not 0 and not uint.MaxValue && parentKey != sceneObject.GaoKey)
            return true;
        parentKey = 0;
        return false;
    }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}
