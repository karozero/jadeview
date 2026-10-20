using System.Numerics;
using JadeView;

internal static class CinematicPlaybackChecks
{
    internal static void Verify(string outputDirectory)
    {
        var root = new SceneObject
        {
            Name = "Actor",
            GaoKey = 1,
            Transform = new() { Translation = new(2, 3, 4) }
        };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var camera = new SceneObject { Name = "Cine__Camera.gao", GaoKey = 2 };
        var cameraTarget = new SceneObject
        {
            Name = "Cine__Cible.gao",
            GaoKey = 3,
            Transform = new() { Translation = new(100, 20, 5) }
        };
        var clip = new AnimationClip
        {
            Key = 10,
            Name = "Synchronized fixture",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            Duration = 2,
            Tracks =
            [
                new() { Bone = -1, Target = 1, Keys =
                [
                    new() { Time = 0, Duration = 2, Position = new(100, 20, 5) },
                    new() { Time = 2, Position = new(104, 20, 5) }
                ] },
                new() { Bone = -1, Target = 2, Keys =
                [new() { Time = 0, Duration = 2, Position = new(95, 20, 5) }] }
            ]
        };
        actor.Clips.Add(clip);
        var world = new WorldData { Name = "Cinematic fixture" };
        world.Objects.AddRange([root, camera, cameraTarget]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.CinematicClips.Add(clip.Key, clip);
        VerifyAssembly(document, world, root);
        var resolver = new ResourceResolver();
        var playback = CinematicPlayback.Discover(document, world, resolver).Single();
        playback.Activate();
        Near(root.PreviewMatrix!.Value.Translation, new(100, 20, 5), "Initial cinematic world position");
        playback.Play();
        playback.Advance(.5f);
        Near(root.PreviewMatrix!.Value.Translation, new(101, 20, 5), "Cinematic time must survive a slow frame");
        playback.Advance(float.NaN);
        Near(root.PreviewMatrix!.Value.Translation, new(101, 20, 5), "Invalid frame time must not poison poses");
        playback.Seek(1);
        Near(root.PreviewMatrix!.Value.Translation, new(102, 20, 5), "Cinematic seek world position");

        string path = Path.Combine(outputDirectory, "cinematic-combined.glb");
        SceneExporter.Export(path, world, resolver, new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            Textures = false
        });
        using var glb = new GlbReader(path);
        var animations = glb.Root.GetProperty("animations");
        if (animations.GetArrayLength() != 1)
            throw new InvalidDataException("Actor and camera must share one cinematic animation.");
        int samplerCount = animations[0].GetProperty("samplers").GetArrayLength();
        foreach (var channel in animations[0].GetProperty("channels").EnumerateArray())
            if (channel.GetProperty("sampler").GetInt32() >= samplerCount)
                throw new InvalidDataException("Merged cinematic sampler is out of bounds.");
        var nodes = glb.Root.GetProperty("nodes");
        int cameraNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
            nodes[index].GetProperty("name").GetString() == camera.Name);
        var cameraRotationChannel = animations[0].GetProperty("channels").EnumerateArray().Single(channel =>
            channel.GetProperty("target").GetProperty("node").GetInt32() == cameraNode
            && channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
        var rotationSampler = animations[0].GetProperty("samplers")[cameraRotationChannel.GetProperty("sampler").GetInt32()];
        float[] rotation = glb.Accessor(rotationSampler.GetProperty("output").GetInt32())[0];
        Near(Vector3.Transform(-Vector3.UnitZ, new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])),
            Vector3.UnitX, "Exported camera must face its source target");

        clip.Cinematic = false;
        actor.SelectClip(clip);
        Near(root.PreviewMatrix!.Value.Translation, root.Transform.Translation, "Gameplay root motion remains relative");
        Console.WriteLine("PASS: absolute cinematic placement, frame timing, seek and unified actor/camera export");
    }

    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > .0001f)
            throw new InvalidDataException($"{message}: {actual}, expected {expected}");
    }

    private static void VerifyAssembly(SceneDocument document, WorldData actorWorld, SceneObject root)
    {
        var cinematicWorld = new WorldData { Name = "Fixture_cine" };
        var environment = new WorldData { Name = "Fixture" };
        var scenery = new SceneObject { Name = "Scenery" };
        environment.Objects.Add(scenery);
        var cinematicProp = new SceneObject { Name = "Animated prop", GaoKey = 901 };
        var environmentProp = new SceneObject { Name = "Duplicate prop", GaoKey = 901 };
        cinematicWorld.Objects.Add(cinematicProp);
        environment.Objects.Add(environmentProp);
        var unrelated = new SceneObject { Name = "Unrelated inventory object", GaoKey = 900 };
        actorWorld.Objects.Add(unrelated);
        document.Worlds.AddRange([actorWorld, cinematicWorld, environment]);
        var assembled = CinematicWorldAssembler.Assemble(document, cinematicWorld);
        if (!assembled.Objects.Contains(root) || !assembled.Objects.Contains(scenery)
            || assembled.Objects.Contains(unrelated) || !assembled.Objects.Contains(cinematicProp)
            || assembled.Objects.Contains(environmentProp))
            throw new InvalidDataException("Cinematic assembly must include participants and scenery without unrelated setup objects.");
        actorWorld.Objects.Remove(unrelated);
    }
}
