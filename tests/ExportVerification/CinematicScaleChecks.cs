using System.Numerics;
using JadeView;

internal static class CinematicScaleChecks
{
    internal static void Verify(string outputDirectory)
    {
        var root = new SceneObject { Name = "Actor root", GaoKey = 1 };
        var bone = new SceneObject
        {
            GaoKey = 3,
            ActorNode = new()
            { Local = Matrix4x4.CreateTranslation(Vector3.UnitX) }
        };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        root.Actor = bone.Actor = actor;
        var prop = new SceneObject { Name = "Script-scaled prop", GaoKey = 2 };
        var world = new WorldData();
        world.Objects.AddRange([root, bone, prop]);
        world.Actors.Add(actor);
        var source = new AnimationClip
        {
            Key = 10,
            Loaded = true,
            Cinematic = true,
            Duration = 3,
            CinematicOwnerKey = 1,
            Tracks =
        [new() { Bone = -1, Target = 1, Keys = [new() { Duration = 3, Position = new(10, 0, 0) }] },
            new() { Bone = -1, Target = 2, Keys = [new() { Duration = 3, Position = Vector3.UnitY }] },
            new() { Bone = -1, Target = 0, Keys =
            [CinematicTestEvents.VectorCall(1, 0, 0x1fbe, new(2.5f)),
                CinematicTestEvents.Call(1.5f, 1, 0x1fc0, BitConverter.SingleToUInt32Bits(.75f)),
                CinematicTestEvents.VectorCall(1, 2, 0x1fbe, new(.5f)),
                CinematicTestEvents.VectorCall(2, 0, 0x1fbe, Vector3.One),
                CinematicTestEvents.VectorCall(2.5f, 0, 0x1fbe, new(float.NaN))] }]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(10, source);
        world.CinematicClips.Add(source.Key, source);
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        Check(.5f, 1);
        Check(1.25f, 2.5f);
        Check(1.5f, .75f);
        if (Math.Abs(prop.PreviewMatrix!.Value.M11 - .5f) > .001f)
            throw new InvalidDataException("A scripted prop scale was lost.");
        Check(1.75f, .75f);
        Check(2.75f, 1);
        Check(.5f, 1);
        if (source.Tracks.Count != 3 || root.Transform.Scale != Vector3.One)
            throw new InvalidDataException("Script transform evaluation modified source data.");

        string exportPath = Path.Combine(outputDirectory, "cinematic-scripted-scale.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = source.Key
        });
        using (var glb = new GlbReader(exportPath))
        {
            var nodes = glb.Root.GetProperty("nodes");
            int propNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
                nodes[index].GetProperty("name").GetString() == prop.Name);
            var animation = glb.Root.GetProperty("animations").EnumerateArray().Single();
            var scaleChannel = animation.GetProperty("channels").EnumerateArray().Single(channel =>
                channel.GetProperty("target").GetProperty("node").GetInt32() == propNode
                && channel.GetProperty("target").GetProperty("path").GetString() == "scale");
            var sampler = animation.GetProperty("samplers")[scaleChannel.GetProperty("sampler").GetInt32()];
            var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
            var scales = glb.Accessor(sampler.GetProperty("output").GetInt32());
            int sampleIndex = Array.FindIndex(times, sample => Math.Abs(sample[0] - 1.5f) < .0001f);
            if (sampleIndex < 0 || Math.Abs(scales[sampleIndex][0] - .5f) > .001f)
                throw new InvalidDataException("GLB export omitted the scripted scale applied to a cinematic prop.");

            int rootNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
                nodes[index].GetProperty("name").GetString() == root.Name
                && nodes[index].GetProperty("extras").TryGetProperty("gaoKey", out _)
                && !nodes[index].GetProperty("extras").TryGetProperty("actor", out var actorMarker));
            var rootScaleChannel = animation.GetProperty("channels").EnumerateArray().Single(channel =>
                channel.GetProperty("target").GetProperty("node").GetInt32() == rootNode
                && channel.GetProperty("target").GetProperty("path").GetString() == "scale");
            var rootScaleSampler = animation.GetProperty("samplers")[
                rootScaleChannel.GetProperty("sampler").GetInt32()];
            var rootTimes = glb.Accessor(rootScaleSampler.GetProperty("input").GetInt32());
            var rootScales = glb.Accessor(rootScaleSampler.GetProperty("output").GetInt32());
            int rootSampleIndex = Array.FindIndex(rootTimes, sample => sample[0] == 1.5f);
            if (rootSampleIndex < 0 || Math.Abs(rootScales[rootSampleIndex][0] - .75f) > .001f)
                throw new InvalidDataException("GLB export omitted the scripted OBJ_ZoomSet actor scale.");
        }
        Console.WriteLine("PASS: cinematic ScaleSet/ZoomSet events preview and export for actor roots and scene props");

        void Check(float time, float scale)
        {
            playback.Seek(time);
            if (Math.Abs(root.PreviewMatrix!.Value.M11 - scale) > .001f
                || Vector3.Distance(bone.PreviewMatrix!.Value.Translation, new(10 + scale, 0, 0)) > .001f)
                throw new InvalidDataException($"Scripted scale/hierarchy mismatch at {time}.");
        }
    }
}
