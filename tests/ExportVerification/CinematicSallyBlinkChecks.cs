using System.Numerics;
using System.Reflection;
using JadeView;

internal static class CinematicSallyBlinkChecks
{
    internal static void Verify(string output)
    {
        const uint worldKey = 0x301;
        const uint clipKey = 0x302;
        const uint actorKey = 0x303;
        const uint cameraKey = 0x304;
        const uint geometryKey = 0x620030FB;
        var geometry = new Geometry
        {
            Key = geometryKey,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            Parts = [new(0, [0, 1, 2], [0, 1, 2])]
        };
        var actorRoot = new SceneObject
        {
            GaoKey = actorKey,
            GeometryKey = geometryKey,
            Modifiers = [new(4, SallyMorphPayload(geometry.Positions.Length))]
        };
        var actor = new SceneActor { Root = actorRoot, Members = [actorRoot], Bones = [actorRoot] };
        actorRoot.Actor = actor;
        var camera = new SceneObject { GaoKey = cameraKey, Name = "Camera" };
        var clip = new AnimationClip
        {
            Loaded = true,
            Key = clipKey,
            Duration = 4,
            CinematicWorldKey = worldKey,
            Cinematic = true,
            SceneMotion = true,
            Tracks =
            [
                new() { Bone = -1, Target = actorKey, Keys = [new() { Duration = 4 }] },
                new() { Bone = -1, Target = cameraKey, Keys = [new() { Duration = 4, Position = Vector3.UnitZ }] },
                new() { Bone = -1, Target = actorKey, Keys = [CinematicTestEvents.Call(1, actorKey, 0x3B64, 0)] }
            ]
        };
        var blinkMessage = new CinematicTimelineMessage(.25f, actorKey, actorKey, 0,
            Vector3.Zero, Vector3.Zero, 0, 0, 0, 1, 53);
        var eventTrack = new CinematicEmbeddedEventTrack(0, actorKey, -1, 0, 1, 2)
        {
            Messages = [(.25f, blinkMessage)]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(clipKey, clip);
        document.CinematicEmbeddedEventTracks[(worldKey, actorKey)] =
        [new(actorKey, "Sally blink owner", worldKey, "SceneEvents", clipKey, [eventTrack])];
        var owner = new CinematicScriptOwner(actorKey, 0, 0, worldKey, uint.MaxValue,
            clipKey, "Sally blink owner", ["Sally_sa_exec_message_read"], [], []);
        document.CinematicScriptOwners[owner.Id] = owner;
        var world = new WorldData { Key = worldKey };
        world.Objects.AddRange([actorRoot, camera]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(clipKey, clip);
        var resolver = new ResourceResolver();
        ((Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resolver)!)
            .Add(geometryKey, geometry);

        var preview = FaceMorphPreview.Get(actorRoot, geometry)
            ?? throw new InvalidDataException("Synthetic Sally morph controller was not created.");
        if (!preview.SupportsSallyBlink)
            throw new InvalidDataException("Synthetic type-4 morph payload did not match the qualified Sally profile.");
        preview.SetWeight(1, .2f);
        preview.SetWeight(2, .4f);
        preview.SetWeight(10, .3f);
        var decoded = CinematicFacialEvent.Decode(blinkMessage, CinematicFacialMessageHandler.Sally);
        if (decoded?.BlinkRequested != true || decoded.AutomaticMouth != null
            || decoded.Commands.Any(command => command != null)
            || CinematicFacialEvent.Decode(blinkMessage with { Int2 = -2 },
                CinematicFacialMessageHandler.Sally)?.BlinkRequested != true)
            throw new InvalidDataException("A nonzero, non-reset Sally Int2 must decode as an explicit blink trigger independently of mouth and channel commands.");

        const float scheduledBlinkTime = 1.25f;
        var playback = CinematicPlayback.Discover(document, world, resolver).Single();
        if (!playback.FacialDiagnostics.Single().Contains("1 explicit blink triggers", StringComparison.Ordinal))
            throw new InvalidDataException("Sally's positive Int2 blink trigger was not retained in actor facial playback.");

        playback.Activate();
        playback.Seek(scheduledBlinkTime - .01f);
        VerifyWeights(.2f, .4f, .3f, "before scheduled trigger");
        playback.Seek(scheduledBlinkTime + MathF.PI / (2 * FaceMorphBlinkCurve.PhaseSpeed));
        VerifyWeights(1, 1, .3f, "at explicit blink peak");
        playback.Seek(scheduledBlinkTime + FaceMorphBlinkCurve.Duration + .01f);
        VerifyWeights(.2f, .4f, .3f, "after explicit blink");
        playback.Seek(scheduledBlinkTime + .02f);
        VerifyWeights(FaceMorphBlinkCurve.Weight(.2f, .3f),
            FaceMorphBlinkCurve.Weight(.4f, .3f), .3f, "after backward seek into blink");

        var sampledTimelines = playback.SampleFacialMorphTimelines();
        if (!sampledTimelines.TryGetValue(actorRoot, out var sampledKeys) || sampledKeys.Length <= 2)
            throw new InvalidDataException("Cinematic export sampling omitted the actor's Sally blink timeline.");
        string glbPath = Path.Combine(output, "sally-cinematic-explicit-blink.glb");
        SceneExporter.Export(glbPath, world, resolver, new()
        {
            Animations = true,
            Textures = false,
            CinematicMorphTimelines = sampledTimelines
        }, actorRoot);
        VerifyExportedBlink(glbPath, scheduledBlinkTime);

        playback.Deactivate();
        VerifyWeights(.2f, .4f, .3f, "after playback cleanup");
        Console.WriteLine("PASS: Sally explicit type-53 blink, source-time seek, GLB morph animation and cleanup");

        void VerifyWeights(float upper, float lower, float unrelated, string stage)
        {
            if (MathF.Abs(preview.GetWeight(1) - upper) > .00002f
                || MathF.Abs(preview.GetWeight(2) - lower) > .00002f
                || MathF.Abs(preview.GetWeight(10) - unrelated) > .00002f)
                throw new InvalidDataException($"Sally blink {stage} produced weights "
                    + $"{preview.GetWeight(1):F5}/{preview.GetWeight(2):F5}/{preview.GetWeight(10):F5}.");
        }
    }

    private static void VerifyExportedBlink(string path, float triggerTime)
    {
        using var glb = new GlbReader(path);
        var animation = glb.Root.GetProperty("animations").EnumerateArray()
            .Single(item => item.GetProperty("channels").EnumerateArray()
                .Any(channel => channel.GetProperty("target").GetProperty("path").GetString() == "weights"));
        var weightChannel = animation.GetProperty("channels").EnumerateArray()
            .Single(channel => channel.GetProperty("target").GetProperty("path").GetString() == "weights");
        var sampler = animation.GetProperty("samplers")[weightChannel.GetProperty("sampler").GetInt32()];
        float[] times = glb.Accessor(sampler.GetProperty("input").GetInt32())
            .Select(value => value[0]).ToArray();
        float[][] values = glb.Accessor(sampler.GetProperty("output").GetInt32());
        if (values.Length != times.Length * 55)
            throw new InvalidDataException("Cinematic blink GLB weights do not match Sally's 55 morph groups.");

        int frameCount = times.Length;
        int triggerIndex = Enumerable.Range(0, frameCount)
            .MinBy(index => MathF.Abs(times[index] - triggerTime));
        int peakIndex = Enumerable.Range(0, frameCount)
            .Where(index => times[index] >= triggerTime && times[index] <= triggerTime + FaceMorphBlinkCurve.Duration)
            .MaxBy(index => values[index * 55 + 1][0]);
        int endIndex = Enumerable.Range(0, frameCount)
            .First(index => times[index] > triggerTime + FaceMorphBlinkCurve.Duration);
        float upperAtPeak = values[peakIndex * 55 + 1][0];
        float lowerAtPeak = values[peakIndex * 55 + 2][0];
        if (MathF.Abs(times[triggerIndex] - triggerTime) > .00001f
            || MathF.Abs(values[triggerIndex * 55 + 1][0] - .2f) > .00002f
            || MathF.Abs(values[triggerIndex * 55 + 2][0] - .4f) > .00002f
            || upperAtPeak < .99f || lowerAtPeak < .99f
            || MathF.Abs(values[endIndex * 55 + 1][0] - .2f) > .00002f
            || MathF.Abs(values[endIndex * 55 + 2][0] - .4f) > .00002f
            || MathF.Abs(values[peakIndex * 55 + 10][0] - .3f) > .00002f)
            throw new InvalidDataException("GLB cinematic facial animation did not preserve the timed blink envelope and unrelated morph baseline.");
    }

    private static byte[] SallyMorphPayload(int vertexCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)vertexCount);
        writer.Write(55);
        writer.Write(6);
        for (int group = 0; group < 55; group++)
        {
            bool blinkGroup = group is 1 or 2;
            writer.Write(blinkGroup ? 1 : 0);
            if (blinkGroup) writer.Write(0);
            if (!blinkGroup) continue;
            writer.Write(group == 1 ? .01f : -.01f);
            writer.Write(.02f);
            writer.Write(0f);
        }
        for (int channel = 0; channel < 6; channel++)
        {
            uint[] references = channel switch
            {
                0 => [1, 1],
                1 => [2, 2],
                _ => []
            };
            writer.Write(references.Length);
            writer.Write(BitConverter.SingleToUInt32Bits(0));
            writer.Write(BitConverter.SingleToUInt32Bits(0));
            foreach (uint group in references) writer.Write(group);
        }
        return stream.ToArray();
    }
}
