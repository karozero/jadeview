using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class CinematicRootTrajectoryAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, uint actorKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var sourceClip = document.CinematicClips[clipKey];
        var sourceWorld = document.Worlds.Single(item => item.Key == sourceClip.CinematicWorldKey);
        var world = CinematicWorldAssembler.Assemble(document, sourceWorld);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        var playback = CinematicPlayback.Discover(document, world, resolver)
            .Single(item => item.Key == clipKey);
        var actor = playback.Actors.Single(item => item.Root.GaoKey == actorKey);
        AnimationTrack[] directRootTracks = sourceClip.Tracks
            .Where(track => track.Bone < 0 && track.Target == actorKey).ToArray();
        playback.Activate();
        var directActionRequests = sourceClip.Tracks
            .Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads
                .Select(payload => CinematicActionEvent.Decode(key, track.Target, payload))))
            .Where(action => action != null && action.Target == actorKey && action.Time <= 15)
            .Select(action => new
            {
                action!.Time,
                action.Selector,
                action.Action,
                action.KitOwner,
                action.LastAction,
                action.Frequency,
                action.StartFrame,
                action.Force,
                action.Release
            })
            .OrderBy(action => action.Time)
            .ToArray();
        var bodySegments = actor.Clip?.CinematicBody?.TimelineSegments(sourceClip.Duration).ToArray() ?? [];
        var bodyActionIds = bodySegments.Select(segment => segment.Label)
            .Where(label => label.StartsWith("Action ", StringComparison.Ordinal))
            .Select(label => int.TryParse(label.AsSpan("Action ".Length), out int actionId)
                ? actionId : -1)
            .Where(actionId => actionId >= 0);
        var requestedActionIds = directActionRequests.Select(action => action.Action)
            .Concat(bodyActionIds).ToHashSet();
        CinematicActionKitResolution actionKitResolution = CinematicActionKitScopeResolver.Resolve(
            actorKey, world.Key, document.CinematicActionKitsByOwner);
        object[] sourceActionKit = actionKitResolution.Actions?
            .Where(entry => requestedActionIds.Contains(entry.Key))
            .OrderBy(entry => entry.Key)
            .Select(entry => (object)new
            {
                Action = entry.Key,
                entry.Value.LoopItem,
                Items = entry.Value.Items.Select(item => new
                {
                    Clip = item.Clip.Key.ToString("X8"),
                    item.Frequency,
                    item.Repetitions,
                    item.Flags,
                    SpeedMode = item.Flags & 0x07,
                    item.DesignFlags,
                    item.CustomBits,
                    item.BlendFrames
                }).ToArray()
            }).ToArray() ?? [];

        float[] actionBoundaries = playback.BodyTimelines
            .Where(item => item.Actor == actor)
            .SelectMany(item => item.Body.TimelineSegments(sourceClip.Duration))
            .SelectMany(item => new[] { item.Start, item.End })
            .Where(time => float.IsFinite(time) && time >= 0 && time <= sourceClip.Duration)
            .Distinct().ToArray();
        var sampleTimes = new SortedSet<float>(actionBoundaries);
        for (float time = 0; time <= sourceClip.Duration; time += .25f) sampleTimes.Add(time);

        var samples = sampleTimes.Select(time =>
        {
            playback.Seek(time);
            Matrix4x4? pose = actor.Root.PreviewMatrix;
            Matrix4x4 rootMotion = actor.Clip?.CinematicBody?.RootPose(time) ?? Matrix4x4.Identity;
            Matrix4x4 directTrackPose = CinematicTransformSampler.Evaluate(actor.Root.Transform,
                directRootTracks, time);
            string? actionClipKey = null;
            float actionClipTime = 0;
            Vector3? actionRootTranslation = null;
            if (actor.Clip?.CinematicBody is { } body
                && body.TrySample(time, int.MaxValue, out var actionClip, out actionClipTime, out _))
            {
                actionClipKey = actionClip.Key.ToString("X8");
                actionRootTranslation = CinematicTransformSampler.EvaluateRoot(actionClip, actionClipTime).Translation;
            }
            var boneBounds = Bounds(actor.Bones.OfType<SceneObject>()
                .Select(member => member.PreviewMatrix is Matrix4x4 bonePose
                    ? bonePose.Translation : AnimationMath.Trs(member.Transform).Translation));
            var meshBounds = Bounds(actor.Members
                .Where(member => !member.PreviewHidden
                    && resolver.Geometries.TryGetValue(member.RenderGeometryKey, out _))
                .SelectMany(member =>
                {
                    Geometry geometry = resolver.Geometries[member.RenderGeometryKey];
                    Vector3[] positions = actor.Skin(member, geometry) ?? geometry.Positions;
                    Matrix4x4 renderMatrix = member.RenderMatrix;
                    return positions.Select(position => Vector3.Transform(position, renderMatrix));
                }));
            return new
            {
                Time = time,
                RootPreview = pose?.Translation,
                RootMotion = rootMotion.Translation,
                DirectTrackPose = directTrackPose.Translation,
                ActionClip = actionClipKey,
                ActionClipTime = actionClipTime,
                ActionRootTranslation = actionRootTranslation,
                RootRenderAxes = new
                {
                    X = Vector3.TransformNormal(Vector3.UnitX, actor.Root.RenderMatrix),
                    Y = Vector3.TransformNormal(Vector3.UnitY, actor.Root.RenderMatrix),
                    Z = Vector3.TransformNormal(Vector3.UnitZ, actor.Root.RenderMatrix)
                },
                BoneWorldBounds = boneBounds,
                SkinnedMeshWorldBounds = meshBounds,
                Rotation = pose is Matrix4x4 matrix
                    && Matrix4x4.Decompose(matrix, out _, out var rotation, out _)
                    ? rotation : Quaternion.Identity,
                Action = actor.Clip?.CinematicBody?.TimelineSegments(sourceClip.Duration)
                    .Where(segment => segment.Start <= time && segment.End >= time)
                    .OrderByDescending(segment => segment.Start).FirstOrDefault().Label
            };
        }).ToArray();

        var report = new
        {
            Container = entry.Id,
            Clip = clipKey.ToString("X8"),
            sourceClip.Duration,
            PlaybackWorldKey = world.Key.ToString("X8"),
            Actor = actor.Root.Name,
            ActorKey = actorKey.ToString("X8"),
            Initial = actor.Root.Transform.Translation,
            CollisionInstanceKey = actor.Root.CollisionInstanceKey.ToString("X8"),
            CollisionMapKey = actor.Root.CollisionMapKey.ToString("X8"),
            ActionKitSourceWorld = actionKitResolution.SourceWorldKey?.ToString("X8"),
            ActionKitResolutionFailure = actionKitResolution.Failure,
            ActionKitCandidateWorlds = document.CinematicActionKitsByOwner.Keys
                .Where(key => key.OwnerKey == actorKey).Select(key => key.WorldKey.ToString("X8")).ToArray(),
            BodyDiagnostics = actor.Clip?.CinematicBody?.Diagnostics,
            BodySegments = bodySegments,
            DirectActionRequests = directActionRequests,
            SourceActionKit = sourceActionKit,
            DirectRootTracks = directRootTracks
                .Select(track => new
                {
                    track.Target,
                    Keys = track.Keys.Where(key => key.Position != null || key.Rotation != null)
                        .Select(key => new { key.Time, key.Position, key.Rotation, key.Duration,
                            key.Flags, key.Format, key.Control1, key.Control2,
                            TimeCurve = key.TimeCurve?.Points }).ToArray()
                }).ToArray(),
            ScriptPositionTracks = CinematicScriptTransformTracks.Read(sourceClip, playback.ScriptCalls)
                .Where(track => track.Bone < 0 && track.Target == actorKey)
                .Select(track => new { track.Target, Keys = track.Keys
                    .Where(key => key.Position != null || key.Rotation != null)
                    .Select(key => new { key.Time, key.Position, key.Rotation, key.Duration }).ToArray() })
                .ToArray(),
            LiteralCalls = CinematicLiteralCall.ReadTimeline(sourceClip)
                .Where(item => item.Time <= 15 && item.Call.Target == actorKey)
                .Select(item => new { item.Time, item.Call.Selector, item.Call.Arguments })
                .ToArray(),
            Samples = samples
        };
        string path = Path.Combine(output, $"{container}-{clipKey:X8}-{actorKey:X8}-root-trajectory.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
        { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"ROOT TRAJECTORY AUDIT: actor={actor.Root.Name} ({actorKey:X8}) duration={sourceClip.Duration:F3}s -> {path}");
    }

    private static object Bounds(IEnumerable<Vector3> points)
    {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        bool found = false;
        foreach (Vector3 point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) continue;
            minimum = Vector3.Min(minimum, point);
            maximum = Vector3.Max(maximum, point);
            found = true;
        }
        return new
        {
            Minimum = found ? minimum : (Vector3?)null,
            Maximum = found ? maximum : (Vector3?)null,
            Center = found ? (minimum + maximum) * .5f : (Vector3?)null,
            Extents = found ? (maximum - minimum) * .5f : (Vector3?)null
        };
    }
}
