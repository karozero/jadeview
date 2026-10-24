using System.Numerics;
using JadeView;

internal static class CinematicOrientToChecks
{
    internal static void Verify(string outputDirectory)
    {
        VerifyMatrixBasisRoundTrip();
        VerifyFlashReferenceSpace();
        VerifyActorRootTarget(outputDirectory);
        var source = new SceneObject { GaoKey = 10, Name = "Orient source" };
        var reference = new SceneObject
        {
            GaoKey = 20,
            Name = "Orient reference",
            Transform = new() { Translation = new Vector3(10, 0, 0) }
        };
        var world = new WorldData { Key = 30, Name = "orientation test" };
        world.Objects.AddRange([source, reference]);
        var clip = new AnimationClip
        {
            Key = 40,
            Name = "orient-to",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
                [CinematicTestEvents.Call(1, source.GaoKey, 8151, reference.GaoKey)] }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            -Vector3.UnitY, "OBJ_OrientTo changed the source before its event time.");
        playback.Seek(1.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            Vector3.UnitX, "OBJ_OrientTo did not orient the source's native -Y sight axis toward its reference.");
        Require(Vector3.Distance(source.PreviewMatrix!.Value.Translation, Vector3.Zero) < .001f,
            "OBJ_OrientTo changed the source object's position.");
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            -Vector3.UnitY, "Seeking before OBJ_OrientTo did not restore the authored orientation.");
        playback.Deactivate();

        VerifyParentRelativeTarget(outputDirectory);

        string exportPath = Path.Combine(outputDirectory, "cinematic-orient-to.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(exportPath);
        int sourceNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == source.Name);
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(sourceNode, animationIndex, 1.5f);
        RequireNear(Vector3.TransformNormal(Vector3.UnitZ, exportedPose), Vector3.UnitX,
            "Cinematic GLB export did not preserve the scripted OBJ_OrientTo rotation.");
        Console.WriteLine("PASS: source-timed OBJ_OrientTo preview, reverse seek, position preservation and GLB export");
    }

    private static void VerifyFlashReferenceSpace()
    {
        var parent = new SceneObject
        {
            GaoKey = 60,
            Transform = new() { Translation = new(10, 0, 0) }
        };
        Matrix4x4 localReference = AnimationMath.Trs(new TransformData
        {
            Translation = new(2, 0, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        });
        var child = new SceneObject
        {
            GaoKey = 61,
            ParentId = parent.GaoKey.ToString("X8"),
            Flags = 0x04000000,
            SerializedHierarchyLocalMatrix = localReference,
            InitialTransform = new() { Translation = new(100, 0, 0) }
        };
        var root = new SceneObject
        {
            GaoKey = 62,
            Flags = 0x04000000,
            Transform = new() { Translation = new(100, 0, 0) },
            InitialTransform = new() { Translation = new(5, 0, 0) }
        };
        AnimationTrack[] tracks =
        [
            new()
            {
                Bone = -1,
                Target = child.GaoKey,
                UsesFlashReference = true,
                Keys = [new AnimationKey { Time = 0, Flags = 0x84, Position = Vector3.UnitX }]
            },
            new()
            {
                Bone = -1,
                Target = root.GaoKey,
                UsesFlashReference = true,
                Keys = [new AnimationKey { Time = 0, Flags = 0x84, Position = Vector3.UnitX }]
            }
        ];
        Dictionary<uint, AnimationTrack[]> tracksByTarget = CinematicHierarchyTransformSampler.Index(tracks);
        SceneObject[] objects = [parent, child, root];

        Matrix4x4 childPose = CinematicHierarchyTransformSampler.EvaluateWorldRender(child,
            tracksByTarget, 0, objects);
        Matrix4x4 expectedChild = AnimationMath.ToRender(AnimationMath.Trs(new TransformData
            {
                Translation = new(3, 0, 0),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            }) * AnimationMath.Trs(parent.Transform));
        RequireMatrixNear(childPose, expectedChild,
            "A hierarchy EVE position key should use the local reference matrix, not the primary/absolute transform.");

        Matrix4x4 rootPose = CinematicHierarchyTransformSampler.EvaluateWorldRender(root,
            tracksByTarget, 0, objects);
        Matrix4x4 expectedRoot = AnimationMath.ToRender(AnimationMath.Trs(new TransformData
        {
            Translation = new(6, 0, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        }));
        RequireMatrixNear(rootPose, expectedRoot,
            "A root EVE position key should use its initial absolute reference without transforming the primary baseline twice.");

        var ordinaryRoot = new SceneObject
        {
            GaoKey = 64,
            Flags = 0x04000000,
            Transform = new() { Translation = new(100, 0, 0) },
            InitialTransform = new() { Translation = new(5, 0, 0) }
        };
        AnimationTrack ordinaryTrack = new()
        {
            Bone = -1,
            Target = ordinaryRoot.GaoKey,
            Keys = [new AnimationKey { Time = 0, Flags = 0x80, Position = Vector3.UnitX }]
        };
        Matrix4x4 ordinaryPose = CinematicHierarchyTransformSampler.EvaluateWorldRender(ordinaryRoot,
            CinematicHierarchyTransformSampler.Index([ordinaryTrack]), 0, [ordinaryRoot]);
        RequireNear(ordinaryPose.Translation, Vector3.UnitX,
            "An ordinary interpolation track must not be transformed by an available Flash reference.");

        var rotationOnlyRoot = new SceneObject
        {
            GaoKey = 63,
            Flags = 0x04000000,
            Transform = new() { Translation = new(100, 0, 0) },
            InitialTransform = new() { Translation = new(5, 0, 0) }
        };
        AnimationTrack rotationOnlyTrack = new()
        {
            Bone = -1,
            Target = rotationOnlyRoot.GaoKey,
            Keys = [new AnimationKey
            {
                Time = 0,
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)
            }]
        };
        Matrix4x4 rotationOnlyPose = CinematicHierarchyTransformSampler.EvaluateWorldRender(
            rotationOnlyRoot, CinematicHierarchyTransformSampler.Index([rotationOnlyTrack]), 0,
            [rotationOnlyRoot]);
        Require(Vector3.Distance(rotationOnlyPose.Translation, new Vector3(100, 0, 0)) < .001f,
            "A rotation-only EVE key must not apply the reference translation to the unanimated position.");
        Console.WriteLine("PASS: EVE transform keys use hierarchy-local or root-absolute reference spaces");
    }

    private static void VerifyParentRelativeTarget(string outputDirectory)
    {
        var parent = new SceneObject
        {
            GaoKey = 30,
            Name = "Parent",
            Transform = new()
            {
                Translation = new(10, 0, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)
            }
        };
        Matrix4x4 localPose = AnimationMath.Trs(new TransformData
        {
            Translation = new(2, 1, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        });
        var source = new SceneObject
        {
            GaoKey = 31,
            Name = "Parented orient source",
            ParentId = parent.GaoKey.ToString("X8"),
            SerializedHierarchyLocalMatrix = localPose,
            Transform = new() { Translation = new(-50, 20, 3) }
        };
        var reference = new SceneObject
        {
            GaoKey = 32,
            Name = "World reference",
            Transform = new() { Translation = new(20, 5, 4) }
        };
        var world = new WorldData { Key = 33, Name = "parented orientation test" };
        world.Objects.AddRange([parent, source, reference]);
        var clip = new AnimationClip
        {
            Key = 34,
            Name = "parented-orient-to",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks =
            [
                new() { Bone = -1, Target = 0, Keys =
                    [CinematicTestEvents.Call(1, source.GaoKey, 8151, reference.GaoKey)] },
                new() { Bone = -1, Target = source.GaoKey, Keys =
                [
                    new AnimationKey { Time = 0, Duration = 2, Position = new(2, 1, 0) },
                    new AnimationKey { Time = 2, Position = new(4, 1, 0) }
                ] },
                new() { Bone = -1, Target = parent.GaoKey, Keys =
                [
                    new AnimationKey { Time = 0, Duration = 2, Position = new(10, 0, 0) },
                    new AnimationKey { Time = 2, Position = new(12, 3, 0) }
                ] }
            ]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(.5f);
        Matrix4x4 initialWorldRender = AnimationMath.ToRender(source.PreviewMatrix!.Value);
        Matrix4x4 animatedParentRender = AnimationMath.ToRender(parent.PreviewMatrix!.Value);
        Matrix4x4 animatedLocalRender = AnimationMath.ToRender(AnimationMath.Trs(new TransformData
        {
            Translation = new(2.5f, 1, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        }));
        RequireMatrixNear(initialWorldRender, animatedLocalRender * animatedParentRender,
            "Parented EVE tracks were not sampled in local space before OBJ_OrientTo.");

        playback.Seek(1.5f);
        Matrix4x4 orientedWorldRender = AnimationMath.ToRender(source.PreviewMatrix!.Value);
        animatedParentRender = AnimationMath.ToRender(parent.PreviewMatrix!.Value);
        RequireNear(Vector3.TransformNormal(TransformData.ToRender(-Vector3.UnitY), orientedWorldRender),
            Vector3.Normalize(reference.RenderMatrix.Translation - orientedWorldRender.Translation),
            "Parented OBJ_OrientTo did not orient its world sight axis toward the reference.");
        Require(Matrix4x4.Invert(animatedParentRender, out Matrix4x4 inverseParent),
            "The test parent matrix should be invertible.");
        Matrix4x4 localAfterOrientRender = orientedWorldRender * inverseParent;
        Require(Vector3.Distance(localAfterOrientRender.Translation,
                AnimationMath.ToRender(AnimationMath.Trs(new TransformData
                {
                    Translation = new(3.5f, 1, 0),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One
                })).Translation) < .001f,
            "Parented OBJ_OrientTo changed the animated local position.");
        playback.Deactivate();

        string exportPath = Path.Combine(outputDirectory, "cinematic-orient-to-parented.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(exportPath);
        int sourceNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == source.Name);
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(sourceNode, animationIndex, 1.5f);
        RequireNear(Vector3.TransformNormal(TransformData.ToRender(-Vector3.UnitY), exportedPose),
            Vector3.Normalize(reference.RenderMatrix.Translation - exportedPose.Translation),
            "Cinematic GLB export lost parent-relative OBJ_OrientTo orientation.");
        Console.WriteLine("PASS: animated parent-relative EVE tracks, OBJ_OrientTo and GLB export");
    }

    private static void VerifyActorRootTarget(string outputDirectory)
    {
        var root = new SceneObject { GaoKey = 50, Name = "Orient actor root" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var reference = new SceneObject
        {
            GaoKey = 51,
            Name = "Actor target",
            Transform = new() { Translation = new(10, 0, 0) }
        };
        var world = new WorldData { Key = 52, Name = "actor root orientation test" };
        world.Objects.AddRange([root, reference]);
        world.Actors.Add(actor);
        var clip = new AnimationClip
        {
            Key = 53,
            Name = "actor-root-orient-to",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
                [CinematicTestEvents.Call(1, root.GaoKey, 8151, reference.GaoKey)] }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        CinematicPlayback playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(1.5f);
        RequireNear(Vector3.TransformNormal(TransformData.ToRender(-Vector3.UnitY), root.RenderMatrix),
            Vector3.Normalize(reference.RenderMatrix.Translation - root.RenderMatrix.Translation),
            "OBJ_OrientTo did not evaluate an actor root toward its scene reference.");

        string exportPath = Path.Combine(outputDirectory, "cinematic-orient-to-actor-root.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Actors = true,
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(exportPath);
        int rootNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => HasExportedActorRoot(glb.Root.GetProperty("nodes")[index], root.GaoKey));
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(rootNode, animationIndex, 1.5f);
        RequireNear(Vector3.TransformNormal(TransformData.ToRender(-Vector3.UnitY), exportedPose),
            Vector3.Normalize(reference.RenderMatrix.Translation - exportedPose.Translation),
            "Cinematic GLB export did not merge actor-root OBJ_OrientTo into the actor animation.");
        playback.Deactivate();
        Require(root.PreviewMatrix == null,
            "Deactivating actor-root OBJ_OrientTo did not restore the actor's prior preview matrix.");
        Console.WriteLine("PASS: actor-root OBJ_OrientTo preview and GLB actor-animation integration");
    }

    private static bool HasExportedActorRoot(System.Text.Json.JsonElement node, uint key) =>
        node.TryGetProperty("extras", out var extras)
        && extras.TryGetProperty("gaoKey", out var gaoKey)
        && gaoKey.GetString() == key.ToString("X8")
        && node.TryGetProperty("translation", out _);

    private static void VerifyMatrixBasisRoundTrip()
    {
        Matrix4x4 pose = Matrix4x4.CreateScale(2, 3, 4)
            * Matrix4x4.CreateFromYawPitchRoll(.3f, -.4f, .8f)
            * Matrix4x4.CreateTranslation(6, -7, 8);
        RequireMatrixNear(AnimationMath.FromRender(AnimationMath.ToRender(pose)), pose,
            "The Jade/render matrix basis conversion did not round-trip.");
    }

    private static void RequireMatrixNear(Matrix4x4 actual, Matrix4x4 expected, string message)
    {
        float[] actualValues =
        [actual.M11, actual.M12, actual.M13, actual.M14, actual.M21, actual.M22, actual.M23, actual.M24,
            actual.M31, actual.M32, actual.M33, actual.M34, actual.M41, actual.M42, actual.M43, actual.M44];
        float[] expectedValues =
        [expected.M11, expected.M12, expected.M13, expected.M14, expected.M21, expected.M22, expected.M23, expected.M24,
            expected.M31, expected.M32, expected.M33, expected.M34, expected.M41, expected.M42, expected.M43, expected.M44];
        if (actualValues.Zip(expectedValues).Any(pair => MathF.Abs(pair.First - pair.Second) > .001f))
            throw new InvalidDataException(message);
    }

    private static void RequireNear(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > .001f)
            throw new InvalidDataException($"{message} Actual {actual}; expected {expected}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
