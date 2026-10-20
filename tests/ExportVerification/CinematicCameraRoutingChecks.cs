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
            throw new InvalidDataException("Repeated camera references incorrectly retained positional transition delay.");
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
        Console.WriteLine("PASS: source camera/target routing, actor references, no-target orientation, unscaled cut offsets, reverse seek and matching GLB poses");
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

    private static AnimationKey Command(float time, uint position, uint target, Vector3 first = default, Vector3 second = default) =>
        CinematicTestEvents.Message(time, 99, 60, first, second, 1, 0, object1: position, object2: target);
}
