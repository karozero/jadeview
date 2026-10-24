using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteCinematicObjectAnimations(WorldData world, List<SceneObject> objects)
    {
        foreach (var clip in SelectedCinematicClips(world, options))
        {
            var tracks = CinematicTracks(clip, world);
            var sceneObjectTracks = CinematicHierarchyTransformSampler.Index(tracks);
            var orientTo = CinematicOrientTo(clip, world);
            var sightSet = CinematicSightSets(clip, world);
            var localRotations = CinematicLocalRotations(clip, world);
            var gameScriptMoves = CinematicGameScriptMoves(clip, world);
            var scriptHierarchyCommands = ScriptHierarchyCommands(clip, world).ToArray();
            foreach (string diagnostic in orientTo.Diagnostics)
                warnings.Add($"Cinematic '{clip.Name}': {diagnostic}");
            foreach (string diagnostic in sightSet.Diagnostics)
                warnings.Add($"Cinematic '{clip.Name}': {diagnostic}");
            foreach (string diagnostic in localRotations.Diagnostics)
                warnings.Add($"Cinematic '{clip.Name}': {diagnostic}");
            foreach (string diagnostic in gameScriptMoves.Diagnostics)
                warnings.Add($"Cinematic '{clip.Name}': {diagnostic}");
            var state = new CinematicObjectState(clip, world, world.Actors,
                NestedCinematicEvents(clip, world).Commands);
            var times = AnimationExportSampling.Times(clip, tracks).Concat(state.VisibilityChangeTimes
                    .SelectMany(time => time > 0 && time <= clip.Duration
                        ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Concat(orientTo.ChangeTimes.SelectMany(time => time > 0 && time <= clip.Duration
                    ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Concat(sightSet.ChangeTimes.SelectMany(time => time > 0 && time <= clip.Duration
                    ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Concat(localRotations.ChangeTimes)
                .Concat(gameScriptMoves.ChangeTimes)
                .Concat(scriptHierarchyCommands.Select(command => command.Time).Distinct()
                    .SelectMany(time => time > 0 && time <= clip.Duration
                        ? new[] { MathF.BitDecrement(time), time } : new[] { time }))
                .Where(time => time >= 0 && time <= clip.Duration).Distinct().Order().ToArray();
            if (times.Length == 0) continue;
            var visibility = SampleCinematicVisibility(state, world, objects, times);
            var orientation = SampleCinematicOrientations(orientTo, sightSet, world, objects,
                tracks, sceneObjectTracks, times);
            var scriptHierarchy = SampleScriptHierarchyWorldMatrices(clip, world, times,
                scriptHierarchyCommands);
            if (scriptHierarchyCommands.Length > 0 && scriptHierarchy == null)
                warnings.Add($"Cinematic '{clip.Name}': script-driven hierarchy changes are present, but no isolated playback context is available; their transform animation is not sampled.");
            int input = Floats(times, 1, "SCALAR", true);
            var channels = new List<Obj>();
            var samplers = new List<Obj>();
            foreach (var sceneObject in objects.Where(item => item.Actor == null
                && nodeByObject.ContainsKey(item) && animatedCinematicObjects.Contains(item)))
            {
                var objectTracks = tracks.Where(track => track.Bone < 0 && track.Target == sceneObject.GaoKey).ToArray();
                bool hasOrientTo = orientation.ContainsKey(sceneObject);
                bool hasSightSet = sightSet.Participants.Contains(sceneObject);
                bool hasLocalRotation = localRotations.Targets.Contains(sceneObject);
                bool hasScriptMove = gameScriptMoves.Targets.Contains(sceneObject);
                bool hasScriptHierarchy = scriptHierarchy?.ContainsKey(sceneObject.GaoKey) == true;
                if (objectTracks.Length == 0 && !visibility.ContainsKey(sceneObject)
                    && !hasOrientTo && !hasSightSet && !hasLocalRotation && !hasScriptMove
                    && !hasScriptHierarchy) continue;
                CinematicCameraBinding? cameraBinding = null;
                if (!sourceCameraObjects.Contains(sceneObject)
                    && sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
                    cameraBinding = CinematicCameraBinding.Create(sceneObject, objects, clip);
                var values = SampleCinematicObject(sceneObject, objectTracks, times, cameraBinding, clip,
                    tracks, sceneObjectTracks, world, visibility.GetValueOrDefault(sceneObject),
                    orientation.GetValueOrDefault(sceneObject), localRotations, gameScriptMoves,
                    scriptHierarchy?.GetValueOrDefault(sceneObject.GaoKey));
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

    private static Dictionary<SceneObject, Matrix4x4[]> SampleCinematicOrientations(
        CinematicOrientToTimeline orientTo, CinematicSightSetTimeline sightSet,
        WorldData world, List<SceneObject> objects,
        AnimationTrack[] tracks, IReadOnlyDictionary<uint, AnimationTrack[]> sceneObjectTracks,
        float[] times)
    {
        var targets = orientTo.OrientedObjects.Concat(sightSet.Participants)
            .Where(objects.Contains).Distinct().ToArray();
        if (targets.Length == 0) return [];

        var samples = targets.ToDictionary(target => target, _ => new Matrix4x4[times.Length]);
        var snapshots = world.Objects.Select(target => (Target: target, target.PreviewMatrix)).ToArray();
        try
        {
            foreach (int sampleIndex in Enumerable.Range(0, times.Length))
            {
                float time = times[sampleIndex];
                foreach (var sceneObject in world.Objects.Where(sceneObject => sceneObject.Actor == null))
                {
                    sceneObject.PreviewMatrix = AnimationMath.FromRender(
                        CinematicHierarchyTransformSampler.EvaluateWorldRender(sceneObject,
                            sceneObjectTracks, time, world.Objects));
                }

                orientTo.Evaluate(time, sightSet);
                sightSet.Evaluate(time, orientTo);
                foreach (var target in targets)
                    samples[target][sampleIndex] = target.PreviewMatrix
                        ?? AnimationMath.Trs(target.Transform);
            }
        }
        finally
        {
            foreach (var snapshot in snapshots) snapshot.Target.PreviewMatrix = snapshot.PreviewMatrix;
        }
        return samples;
    }

    private static Dictionary<SceneObject, bool[]> SampleCinematicVisibility(CinematicObjectState state,
        WorldData world, List<SceneObject> objects, float[] times)
    {
        var targets = state.VisibilityTargets.Where(objects.Contains).ToArray();
        var sampled = targets.ToDictionary(target => target, _ => new bool[times.Length]);
        var snapshots = world.Objects.Select(target => (Target: target, target.PreviewControlFlags,
            target.PreviewCapabilities, target.CinematicExcluded)).ToArray();
        try
        {
            for (int sampleIndex = 0; sampleIndex < times.Length; sampleIndex++)
            {
                state.Evaluate(times[sampleIndex]);
                foreach (var target in targets)
                    sampled[target][sampleIndex] = target.IsControlHidden;
            }
        }
        finally
        {
            foreach (var snapshot in snapshots)
            {
                snapshot.Target.PreviewControlFlags = snapshot.PreviewControlFlags;
                snapshot.Target.PreviewCapabilities = snapshot.PreviewCapabilities;
                snapshot.Target.CinematicExcluded = snapshot.CinematicExcluded;
            }
        }
        return sampled;
    }

    private static (List<float> Translation, List<float> Rotation, List<float> Scale) SampleCinematicObject(
        SceneObject sceneObject, AnimationTrack[] tracks, float[] times,
        CinematicCameraBinding? cameraBinding, AnimationClip clip, AnimationTrack[] sourceTracks,
        IReadOnlyDictionary<uint, AnimationTrack[]> sceneObjectTracks, WorldData world,
        bool[]? hiddenSamples, Matrix4x4[]? orientedSamples,
        CinematicLocalRotationTimeline localRotations,
        CinematicGameScriptMoveTimeline gameScriptMoves,
        Matrix4x4?[]? scriptHierarchySamples)
    {
        var translation = new List<float>(times.Length * 3);
        var rotation = new List<float>(times.Length * 4);
        var scale = new List<float>(times.Length * 3);
        using var camera = cameraBinding == null ? null
            : new CinematicCameraExportSampler(cameraBinding, clip, sourceTracks);
        foreach (float time in times)
        {
            var jadePose = orientedSamples?[translation.Count / 3]
                ?? AnimationMath.FromRender(CinematicHierarchyTransformSampler.EvaluateWorldRender(
                    sceneObject, sceneObjectTracks, time, world.Objects, tracks));
            jadePose = localRotations.EvaluatePose(sceneObject, jadePose, time);
            jadePose = gameScriptMoves.Apply(sceneObject, jadePose, time);
            var renderPose = scriptHierarchySamples?[translation.Count / 3]
                ?? AnimationMath.ToRender(jadePose);
            if (camera != null) renderPose = camera.Evaluate(time);
            var (sampleScale, sampleRotation, samplePosition) = Decompose(renderPose);
            if (hiddenSamples?[translation.Count / 3] == true) sampleScale = Vector3.Zero;
            translation.AddRange([samplePosition.X, samplePosition.Y, samplePosition.Z]);
            rotation.AddRange([sampleRotation.X, sampleRotation.Y, sampleRotation.Z, sampleRotation.W]);
            scale.AddRange([sampleScale.X, sampleScale.Y, sampleScale.Z]);
        }
        return (translation, rotation, scale);
    }

    private Dictionary<uint, Matrix4x4?[]>? SampleScriptHierarchyWorldMatrices(AnimationClip clip,
        WorldData world, float[] times, CinematicScriptHierarchyCommand[] commands)
    {
        if (commands.Length == 0) return null;
        if (options.CinematicPlayback is { } activePlayback
            && activePlayback.Key == clip.Key && activePlayback.WorldKey == world.Key)
            return activePlayback.SampleScriptHierarchyWorldMatrices(times);
        if (options.CinematicPlayback is not { } currentPlayback
            || options.CinematicDocument is not { } document)
            return null;

        CinematicPlayback? clipPlayback = CinematicPlayback.DiscoverClip(clip, document, world, resolver,
            options.DisabledCinematicCallbackOwners, options.PreviewedCinematicSceneEventOwners,
            options.PreviewedCinematicSceneEventStartTimes);
        if (clipPlayback == null) return null;

        float currentTime = currentPlayback.Time;
        try
        {
            clipPlayback.Activate();
            return clipPlayback.SampleScriptHierarchyWorldMatrices(times);
        }
        finally
        {
            try { clipPlayback.Deactivate(); }
            finally
            {
                currentPlayback.Activate();
                currentPlayback.Seek(currentTime);
            }
        }
    }
}
