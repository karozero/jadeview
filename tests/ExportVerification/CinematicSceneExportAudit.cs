using System.Numerics;
using JadeView;

internal static class CinematicSceneExportAudit
{
    private const uint PreferredClipKey = 0xBF00218E;

    internal static CinematicSceneExportResult Run(Archive archive, Entry entry,
        SceneDocument document, string outputDirectory, uint? requestedClipKey = null)
    {
        var world = AssembleCinematicWorld(document, requestedClipKey);
        if (world == null) return CinematicSceneExportResult.Empty;

        uint clipKey = requestedClipKey ?? SelectClipKey(world);
        if (clipKey == 0) return CinematicSceneExportResult.Empty;
        SelectClip(world, clipKey);

        var resolver = LoadResources(archive, entry, document);
        var playback = CinematicPlayback.Discover(document, world, resolver)
            .SingleOrDefault(candidate => candidate.Key == clipKey)
            ?? throw new InvalidDataException($"No synchronized playback was resolved for {clipKey:X8}.");
        playback.Activate();
        var facialTimelines = playback.SampleFacialMorphTimelines();
        var bodyBindings = world.Actors.Where(actor => actor.Clip?.CinematicBody != null).Select(actor => new
        {
            Actor = actor.Root.Name,
            Actions = actor.Clip!.CinematicBody!.ActionCount,
            actor.Clip.CinematicBody.FirstActionTime,
            Diagnostics = actor.Clip.CinematicBody.Diagnostics,
            AnimatedBones = CountAnimatedBones(actor)
        }).ToArray();
        var morphBindings = CollectMorphBindings(world, resolver);
        string exportPath = Path.Combine(outputDirectory, $"{entry.Id}-cinematic-synchronized.glb");

        ExportCutscene(document, world, resolver, facialTimelines, exportPath, clipKey, playback);
        var exportedClip = document.CinematicClips[clipKey];
        var nestedEventTimeline = CinematicNestedEventTransformTracks.ReadTimeline(exportedClip, world, document);
        VerifyExportContainsNestedEventTracks(exportPath, exportedClip, world,
            nestedEventTimeline.TransformTracks);
        VerifyExportContainsVisibilityTransitions(exportPath, exportedClip, playback);
        if (HasCameraAnimationSource(world, clipKey))
            VerifyExportContainsCameraTrack(exportPath, clipKey);

        var populatedTimelines = facialTimelines.Where(IsNonNeutral).ToArray();
        VerifyExportContainsFacialTracks(exportPath, populatedTimelines.Length);
        return new(exportPath, populatedTimelines.Length,
            populatedTimelines.Sum(pair => pair.Value.Length), morphBindings, bodyBindings);
    }

    private static void VerifyExportContainsNestedEventTracks(string path, AnimationClip clip,
        WorldData world, AnimationTrack[] nestedTracks)
    {
        if (nestedTracks.Length == 0) return;
        using var glb = new GlbReader(path);
        var nodes = glb.Root.GetProperty("nodes");
        var cinematicAnimation = glb.Root.GetProperty("animations").EnumerateArray()
            .FirstOrDefault(animation => animation.GetProperty("name").GetString() == clip.Name);
        if (cinematicAnimation.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidDataException($"GLB export for {clip.Key:X8} omitted its synchronized animation.");

        var channels = cinematicAnimation.GetProperty("channels").EnumerateArray().ToArray();
        foreach (uint targetKey in nestedTracks.Select(track => track.Target).Distinct())
        {
            var target = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == targetKey)
                ?? throw new InvalidDataException($"Nested event target {targetKey:X8} is absent from the assembled world.");
            var targetNodes = Enumerable.Range(0, nodes.GetArrayLength())
                .Where(index => nodes[index].TryGetProperty("name", out var name)
                    && name.GetString() == target.Name).ToHashSet();
            bool hasAnimationChannel = channels.Any(channel =>
                channel.GetProperty("target").TryGetProperty("node", out var node)
                && targetNodes.Contains(node.GetInt32()));
            if (targetNodes.Count == 0 || !hasAnimationChannel)
                throw new InvalidDataException(
                    $"GLB export for {clip.Key:X8} omitted nested transform channels for {target.Name} ({targetKey:X8}).");
        }
    }

    private static void VerifyExportContainsVisibilityTransitions(string path, AnimationClip clip,
        CinematicPlayback playback)
    {
        SceneObject[] targets = playback.VisibilityTargets
            .Where(target => !SceneRenderer.IsCollision(target))
            .Distinct().ToArray();
        if (targets.Length == 0) return;
        using var glb = new GlbReader(path);
        var nodes = glb.Root.GetProperty("nodes");
        var cinematicAnimation = glb.Root.GetProperty("animations").EnumerateArray()
            .FirstOrDefault(animation => animation.GetProperty("name").GetString() == clip.Name);
        if (cinematicAnimation.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidDataException($"GLB export for {clip.Key:X8} omitted its synchronized animation.");
        var channels = cinematicAnimation.GetProperty("channels").EnumerateArray().ToArray();
        var targetNodesByObject = new Dictionary<SceneObject, int>();
        foreach (var target in targets)
        {
            var targetNodes = Enumerable.Range(0, nodes.GetArrayLength())
                .Where(index => ExportVisibilityNodeMatches(nodes[index], target)).ToArray();
            int animatedNode = targetNodes.FirstOrDefault(index => channels.Any(channel =>
                channel.GetProperty("target").TryGetProperty("node", out var node)
                && node.GetInt32() == index), -1);
            if (animatedNode < 0)
                throw new InvalidDataException(
                    $"GLB export for {clip.Key:X8} omitted visibility animation for {target.Name} ({target.GaoKey:X8}).");
            targetNodesByObject[target] = animatedNode;
        }

        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index]
                .GetProperty("name").GetString() == clip.Name);
        float restoreTime = playback.Time;
        var sampleTimes = playback.VisibilityChangeTimes.Append(0)
            .Where(time => time >= 0 && time <= clip.Duration).Distinct().Order().ToArray();
        try
        {
            foreach (float time in sampleTimes)
            {
                playback.Seek(time);
                foreach (var target in targets)
                {
                    Matrix4x4 exportedPose = glb.World(targetNodesByObject[target], animationIndex, time);
                    bool exportedHidden = MathF.Abs(exportedPose.M11) + MathF.Abs(exportedPose.M12)
                        + MathF.Abs(exportedPose.M13) + MathF.Abs(exportedPose.M21)
                        + MathF.Abs(exportedPose.M22) + MathF.Abs(exportedPose.M23)
                        + MathF.Abs(exportedPose.M31) + MathF.Abs(exportedPose.M32)
                        + MathF.Abs(exportedPose.M33) < .0001f;
                    bool previewHidden = target.IsControlHidden
                        || (target.EffectiveDrawFlags & SceneObject.DrawVisibilityMask) == 0;
                    if (exportedHidden != previewHidden)
                        throw new InvalidDataException(
                            $"GLB export for {clip.Key:X8} has incorrect visibility for {target.Name} "
                            + $"({target.GaoKey:X8}) at {time:F3}s.");
                }
            }
        }
        finally { playback.Seek(restoreTime); }
    }

    private static bool ExportVisibilityNodeMatches(System.Text.Json.JsonElement node, SceneObject target)
    {
        if (target.Actor == null)
            return node.TryGetProperty("name", out var name) && name.GetString() == target.Name;
        if (!node.TryGetProperty("extras", out var extras)
            || !extras.TryGetProperty("gaoKey", out var key)
            || key.GetString() != target.GaoKey.ToString("X8")) return false;
        return node.TryGetProperty("mesh", out _)
            || extras.TryGetProperty("actorComponentVisual", out var visual) && visual.GetBoolean()
            || extras.TryGetProperty("actorVisibility", out var actorVisibility) && actorVisibility.GetBoolean();
    }

    private static int CountAnimatedBones(SceneActor actor)
    {
        float restoreTime = actor.Time;
        var poses = new Dictionary<SceneObject, System.Numerics.Matrix4x4>();
        var moving = new HashSet<SceneObject>();
        for (float time = 0; time < actor.Clip!.Duration; time += .5f)
        {
            actor.Time = time;
            actor.Evaluate(0);
            System.Numerics.Matrix4x4.Invert(actor.Root.PreviewMatrix!.Value, out var inverseRoot);
            foreach (var bone in actor.Bones.OfType<SceneObject>())
            {
                var pose = bone.PreviewMatrix!.Value * inverseRoot;
                if (poses.TryGetValue(bone, out var previous) && PoseChanged(previous, pose)) moving.Add(bone);
                poses[bone] = pose;
            }
        }
        actor.Time = restoreTime;
        actor.Evaluate(0);
        return moving.Count;
    }

    private static bool PoseChanged(System.Numerics.Matrix4x4 previous, System.Numerics.Matrix4x4 current)
    {
        System.Numerics.Matrix4x4.Decompose(previous, out var previousScale, out var previousRotation, out var previousPosition);
        System.Numerics.Matrix4x4.Decompose(current, out var scale, out var rotation, out var position);
        return System.Numerics.Vector3.Distance(previousPosition, position) > .0001f
            || System.Numerics.Vector3.Distance(previousScale, scale) > .0001f
            || 1 - MathF.Abs(System.Numerics.Quaternion.Dot(previousRotation, rotation)) > .00001f;
    }

    private static WorldData? AssembleCinematicWorld(SceneDocument document, uint? clipKey)
    {
        var cinematicWorld = clipKey.HasValue
            ? document.Worlds.FirstOrDefault(world => world.Key == document.CinematicClips[clipKey.Value].CinematicWorldKey)
            : document.Worlds.FirstOrDefault(world => world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase));
        return cinematicWorld == null ? null : CinematicWorldAssembler.Assemble(document, cinematicWorld);
    }

    private static uint SelectClipKey(WorldData world)
    {
        if (world.CinematicClips.ContainsKey(PreferredClipKey)) return PreferredClipKey;
        return world.CinematicClips.Keys.FirstOrDefault(key =>
            world.Actors.Count(actor => actor.Clips.Any(clip => clip.Cinematic && clip.Key == key)) > 1);
    }

    private static void SelectClip(WorldData world, uint clipKey)
    {
        foreach (var actor in world.Actors)
        {
            var matchingClip = actor.Clips.FirstOrDefault(clip => clip.Cinematic && clip.Key == clipKey);
            if (matchingClip != null) actor.SelectClip(matchingClip);
        }
    }

    private static ResourceResolver LoadResources(Archive archive, Entry entry, SceneDocument document)
    {
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        return resolver;
    }

    private static Dictionary<SceneObject, FaceMorphKeyframe[]> SampleFacialTimelines(
        SceneDocument document, WorldData world, ResourceResolver resolver, uint clipKey)
    {
        var playback = CinematicPlayback.Discover(document, world, resolver)
            .FirstOrDefault(candidate => candidate.Key == clipKey);
        if (playback == null) return [];
        playback.Activate();
        VerifyActorWorldPositions(world, playback.ScriptStateCalls);
        return playback.SampleFacialMorphTimelines();
    }

    private static void VerifyActorWorldPositions(WorldData world,
        IEnumerable<(float Time, CinematicLiteralCall Call)> scriptCalls)
    {
        foreach (var actor in world.Actors.Where(actor => actor.Clip?.Cinematic == true))
        {
            AnimationClip clip = actor.Clip!;
            Vector3? sourcePosition = clip.CinematicBody?.RootPose(0).Translation;
            sourcePosition ??= CinematicScriptTransformTracks.Read(clip, scriptCalls)
                .Where(track => track.Target == actor.Root.GaoKey)
                .SelectMany(track => track.Keys)
                .Where(key => key.Time == 0 && key.Position != null)
                .Select(key => key.Position).LastOrDefault();
            sourcePosition ??= clip.Tracks
                .Where(track => track.Bone < 0 && track.Target == actor.Root.GaoKey)
                .SelectMany(track => track.Keys)
                .FirstOrDefault(key => key.Time == 0 && key.Position != null)?.Position;
            if (sourcePosition == null) continue;
            var actual = actor.Root.PreviewMatrix!.Value.Translation;
            if (System.Numerics.Vector3.Distance(actual, sourcePosition.Value) > .001f)
                throw new InvalidDataException($"Cinematic actor {actor.Root.Name} lost its source root position "
                    + $"at 0s (expected {sourcePosition.Value}, got {actual}).");
        }
    }

    private static object CollectMorphBindings(WorldData world, ResourceResolver resolver) =>
        world.Actors.SelectMany(actor => actor.Members
            .Where(actor.HasMorphAppearance)
            .Select(member => CreateMorphBinding(actor, member, resolver)))
            .ToArray();

    private static object CreateMorphBinding(SceneActor actor, SceneObject member, ResourceResolver resolver)
    {
        actor.ResolveMorphAppearance(member, candidate => CountExpressions(candidate, resolver));
        resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var mesh);
        var preview = mesh != null ? FaceMorphPreview.Get(member, mesh) : null;
        return new
        {
            Actor = actor.Root.Name,
            Member = member.Name,
            Geometry = member.RenderGeometryKey.ToString("X8"),
            Groups = preview?.Data?.Groups.Length ?? 0,
            Channels = preview?.Data?.Channels.Length ?? 0,
            MouthCycle = preview?.MouthCycleDefinition != null,
            ControllerBinding = preview?.ControllerBindingDiagnostic,
            ChannelSlots = preview?.Data?.Channels.Select(channel => channel.GroupReferences.Length).ToArray(),
            ChannelDefaults = preview?.Data?.Channels.Select((channel, index) => new
            {
                Channel = index,
                channel.Weight,
                channel.Progression
            }).ToArray(),
            MorphGroupMotion = preview?.Data?.Groups.Select(group => new
            {
                group.Index,
                Vertices = group.AccumulatedDeltas().Count
            }).ToArray(),
            Type2FormMotion = preview?.Data?.Groups
                .Where(group => group.Index is >= 6 and <= 9)
                .Select(group => new { group.Index, Vertices = group.AccumulatedDeltas().Count })
                .ToArray(),
            ModifierCount = member.Modifiers.Count(modifier => modifier.Type == 4)
        };
    }

    private static int CountExpressions(SceneObject candidate, ResourceResolver resolver) =>
        resolver.Geometries.TryGetValue(candidate.RenderGeometryKey, out var geometry)
            ? FaceMorphPreview.Get(candidate, geometry)?.Expressions.Count ?? 0
            : 0;

    private static bool IsNonNeutral(KeyValuePair<SceneObject, FaceMorphKeyframe[]> timeline) =>
        timeline.Value.Length > 1
        && timeline.Value.Any(frame => frame.Weights.Any(weight => MathF.Abs(weight) > 1e-5f));

    private static void ExportCutscene(SceneDocument document, WorldData world, ResourceResolver resolver,
        IReadOnlyDictionary<SceneObject, FaceMorphKeyframe[]> facialTimelines, string path, uint clipKey,
        CinematicPlayback playback)
    {
        SceneExporter.Export(path, world, resolver, new()
        {
            Textures = false,
            Actors = true,
            UnknownObjects = true,
            GameHidden = true,
            Animations = true,
            AllClips = false,
            CinematicClipKey = clipKey,
            CinematicDocument = document,
            CinematicPlayback = playback,
            MorphAnimations = true,
            CinematicMorphTimelines = facialTimelines
        });
    }

    private static void VerifyExportContainsCameraTrack(string path, uint clipKey)
    {
        using var glb = new GlbReader(path);
        var nodes = glb.Root.GetProperty("nodes");
        var cameraNodes = Enumerable.Range(0, nodes.GetArrayLength())
            .Where(index => nodes[index].TryGetProperty("name", out var name)
                && name.GetString()?.Contains("_PJ_Camera", StringComparison.OrdinalIgnoreCase) == true)
            .ToHashSet();
        var matchingAnimations = glb.Root.GetProperty("animations").EnumerateArray()
            .Where(animation => animation.GetProperty("name").GetString()?.Contains(
                clipKey.ToString("X8"), StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        bool hasCameraChannel = matchingAnimations
            .SelectMany(animation => animation.GetProperty("channels").EnumerateArray())
            .Any(channel => channel.GetProperty("target").TryGetProperty("node", out var node)
                && cameraNodes.Contains(node.GetInt32()));
        if (!hasCameraChannel)
            throw new InvalidDataException($"GLB export for {clipKey:X8} omitted its source camera animation.");

        bool hasCameraObject = cameraNodes.Any(index => nodes[index].TryGetProperty("camera", out _));
        if (!hasCameraObject || !glb.Root.TryGetProperty("cameras", out var cameras)
            || cameras.GetArrayLength() == 0)
            throw new InvalidDataException($"GLB export for {clipKey:X8} omitted the source camera object.");
    }

    private static bool HasCameraAnimationSource(WorldData world, uint clipKey)
    {
        if (!world.CinematicClips.TryGetValue(clipKey, out var clip)) return false;
        if (CinematicCameraEvent.Read(clip).Length > 0) return true;
        var cameraKeys = world.Objects
            .Where(sceneObject => sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
            .Select(sceneObject => sceneObject.GaoKey)
            .ToHashSet();
        return clip.Tracks.Any(track => cameraKeys.Contains(track.Target) && track.Keys.Count > 0);
    }

    private static void VerifyExportContainsFacialTracks(string path, int expectedTracks)
    {
        if (expectedTracks == 0) return;
        using var glb = new GlbReader(path);
        var cinematicAnimations = glb.Root.GetProperty("animations").EnumerateArray()
            .Where(animation => animation.TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("cinematicClipKey", out _)).ToArray();
        if (cinematicAnimations.Length != 1)
            throw new InvalidDataException("Selected cutscene must export as one synchronized animation.");
        var animation = cinematicAnimations[0];
        var channels = animation.GetProperty("channels").EnumerateArray().ToArray();
        int facialTracks = channels.Count(channel =>
            channel.GetProperty("target").GetProperty("path").GetString() == "weights");
        int samplerCount = animation.GetProperty("samplers").GetArrayLength();
        if (channels.Any(channel => channel.GetProperty("sampler").GetInt32() >= samplerCount))
            throw new InvalidDataException("Combined cinematic channel references an invalid sampler.");
        int uniqueTargets = channels.Select(channel => channel.GetProperty("target").GetRawText()).Distinct().Count();
        if (uniqueTargets != channels.Length)
            throw new InvalidDataException("Combined cinematic animation has conflicting target channels.");
        if (facialTracks < expectedTracks)
            throw new InvalidDataException($"GLB export has {facialTracks} facial tracks; expected {expectedTracks}.");
    }
}

internal sealed record CinematicSceneExportResult(
    string? Path, int MorphMeshes, int MorphKeyframes, object MorphBindings, object BodyBindings)
{
    internal static CinematicSceneExportResult Empty { get; } = new(null, 0, 0, Array.Empty<object>(), Array.Empty<object>());
}
