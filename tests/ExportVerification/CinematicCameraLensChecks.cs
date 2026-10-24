using System.Numerics;
using JadeView;

internal static class CinematicCameraLensChecks
{
    internal static void Verify(string output)
    {
        var source = new AnimationClip
        {
            Key = 101,
            Name = "Lens fixture",
            Loaded = true,
            Cinematic = true,
            Duration = 5,
            Tracks =
        [new() { Bone = -1, Target = 99, Keys =
        [Command(1, 2, .6f), Command(2.5f, .5f, .4f), Command(3.5f, 0, 0), Command(4, 0, float.NaN)] },
         new() { Bone = -1, Target = 7, Keys = [new() { Position = Vector3.Zero, Duration = 5 }] }]
        };
        var events = CinematicCameraEvent.Read(source);
        if (events[0].Transition != 2 || events[0].FocalAngle != .6f)
            throw new InvalidDataException("Camera f2 transition and f1 lens were swapped.");
        var lens = new CinematicCameraLens(events);
        var withShake = new CinematicCameraLens(events.Append(new(4.5f, 99, 7, 8, 3, 0,
            default, default, 2)));
        if (Math.Abs(withShake.Evaluate(5) - .4f) > .0001f)
            throw new InvalidDataException("Camera shake parameters were interpreted as a lens command.");
        Check(.5f, 1.2f); Check(1, 1.2f); Check(2, .9f);
        Check(2.75f, .575f); Check(4.5f, .4f); Check(2, .9f);
        var position = new SceneObject { GaoKey = 7, Name = "Position marker" };
        var target = new SceneObject { GaoKey = 8, Name = "Target marker", Transform = new() { Translation = Vector3.UnitX } };
        var world = new WorldData();
        world.Objects.AddRange([position, target]);
        world.CinematicClips.Add(source.Key, source);
        var binding = CinematicCameraBinding.Discover(source, world.Objects, []).Single();
        if (Math.Abs(binding.Lens!.Evaluate(4) - .4f) > .0001f)
            throw new InvalidDataException("Source camera binding lost its lens timeline.");

        CinematicLiteralCall DecodeFocalCall(float time, uint target, uint viewIndex, float angle) =>
            CinematicLiteralCall.Decode(CinematicTestEvents.Call(time, target, 0x30D9, viewIndex,
                BitConverter.SingleToUInt32Bits(angle)).OpaquePayloads[0], target, 0)!;
        var scriptedFocalCalls = new (float Time, CinematicLiteralCall Call)[]
        {
            (4.25f, DecodeFocalCall(4.25f, 99, 0, .75f)),
            (4.5f, DecodeFocalCall(4.5f, 99, 1, 1.1f)),
            (4.75f, DecodeFocalCall(4.75f, 123, 0, 1.0f))
        };
        var scriptedBinding = CinematicCameraBinding.Discover(source, world.Objects, [], scriptedFocalCalls).Single();
        if (Math.Abs(scriptedBinding.Lens!.Evaluate(4.24f) - .4f) > .0001f
            || Math.Abs(scriptedBinding.Lens.Evaluate(4.25f) - .75f) > .0001f
            || Math.Abs(scriptedBinding.Lens.Evaluate(4.8f) - .75f) > .0001f
            || !CinematicEngineFunctionCatalog.Describe(scriptedFocalCalls[0].Call)
                .Contains("world view slot 0 focal angle to 0.75 radians", StringComparison.Ordinal))
            throw new InvalidDataException("Scripted VIEWFocaleSet did not update only the matching camera view's lens.");

        string path = Path.Combine(output, "camera-lens.glb");
        var result = SceneExporter.Export(path, world, new(), new()
        { Animations = true, CinematicClipKey = source.Key, UnknownObjects = true, Textures = false });
        using var glb = new GlbReader(path);
        var camera = glb.Root.GetProperty("cameras")[0];
        float expected = CinematicCameraLens.VerticalAngle(1.2f, CinematicCameraLens.ExportAspect);
        if (Math.Abs(camera.GetProperty("perspective").GetProperty("yfov").GetSingle() - expected) > .0001f
            || !camera.GetProperty("extras").TryGetProperty("jadeViewLensTracks", out _)
            || !result.Warnings.Any(warning => warning.Contains("KHR_animation_pointer")))
            throw new InvalidDataException("GLB lens metadata, initial FOV or animated-FOV limitation was lost.");
        var animation = glb.Root.GetProperty("animations")[0];
        var pointer = animation.GetProperty("channels").EnumerateArray().Single(channel =>
            channel.GetProperty("target").GetProperty("path").GetString() == "pointer");
        var sampler = animation.GetProperty("samplers")[pointer.GetProperty("sampler").GetInt32()];
        var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
        var angles = glb.Accessor(sampler.GetProperty("output").GetInt32());
        int sample = Array.FindIndex(times, value => value[0] == 2);
        if (sample < 0 || Math.Abs(angles[sample][0] - CinematicCameraLens.VerticalAngle(.9f,
            CinematicCameraLens.ExportAspect)) > .0001f)
            throw new InvalidDataException("Animated FOV pointer lost the source lens curve.");
        Console.WriteLine("PASS: native camera float layout, message and scripted lens changes, view/receiver routing, ramps/interruption, reverse seek and GLB lens metadata");

        void Check(float time, float expectedAngle)
        {
            if (Math.Abs(lens.Evaluate(time) - expectedAngle) > .0001f)
                throw new InvalidDataException($"Lens mismatch at {time}: {lens.Evaluate(time)} vs {expectedAngle}.");
        }
    }

    private static AnimationKey Command(float time, float duration, float angle) =>
        CinematicTestEvents.Message(time, 99, 60, new(), new(), 1, 0,
            scalar2: duration, scalar1: angle, object1: 7, object2: 8);
}
