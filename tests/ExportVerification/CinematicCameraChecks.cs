using System.Numerics;
using JadeView;

internal static class CinematicCameraChecks
{
    internal static void Verify(string output)
    {
        var position = new SceneObject
        {
            GaoKey = 7,
            Name = "Marker_A",
            DrawFlags = 8,
            Transform = new() { Translation = new(10, 0, 0) }
        };
        var target = new SceneObject
        {
            GaoKey = 8,
            Name = "Marker_B",
            DrawFlags = 8,
            Transform = new() { Translation = new(11, 0, 0) }
        };
        var controller = new SceneObject { GaoKey = 9, Name = "Controller", DrawFlags = 8 };
        var clip = new AnimationClip
        {
            Key = 10,
            Loaded = true,
            Cinematic = true,
            Duration = 1,
            Tracks =
        [
            new() { Bone = -1, Target = 7, Keys = [new() { Duration = 1, Position = new(10, 0, 0) }] },
            new() { Bone = -1, Target = 8, Keys = [new() { Duration = 1, Position = new(11, 0, 0) }] },
            new() { Bone = -1, Target = 9, Keys = [Message()] }
        ]
        };
        SceneObject[] objects = [position, target, controller];
        var binding = CinematicCameraBinding.Discover(clip, objects, []).Single();
        if (!binding.FromSourceEvent || binding.Source != position || binding.Target != target)
            throw new InvalidDataException("Camera binding relied on object names instead of literal message references.");
        var sharedOwnerClip = new AnimationClip
        {
            CinematicOwnerKeys = [9, 10],
            Tracks = [new() { Bone = -1, Target = 0, Keys = [Message()] }]
        };
        var sharedCameraEvents = CinematicCameraEvent.Read(sharedOwnerClip);
        if (sharedCameraEvents.Length != 2 || !sharedCameraEvents.Select(item => item.ReceiverKey).Order().SequenceEqual([9u, 10u]))
            throw new InvalidDataException("A target-zero camera event on a shared clip did not retain both owner receivers.");
        var pose = binding.RenderPose();
        Matrix4x4.Decompose(pose, out _, out var rotation, out _);
        if (Vector3.Distance(Vector3.Transform(-Vector3.UnitZ, rotation), Vector3.UnitX) > .0001f)
            throw new InvalidDataException("Explicit camera target was not applied.");
        var world = new WorldData();
        world.Objects.AddRange(objects);
        world.CinematicClips.Add(clip.Key, clip);
        string path = Path.Combine(output, "camera-reference-binding.glb");
        SceneExporter.Export(path, world, new(), new()
        {
            Animations = true,
            CinematicClipKey = clip.Key,
            Textures = false,
            UnknownObjects = true
        });
        using var glb = new GlbReader(path);
        if (glb.Root.GetProperty("cameras").GetArrayLength() != 1)
            throw new InvalidDataException("A camera helper without Camera in its name did not export as a camera.");
        Console.WriteLine("PASS: camera/target references from message 60, arbitrary object names and GLB camera role");
    }

    private static AnimationKey Message()
    {
        uint[] values = [0, 0, 60, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 123];
        uint[] tags = [0x01000028, 0x02000806, 0x01000021, 0x01000028, 0x01000028,
            0x01000022, 0x01000022, 0x01000022, 0x03001770, 0x01000022, 0x01000022,
            0x01000022, 0x03001770, 0x01000022, 0x01000022, 0x01000021, 0x01000021, 0x03001f78, 0x1f000000];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++) { writer.Write(values[index]); writer.Write(tags[index]); }
        return new() { Duration = 1, OpaquePayloads = [new("word-pair-list", 0, stream.ToArray())] };
    }
}
