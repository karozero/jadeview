using System.Numerics;
using JadeView;

internal static class CinematicSightSetChecks
{
    internal static void Verify(string outputDirectory)
    {
        var target = new SceneObject
        {
            GaoKey = 0x71001234,
            Name = "Sight-set receiver",
            Transform = new()
            {
                Translation = new(3, 4, 5),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .4f),
                Scale = new(1.5f, 2, .75f)
            }
        };
        var world = new WorldData { Key = 0x35000001, Name = "sight-set test" };
        world.Objects.Add(target);
        AnimationKey sourceCall = CinematicTestEvents.VectorCall(1, target.GaoKey,
            0x1FB6, Vector3.UnitX);
        var clip = new AnimationClip
        {
            Key = 0x35000002,
            Name = "sight-set",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = target.GaoKey, Keys = [sourceCall] }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        Matrix4x4 initialPose = AnimationMath.Trs(target.Transform);
        Vector3 initialBankAxis = Vector3.Normalize(new(initialPose.M31, initialPose.M32, initialPose.M33));
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, target.PreviewMatrix!.Value),
            Vector3.TransformNormal(-Vector3.UnitY, initialPose),
            "OBJ_SightSet changed its receiver before the EVE command time.");
        playback.Seek(1.5f);
        Matrix4x4 sightPose = target.PreviewMatrix!.Value;
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, sightPose)), Vector3.UnitX,
            "OBJ_SightSet did not aim the native -Y sight axis along its serialized vector.");
        Vector3 resultingBankAxis = Vector3.Normalize(new(sightPose.M31, sightPose.M32, sightPose.M33));
        Require(Vector3.Dot(initialBankAxis, resultingBankAxis) > .999f,
            "OBJ_SightSet did not preserve the receiver's existing banking axis.");
        Require(Vector3.Distance(sightPose.Translation, initialPose.Translation) < .001f,
            "OBJ_SightSet changed the receiver position.");
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, target.PreviewMatrix!.Value),
            Vector3.TransformNormal(-Vector3.UnitY, initialPose),
            "Reverse seeking before OBJ_SightSet did not restore the authored pose.");
        playback.Deactivate();

        string exportPath = Path.Combine(outputDirectory, "cinematic-sight-set.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using (var glb = new GlbReader(exportPath))
        {
            int sourceNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
                .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == target.Name);
            int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
                .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
            Matrix4x4 exportedPose = glb.World(sourceNode, animationIndex, 1.5f);
            RequireNear(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, exportedPose)), Vector3.UnitX,
                "Cinematic GLB export did not preserve the scripted OBJ_SightSet rotation.");
        }

        VerifyAuthoredRotationWinsAfterSightSet(target.GaoKey);
        VerifyLaterOrientToTakesOver();
        VerifyActorRootTarget(outputDirectory);
        Console.WriteLine("PASS: source-timed OBJ_SightSet preview and GLB export preserve banking, position and reverse seek");
    }

    private static void VerifyAuthoredRotationWinsAfterSightSet(uint targetKey)
    {
        var target = new SceneObject { GaoKey = targetKey, Name = "Animated sight-set receiver" };
        var world = new WorldData { Key = 0x35000003, Name = "authored-rotation test" };
        world.Objects.Add(target);
        AnimationTrack track = new()
        {
            Bone = -1,
            Target = target.GaoKey,
            Keys =
            [
                new AnimationKey { Time = 0, Rotation = Quaternion.Identity },
                CinematicTestEvents.VectorCall(1, target.GaoKey, 0x1FB6, Vector3.UnitX),
                new AnimationKey
                {
                    Time = 1.5f,
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)
                }
            ]
        };
        var clip = new AnimationClip
        {
            Key = 0x35000004,
            Name = "sight-set-with-authored-turn",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [track]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(1.2f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, target.PreviewMatrix!.Value)), Vector3.UnitX,
            "The event sight orientation was not active before a later authored rotation key.");
        playback.Seek(1.75f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, target.PreviewMatrix!.Value)), Vector3.UnitY,
            "OBJ_SightSet overrode a later authored rotation channel.");
        playback.Deactivate();
    }

    private static void VerifyActorRootTarget(string outputDirectory)
    {
        var root = new SceneObject { GaoKey = 0x71004567, Name = "Sight-set actor root" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var world = new WorldData { Key = 0x35000005, Name = "actor root sight-set test" };
        world.Objects.Add(root);
        world.Actors.Add(actor);
        var clip = new AnimationClip
        {
            Key = 0x35000006,
            Name = "actor-root-sight-set",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = root.GaoKey,
                Keys = [CinematicTestEvents.VectorCall(1, root.GaoKey, 0x1FB6, Vector3.UnitX)] }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        CinematicPlayback playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(1.5f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, root.PreviewMatrix!.Value)), Vector3.UnitX,
            "OBJ_SightSet did not apply to an actor-root receiver.");

        string exportPath = Path.Combine(outputDirectory, "cinematic-sight-set-actor-root.glb");
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
            .Single(index => glb.Root.GetProperty("nodes")[index].TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("gaoKey", out var gaoKey)
                && gaoKey.GetString() == root.GaoKey.ToString("X8")
                && glb.Root.GetProperty("nodes")[index].TryGetProperty("translation", out _));
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(rootNode, animationIndex, 1.5f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, exportedPose)), Vector3.UnitX,
            "Cinematic GLB export did not merge actor-root OBJ_SightSet into the root animation.");
        playback.Deactivate();
    }

    private static void VerifyLaterOrientToTakesOver()
    {
        var source = new SceneObject { GaoKey = 0x71007890, Name = "Sight then orient" };
        var reference = new SceneObject
        {
            GaoKey = 0x71007891,
            Name = "Orient target",
            Transform = new() { Translation = new(0, 10, 0) }
        };
        var world = new WorldData { Key = 0x35000007, Name = "orientation command order" };
        world.Objects.AddRange([source, reference]);
        var clip = new AnimationClip
        {
            Key = 0x35000008,
            Name = "sight-then-orient",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks =
            [
                new() { Bone = -1, Target = source.GaoKey,
                    Keys = [CinematicTestEvents.VectorCall(1, source.GaoKey, 0x1FB6, Vector3.UnitX)] },
                new() { Bone = -1, Target = source.GaoKey,
                    Keys = [CinematicTestEvents.Call(1.5f, source.GaoKey, 8151, reference.GaoKey)] }
            ]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        CinematicPlayback playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(1.2f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value)),
            Vector3.UnitX, "OBJ_SightSet should remain active until the later OBJ_OrientTo command.");
        playback.Seek(1.75f);
        RequireNear(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value)),
            Vector3.UnitY, "An earlier OBJ_SightSet overrode the later OBJ_OrientTo command.");
        playback.Deactivate();
    }

    private static void RequireNear(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > .002f)
            throw new InvalidDataException($"{message} Expected {expected}, got {actual}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
