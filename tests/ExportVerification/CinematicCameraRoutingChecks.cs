using System.Numerics;
using JadeView;

internal static class CinematicCameraRoutingChecks
{
    internal static void Verify(string output)
    {
        var first = new SceneObject
        {
            GaoKey = 7,
            Name = "Position A",
            Transform = new()
            { Translation = new(10, 0, 0), Scale = new(2.5f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) }
        };
        var target = new SceneObject { GaoKey = 8, Name = "Target A", Transform = new() { Translation = new(10, 1, 0) } };
        var second = new SceneObject
        {
            GaoKey = 9,
            Name = "Position B",
            Transform = new()
            { Translation = new(20, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) }
        };
        var moving = new SceneObject { GaoKey = 10, Name = "Moving actor", Transform = new() { Translation = new(20, 0, 1) } };
        var motion = new AnimationTrack
        {
            Bone = -1,
            Target = 10,
            Keys =
            [new() { Duration = 4, Position = new(20, 0, 1) }, new() { Time = 4, Position = new(20, 0, 5) }]
        };
        var actorClip = new AnimationClip { Key = 100, Loaded = true, Cinematic = true, Duration = 4, Tracks = [motion] };
        var actor = new SceneActor { Root = moving, Members = [moving], Clip = actorClip, Clips = [actorClip], Time = .25f, Playing = true };
        moving.Actor = actor;
        actor.Evaluate(0);
        var clip = new AnimationClip
        {
            Key = 100,
            Name = "Routed camera fixture",
            Loaded = true,
            Cinematic = true,
            Duration = 4,
            Tracks = [new() { Bone = -1, Target = 99, Keys =
            [Command(0, 7, 8), Command(1, 7, 10), Command(2, 10, 8), Command(3, 9, 0),
             Command(3.5f, 7, 8, Vector3.UnitX, Vector3.UnitZ)] }, motion]
        };
        var world = new WorldData();
        world.Objects.AddRange([first, target, second, moving]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(clip.Key, clip);
        var repeated = new CinematicCameraRouting([
            new(0, 99, 7, 8, 1, 0, default, default),
            new(1, 99, 7, 8, 1, 2, Vector3.UnitX, default)], [first, target]);
        if (Vector3.Distance(repeated.Evaluate(1, first, item => item.RenderMatrix).Translation, new(10, 0, -1)) > .0001f)
            throw new InvalidDataException("A nonzero DYN transition speed incorrectly suppressed the authored camera offset.");
        var switchedWithOffsets = new CinematicCameraRouting([
            new(0, 99, 7, 8, 1, 0, default, default),
            new(1, 99, 9, 8, 1, 2, Vector3.UnitX, Vector3.UnitZ)], [first, target, second]);
        Matrix4x4 switchedPose = switchedWithOffsets.Evaluate(1, first, item => item.RenderMatrix);
        if (Vector3.Distance(switchedPose.Translation, new(19, 0, 0)) > .0001f
            || Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, switchedPose)),
                Vector3.Normalize(new Vector3(10, 1, -1) - new Vector3(19, 0, 0))) > .0001f)
            throw new InvalidDataException("A camera-reference switch with DYN transition speed lost its source-local camera or target offset.");
        var binding = CinematicCameraBinding.Discover(clip, world.Objects, [first, second]).Single();
        if (!binding.FromSourceEvent || binding.Source != first)
            throw new InvalidDataException("One native camera receiver produced separate per-marker cameras.");
        foreach (float time in new[] { .5f, 1.5f, 2.5f, 3.25f, 3.75f, 1.5f })
        {
            actor.Time = time; actor.Evaluate(0);
            VerifyPose(binding.RenderPose(time), first, target, second, moving, time);
        }
        actor.Time = .25f; actor.Evaluate(0);
        var previousPose = moving.PreviewMatrix;
        string path = Path.Combine(output, "camera-routing.glb");
        SceneExporter.Export(path, world, new(), new()
        { Actors = true, Animations = true, CinematicClipKey = clip.Key, UnknownObjects = true, Textures = false });
        if (actor.Time != .25f || actor.Clip != actorClip || !actor.Playing || moving.PreviewMatrix != previousPose)
            throw new InvalidDataException("Camera export changed its followed actor's preview state.");
        using var glb = new GlbReader(path);
        var nodes = glb.Root.GetProperty("nodes");
        int cameraNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index => nodes[index].TryGetProperty("camera", out _));
        foreach (float time in new[] { .5f, 1.5f, 2.5f, 3.25f, 3.75f })
        {
            actor.Time = time; actor.Evaluate(0);
            VerifyPose(glb.World(cameraNode, 0, time), first, target, second, moving, time);
        }
        VerifySharedSourceReceivers(output, world, clip);
        VerifyHierarchicalCameraPose();
        VerifyViewportCameraPoseReset();
        Console.WriteLine("PASS: source camera/target routing, actor references, no-target orientation, unscaled cut offsets, reverse seek and matching GLB poses");
    }

    private static void VerifyViewportCameraPoseReset()
    {
        using var renderer = new SceneRenderer();
        SceneRenderer.ViewportCameraState stockCamera = renderer.CaptureViewportCameraState();
        var sourceRotation = Quaternion.CreateFromYawPitchRoll(.7f, -.25f, .1f);
        var sourcePosition = new Vector3(12, 5, -8);
        var sourcePose = Matrix4x4.CreateFromQuaternion(sourceRotation)
            * Matrix4x4.CreateTranslation(sourcePosition);
        const float sourceFov = 62;
        if (!renderer.TrySetCameraPose(sourcePose, sourceFov))
            throw new InvalidDataException("A valid source camera pose could not be applied before detaching.");
        Vector3 expectedForward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, sourceRotation));
        Vector3 actualForward = Vector3.Normalize(renderer.Camera.Target - renderer.Camera.Position);
        if (Vector3.Distance(renderer.Camera.Position, sourcePosition) > .0001f
            || Vector3.Distance(actualForward, expectedForward) > .0001f
            || renderer.CinematicHorizontalFov != sourceFov)
            throw new InvalidDataException("Following the source camera did not apply its pose and lens.");

        renderer.Camera.Up = Vector3.Transform(Vector3.UnitY, sourceRotation);
        renderer.SynchronizeManualCameraOrientation();
        Vector3 manualForward = Vector3.Normalize(renderer.Camera.Target - renderer.Camera.Position);
        Vector3 trackedAnglesForward = new(MathF.Sin(renderer.Yaw) * MathF.Cos(renderer.Pitch),
            MathF.Sin(renderer.Pitch), MathF.Cos(renderer.Yaw) * MathF.Cos(renderer.Pitch));
        if (renderer.Camera.Up != Vector3.UnitY
            || Vector3.Distance(manualForward, trackedAnglesForward) > .0001f
            || MathF.Abs(Vector3.Distance(renderer.Camera.Position, renderer.Camera.Target) - 1) > .0001f)
            throw new InvalidDataException("Detaching from a rolled source view must synchronize yaw/pitch and restore the editor world-up free-look basis.");

        Vector3 manualPosition = renderer.Camera.Position;
        renderer.ApplyManualCameraLook(new(80, -25));
        if (renderer.Camera.Position != manualPosition
            || Vector3.Distance(Vector3.Normalize(renderer.Camera.Target - renderer.Camera.Position), manualForward) < .01f)
            throw new InvalidDataException("Manual RMB look must rotate view direction from the camera position instead of orbiting its target.");

        renderer.Camera.Position += Vector3.UnitX * 3;
        renderer.Camera.Target += Vector3.UnitX * 3;
        renderer.RestoreViewportCameraState(stockCamera);
        if (renderer.Camera.Position != stockCamera.Camera.Position
            || renderer.Camera.Target != stockCamera.Camera.Target
            || renderer.Yaw != stockCamera.Yaw
            || renderer.Pitch != stockCamera.Pitch
            || renderer.MoveSpeed != stockCamera.MoveSpeed
            || renderer.CinematicHorizontalFov != stockCamera.CinematicHorizontalFov)
            throw new InvalidDataException("Reset viewport view must restore the captured stock framing without restoring the source view.");

        if (!renderer.TrySetCameraPose(sourcePose, sourceFov)
            || Vector3.Distance(renderer.Camera.Position, sourcePosition) > .0001f
            || renderer.CinematicHorizontalFov != sourceFov)
            throw new InvalidDataException("Re-enabling source follow must restore the source camera after manual navigation/reset.");

        var invalidPose = default(Matrix4x4);
        var previousCamera = renderer.Camera;
        if (renderer.TrySetCameraPose(invalidPose)
            || renderer.Camera.Position != previousCamera.Position
            || renderer.Camera.Target != previousCamera.Target)
            throw new InvalidDataException("An invalid source pose changed the viewport camera.");
    }

    private static void VerifyHierarchicalCameraPose()
    {
        var parent = new SceneObject
        {
            GaoKey = 0x71003F5A,
            Name = "Animated actor root",
            PreviewMatrix = Matrix4x4.CreateRotationZ(.4f) * Matrix4x4.CreateTranslation(8, -3, 4)
        };
        var helper = new SceneObject
        {
            GaoKey = 0x35009FEB,
            Name = "Parented camera helper",
            ParentId = "71003F5A",
            Transform = new() { Translation = new(0.469f, -0.846f, 1.276f) },
            SerializedHierarchyLocalMatrix = Matrix4x4.CreateTranslation(.25f, .5f, 1.3f)
        };
        SceneObject[] hierarchy = [parent, helper];
        foreach (float time in new[] { 0f, 2.49f, 2.5f, 0f })
        {
            parent.PreviewMatrix = Matrix4x4.CreateRotationZ(.1f + time * .03f)
                * Matrix4x4.CreateTranslation(8 + time, -3, 4);
            if (!CinematicHierarchyPose.TryResolveRenderMatrix(helper, hierarchy, null,
                out var resolved, out var status) || status != CinematicHierarchyPoseStatus.WorldPose)
                throw new InvalidDataException("A parented camera helper failed to resolve against its current animated parent pose.");
            Matrix4x4 expected = AnimationMath.ToRender(helper.SerializedHierarchyLocalMatrix!.Value)
                * AnimationMath.ToRender(parent.PreviewMatrix!.Value);
            if (Vector3.Distance(resolved.Translation, expected.Translation) > .0001f)
                throw new InvalidDataException($"Hierarchical camera helper did not follow its parent at {time} seconds.");
        }

        var cycleParent = new SceneObject { GaoKey = 20, ParentId = "00000015", SerializedHierarchyLocalMatrix = Matrix4x4.Identity };
        var cycleChild = new SceneObject { GaoKey = 21, ParentId = "00000014", SerializedHierarchyLocalMatrix = Matrix4x4.Identity };
        if (CinematicHierarchyPose.TryResolveRenderMatrix(cycleParent, [cycleParent, cycleChild], null,
            out _, out var cycleStatus) || cycleStatus != CinematicHierarchyPoseStatus.Cycle)
            throw new InvalidDataException("A cyclic hierarchy was not reported as unresolved.");

        var ambiguousOne = new SceneObject { GaoKey = 30 };
        var ambiguousTwo = new SceneObject { GaoKey = 30 };
        var ambiguousChild = new SceneObject { GaoKey = 31, ParentId = "0000001E", SerializedHierarchyLocalMatrix = Matrix4x4.Identity };
        if (CinematicHierarchyPose.TryResolveRenderMatrix(ambiguousChild, [ambiguousOne, ambiguousTwo, ambiguousChild], null,
            out _, out var ambiguousStatus) || ambiguousStatus != CinematicHierarchyPoseStatus.ParentAmbiguous)
            throw new InvalidDataException("Ambiguous parent keys were silently resolved to an arbitrary object.");

        var actorOwned = new SceneObject
        {
            GaoKey = 32,
            ParentId = "0000001E",
            SerializedHierarchyLocalMatrix = Matrix4x4.CreateTranslation(100, 100, 100),
            PreviewMatrix = Matrix4x4.CreateTranslation(1, 2, 3),
            Actor = new SceneActor { Root = ambiguousOne }
        };
        Matrix4x4 actorWorld = actorOwned.RenderMatrix;
        Matrix4x4 resolvedActorWorld = CinematicHierarchyPose.ResolveRenderMatrix(actorOwned,
            [ambiguousOne, actorOwned]);
        if (Vector3.Distance(actorWorld.Translation, resolvedActorWorld.Translation) > .0001f)
            throw new InvalidDataException("An actor-owned global preview pose was incorrectly composed with its hierarchy parent.");
    }

    private static void VerifySharedSourceReceivers(string output, WorldData world, AnimationClip clip)
    {
        clip.Tracks.Add(new()
        {
            Bone = -1,
            Target = 98,
            Keys =
            [CinematicTestEvents.Message(0, 98, 60, default, default, 1, 0, object1: 7, object2: 0)]
        });
        var bindings = CinematicCameraBinding.Discover(clip, world.Objects, world.Objects.Take(1));
        if (bindings.Length != 2 || bindings.Select(binding => binding.ReceiverKey).Distinct().Count() != 2)
            throw new InvalidDataException("Camera receivers sharing one marker were merged or duplicated.");
        string path = Path.Combine(output, "camera-shared-source.glb");
        SceneExporter.Export(path, world, new(), new()
        { Actors = true, Animations = true, CinematicClipKey = clip.Key, UnknownObjects = true, Textures = false });
        using var glb = new GlbReader(path);
        var cameraNodes = glb.Root.GetProperty("nodes").EnumerateArray()
            .Where(node => node.TryGetProperty("camera", out _)).ToArray();
        if (cameraNodes.Length != 2 || cameraNodes.Select(node => node.GetProperty("extras")
                .GetProperty("cameraReceiverKey").GetString()).Distinct().Count() != 2)
            throw new InvalidDataException("GLB lost independent camera receivers sharing a source marker.");
    }

    private static void VerifyPose(Matrix4x4 pose, SceneObject first, SceneObject target, SceneObject second,
        SceneObject moving, float time)
    {
        Vector3 position = time < 2 ? first.RenderMatrix.Translation : time < 3 ? moving.RenderMatrix.Translation : second.RenderMatrix.Translation;
        if (time >= 3.5f) position = new(10, 0, -1);
        if (Vector3.Distance(pose.Translation, position) > .0001f)
            throw new InvalidDataException($"Routed camera position mismatch at {time}.");
        if (time is >= 3 and < 3.5f)
        {
            var actual = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, pose));
            var expected = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, second.RenderMatrix));
            if (Vector3.Distance(actual, expected) > .0001f) throw new InvalidDataException("Target-free camera lost authored orientation.");
            return;
        }
        var point = time is >= 1 and < 2 ? moving.RenderMatrix.Translation : target.RenderMatrix.Translation;
        if (time >= 3.5f) point += Vector3.UnitY;
        var direction = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, pose));
        if (Vector3.Distance(direction, Vector3.Normalize(point - position)) > .0001f)
            throw new InvalidDataException($"Routed camera target mismatch at {time}.");
    }

    private static AnimationKey Command(float time, uint position, uint target, Vector3 first = default,
        Vector3 second = default, float transition = 0) =>
        CinematicTestEvents.Message(time, 99, 60, first, second, 1, 0,
            scalar2: transition, object1: position, object2: target);
}
