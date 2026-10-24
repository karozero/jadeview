using System.Numerics;
using JadeView;

internal static class CinematicPositionSetExportChecks
{
    internal static void Verify(string outputDirectory)
    {
        const uint worldKey = 0x100;
        const uint actorKey = 0x71003F5A;
        const uint boneKey = 0x71003F5B;
        const uint clipKey = 0x3500A927;

        var root = new SceneObject { Name = "Position-set actor", GaoKey = actorKey };
        var prop = new SceneObject { Name = "Cinematic reference", GaoKey = 0x400 };
        var bone = new SceneObject
        {
            Name = "Position-set bone",
            GaoKey = boneKey,
            ActorNode = new() { Parent = actorKey, Local = Matrix4x4.CreateTranslation(Vector3.UnitY) }
        };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        root.Actor = bone.Actor = actor;

        var actionClip = new AnimationClip
        {
            Key = 0x200,
            Loaded = true,
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
                [new() { Duration = 1, Position = Vector3.Zero },
                    new() { Time = 1, Position = Vector3.UnitX }] }]
        };
        var action = new CinematicAction(0,
            [new CinematicActionItem(actionClip, 0, 64, 0x300) { Flags = 0 }]);
        var source = new AnimationClip
        {
            Key = clipKey,
            Name = "One-shot actor position setter",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = worldKey,
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = actorKey, Keys =
                [CinematicTestEvents.Call(0, actorKey, 0x3391, 0x300),
                    CinematicTestEvents.VectorCall(.5f, actorKey, 0x1fa6, new(5, 0, 0))] },
                new() { Bone = -1, Target = prop.GaoKey, Keys = [new() { Position = Vector3.Zero }] }]
        };
        var world = new WorldData { Key = worldKey, Name = "Position-set export fixture" };
        world.Objects.AddRange([root, bone, prop]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(source.Key, source);

        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(source.Key, source);
        document.CinematicActionKitsByOwner[(worldKey, actorKey)] = new() { [0x300] = action };

        var playback = CinematicPlayback.Discover(document, world, new ResourceResolver()).Single();
        playback.Activate();
        playback.Seek(.75f);
        Matrix4x4 previewBeforeExport = root.PreviewMatrix!.Value;

        string exportPath = Path.Combine(outputDirectory, "cinematic-position-set-root-motion.glb");
        SceneExporter.Export(exportPath, world, new ResourceResolver(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            Textures = false,
            CinematicClipKey = clipKey,
            CinematicDocument = document
        });

        using var glb = new GlbReader(exportPath);
        var nodes = glb.Root.GetProperty("nodes");
        int rootNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
            nodes[index].GetProperty("name").GetString() == root.Name
            && nodes[index].GetProperty("extras").TryGetProperty("gaoKey", out _)
            && !nodes[index].GetProperty("extras").TryGetProperty("actor", out _));
        var animation = glb.Root.GetProperty("animations").EnumerateArray().Single(item =>
            item.GetProperty("name").GetString() == source.Name);
        var translationChannel = animation.GetProperty("channels").EnumerateArray().Single(channel =>
            channel.GetProperty("target").GetProperty("node").GetInt32() == rootNode
            && channel.GetProperty("target").GetProperty("path").GetString() == "translation");
        var sampler = animation.GetProperty("samplers")[
            translationChannel.GetProperty("sampler").GetInt32()];
        float[][] times = glb.Accessor(sampler.GetProperty("input").GetInt32());
        float[][] positions = glb.Accessor(sampler.GetProperty("output").GetInt32());
        int setIndex = FindSample(times, .5f);
        int movedIndex = FindSample(times, .75f);
        Vector3 setPosition = ReadVector(positions[setIndex]);
        Vector3 movedPosition = ReadVector(positions[movedIndex]);
        Vector3 expectedSetPosition = TransformData.ToRender(new(5, 0, 0));
        Vector3 expectedAfterSetDelta = TransformData.ToRender(new(.25f, 0, 0));
        if (Vector3.Distance(setPosition, expectedSetPosition) > .001f)
            throw new InvalidDataException($"GLB omitted the one-shot actor position at 0.5s: {setPosition}.");
        if (Vector3.Distance(movedPosition - setPosition, expectedAfterSetDelta) > .001f)
            throw new InvalidDataException($"GLB actor root did not continue action motion after PosSet: {movedPosition - setPosition}.");
        if (MatrixDistance(root.PreviewMatrix!.Value, previewBeforeExport) > .001f)
            throw new InvalidDataException("Cinematic GLB export did not restore the active preview pose.");

        Console.WriteLine("PASS: GLB actor-root motion continues after one-shot OBJ_PosSet and export restores preview state");

        static int FindSample(float[][] samples, float time)
        {
            int index = Array.FindIndex(samples, sample => sample[0] == time);
            return index >= 0 ? index : throw new InvalidDataException($"GLB omitted exact sample time {time}.");
        }

        static Vector3 ReadVector(float[] values) => new(values[0], values[1], values[2]);

        static float MatrixDistance(Matrix4x4 first, Matrix4x4 second) =>
            MathF.Sqrt(
                Square(first.M11 - second.M11) + Square(first.M12 - second.M12)
                + Square(first.M13 - second.M13) + Square(first.M14 - second.M14)
                + Square(first.M21 - second.M21) + Square(first.M22 - second.M22)
                + Square(first.M23 - second.M23) + Square(first.M24 - second.M24)
                + Square(first.M31 - second.M31) + Square(first.M32 - second.M32)
                + Square(first.M33 - second.M33) + Square(first.M34 - second.M34)
                + Square(first.M41 - second.M41) + Square(first.M42 - second.M42)
                + Square(first.M43 - second.M43) + Square(first.M44 - second.M44));

        static float Square(float value) => value * value;
    }
}
