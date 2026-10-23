using System.Numerics;
using System.Reflection;
using JadeView;

internal static class CinematicFacialSignalChecks
{
    internal static void Verify()
    {
        var geometry = new Geometry { Key = 100, Positions = [Vector3.Zero] };
        var root = new SceneObject
        {
            GaoKey = 1,
            GeometryKey = 100,
            Modifiers = [new(4, MorphPreviewChecks.Payload(1))]
        };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var helper = new SceneObject { GaoKey = 3 };
        var camera = new SceneObject { GaoKey = 2, Name = "Camera" };
        var clip = new AnimationClip
        {
            Loaded = true,
            Key = 10,
            Duration = 2,
            Cinematic = true,
            Tracks =
        [
            new() { Bone = -1, Target = 1, Keys = [new() { Duration = 2, Position = Vector3.Zero }] },
            new() { Bone = -1, Target = 2, Keys = [new() { Duration = 2, Position = Vector3.UnitZ }] },
            new() { Bone = -1, Target = 3, Keys =
                [new() { Duration = 2, Position = new(1, -1, 0) }, new() { Time = 2, Position = new(1, 0, 0) }] },
            new() { Bone = -1, Target = 3, Keys = [Binding()] }
        ]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(clip.Key, clip);
        var world = new WorldData();
        world.Objects.AddRange([root, helper, camera]);
        world.Actors.Add(actor);
        var resolver = new ResourceResolver();
        var geometries = (Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resolver)!;
        geometries.Add(geometry.Key, geometry);
        var playback = CinematicPlayback.Discover(document, world, resolver).Single();
        playback.Activate();
        VerifyWeight(.5f, .25f);
        VerifyWeight(1.5f, .75f);
        VerifyWeight(2, 1);
        VerifyWeight(.5f, .25f);
        playback.Pause();
        var preview = root.MorphPreview!;
        preview.Timeline.Record(0, [0, 0]);
        preview.Timeline.Record(1, [0, 1]);
        preview.Timeline.Playing = true;
        actor.Evaluate(.1f);
        if (MathF.Abs(preview.GetWeight(1) - .25f) > .0001f)
            throw new InvalidDataException("Viewport advanced facial time outside the cinematic clock.");
        if (!actor.CinematicClockOwned) throw new InvalidDataException("Cinematic lost ownership of actor time.");
        playback.Deactivate();
        if (actor.CinematicClockOwned) throw new InvalidDataException("Cinematic clock ownership leaked after exit.");
        Console.WriteLine("PASS: continuous facial helper from separate event/transform tracks, shared clock, seek and cleanup");
        VerifyDirectChannelCalls();

        void VerifyWeight(float time, float expected)
        {
            playback.Seek(time);
            if (MathF.Abs(root.MorphPreview!.GetWeight(1) - expected) > .0001f)
                throw new InvalidDataException($"Facial helper was not sampled continuously at {time}.");
        }
    }

    private static void VerifyDirectChannelCalls()
    {
        var root = new SceneObject
        {
            GaoKey = 0x101,
            GeometryKey = 100,
            Modifiers = [new(4, MorphPreviewChecks.Payload(1))]
        };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var camera = new SceneObject { GaoKey = 0x102, Name = "Camera" };
        var clip = new AnimationClip
        {
            Loaded = true,
            Key = 0x103,
            Duration = 1,
            Cinematic = true,
            Tracks =
            [
                new() { Bone = -1, Target = root.GaoKey, Keys = [new() { Duration = 1 }] },
                new() { Bone = -1, Target = camera.GaoKey, Keys = [new() { Duration = 1, Position = Vector3.UnitZ }] },
                new()
                {
                    Bone = -1,
                    Target = root.GaoKey,
                    Keys =
                    [
                        new() { Time = .25f, OpaquePayloads = [ChannelCall(8253, .5f)] },
                        new() { Time = .75f, OpaquePayloads = [ChannelCall(8255, .25f)] }
                    ]
                }
            ]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(clip.Key, clip);
        var world = new WorldData();
        world.Objects.AddRange([root, camera]);
        world.Actors.Add(actor);
        var resolver = new ResourceResolver();
        ((Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resolver)!)
            .Add(100, new Geometry { Key = 100, Positions = [Vector3.Zero] });

        var playback = CinematicPlayback.Discover(document, world, resolver).Single();
        playback.Activate();
        VerifyState(.25f, .5f, 0);
        VerifyState(.5f, .5f, 0);
        VerifyState(.75f, .5f, .25f);
        VerifyState(1, .5f, .25f);
        VerifyState(0, 1, 0);
        playback.Deactivate();
        Console.WriteLine("PASS: literal OBJ_SetChannelFactor/OBJ_SetChannelProg apply to actor morph channels and seek deterministically");

        void VerifyState(float time, float expectedWeight, float expectedProgression)
        {
            playback.Seek(time);
            var state = root.MorphPreview!.GetChannel(0);
            if (MathF.Abs(state.Weight - expectedWeight) > .0001f
                || MathF.Abs(state.Progression - expectedProgression) > .0001f)
                throw new InvalidDataException($"Morph channel script call at {time} produced {state}, expected {expectedWeight}/{expectedProgression}.");
        }
    }

    private static AnimationOpaquePayload ChannelCall(int selector, float value)
    {
        uint[] values = [0, 0, 0, BitConverter.SingleToUInt32Bits(value), 0, 0];
        uint[] tags = [0x01000028, 0x02000806, 0x01000021, 0x01000022,
            (uint)(0x03000000 | selector), 0x1f000000];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            writer.Write(values[index]);
            writer.Write(tags[index]);
        }
        return new("word-pair-list", 0, stream.ToArray());
    }

    private static AnimationKey Binding()
    {
        uint[] values = [0, 0, 1, uint.MaxValue, 0, 0, 0];
        uint[] tags = [0x01000028, 0x02000806, 0x01000028, 0x01000021, 0x01000021, 0x03002046, 0x1f000000];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++) { writer.Write(values[index]); writer.Write(tags[index]); }
        return new() { Duration = 2, OpaquePayloads = [new("word-pair-list", 0, stream.ToArray())] };
    }
}
