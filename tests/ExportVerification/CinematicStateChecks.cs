using System.Numerics;
using JadeView;

internal static class CinematicStateChecks
{
    internal static void Verify(string outputDirectory)
    {
        var effect = new SceneObject
        {
            Name = "Conditional effect",
            GaoKey = 99,
            ControlFlags = CinematicObjectState.Invisible | CinematicObjectState.Inactive
        };
        effect.Name = "Conditional effect";
        var root = new SceneObject { GaoKey = 1, Name = "Participant" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var unrelatedRoot = new SceneObject { GaoKey = 2, Name = "Gameplay actor" };
        var unrelated = new SceneActor { Root = unrelatedRoot, Members = [unrelatedRoot] };
        unrelatedRoot.Actor = unrelated;
        var residentRoot = new SceneObject { GaoKey = 3, Name = "Scene resident actor" };
        var resident = new SceneActor { Root = residentRoot, Members = [residentRoot], IsCinematicWorldResident = true };
        residentRoot.Actor = resident;
        var world = new WorldData();
        world.Objects.AddRange([root, unrelatedRoot, residentRoot, effect]);
        world.Actors.AddRange([actor, unrelated, resident]);
        var source = new AnimationClip
        {
            Key = 10,
            Cinematic = true,
            Loaded = true,
            Duration = 4,
            CinematicOwnerKey = 44,
            Tracks =
            [
                new() { Bone = -1, Target = 1, Keys = [new() { Duration = 4, Position = Vector3.Zero }] },
                new() { Bone = -1, Target = 99, Keys = [new() { Duration = 4, Position = Vector3.Zero }] },
                new() { Bone = -1, Target = 0, Keys =
                [
                    CinematicTestEvents.Call(1, 99, 8115, 0, 0x00600000),
                    CinematicTestEvents.Call(2, 99, 8152, 1),
                    CinematicTestEvents.Call(3, 99, 8113),
                    CinematicTestEvents.Call(3.5f, 99, 8115, 0, 0x00600000)
                ] }
            ]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(source.Key, source);
        world.CinematicClips.Add(source.Key, source);
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        Require(effect.IsControlHidden && unrelatedRoot.IsControlHidden,
            "Initial conditional objects and unrelated imported actors must be hidden");
        Require(!residentRoot.IsControlHidden,
            "Actors originating in the cinematic's gameplay world must retain their serialized visibility");
        playback.Seek(1.5f);
        Require(!effect.IsControlHidden && unrelatedRoot.IsControlHidden, "Only the activated effect should appear");
        playback.Seek(2.5f);
        Require(effect.IsControlHidden, "Explicit hide event was ignored");
        playback.Seek(3.25f);
        Require(effect.IsControlHidden && (effect.PreviewControlFlags & CinematicObjectState.Inactive) != 0,
            "OBJDestroy must hide and deactivate authored objects in preview.");
        playback.Seek(3.75f);
        Require(!effect.IsControlHidden, "A source reactivation did not restore a previously destroyed authored object.");
        playback.Seek(.5f);
        Require(effect.IsControlHidden, "Backward seek must restore the source state");
        playback.Deactivate();
        Require(effect.PreviewControlFlags == null && !unrelatedRoot.CinematicExcluded,
            "Leaving cutscenes must clear preview-only state");
        Require(effect.ControlFlags == 0x00600000, "Source control flags were modified");
        Require(!effect.IsControlHidden, "Asset mode must remain inspectable after the cinematic overlay is removed");

        string exportPath = Path.Combine(outputDirectory, "cinematic-conditional-effect.glb");
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
            int effectNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
                nodes[index].GetProperty("name").GetString() == effect.Name);
            var animation = glb.Root.GetProperty("animations").EnumerateArray().Single();
            var scaleChannel = animation.GetProperty("channels").EnumerateArray().Single(channel =>
                channel.GetProperty("target").GetProperty("node").GetInt32() == effectNode
                && channel.GetProperty("target").GetProperty("path").GetString() == "scale");
            var sampler = animation.GetProperty("samplers")[scaleChannel.GetProperty("sampler").GetInt32()];
            var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
            var scales = glb.Accessor(sampler.GetProperty("output").GetInt32());
            Require(ScaleAt(1.5f) == 1 && ScaleAt(2) == 0 && ScaleAt(3.75f) == 1,
                "GLB export must reproduce source-timed visibility activation and reactivation.");

            float ScaleAt(float time)
            {
                int index = Array.FindIndex(times, sample => sample[0] == time);
                return index < 0 ? -1 : scales[index][0];
            }
        }

        var firstOwner = new SceneObject { GaoKey = 44, InitialCapabilities = 0x03 };
        var secondOwner = new SceneObject { GaoKey = 45, InitialCapabilities = 0x08 };
        var sharedWorld = new WorldData();
        sharedWorld.Objects.AddRange([firstOwner, secondOwner]);
        var sharedClip = new AnimationClip
        {
            CinematicOwnerKey = 44,
            CinematicOwnerKeys = [44, 45],
            Tracks = [new() { Bone = -1, Target = 0, Keys = [CinematicTestEvents.Call(1, 0, 8133, 4, 2)] }]
        };
        var sharedState = new CinematicObjectState(sharedClip, sharedWorld, []);
        sharedState.Evaluate(1);
        Require(firstOwner.PreviewCapabilities == 0x05 && secondOwner.PreviewCapabilities == 0x0c,
            "A target-zero capability command must apply to every serialized owner with independent initial state.");
        sharedState.Evaluate(.5f);
        Require(firstOwner.PreviewCapabilities == 0x03 && secondOwner.PreviewCapabilities == 0x08,
            "Seeking before a shared capability command must restore each owner's initial capability mask.");
        sharedState.Clear();
        Require(firstOwner.PreviewCapabilities == null && secondOwner.PreviewCapabilities == null
            && firstOwner.InitialCapabilities == 0x03 && secondOwner.InitialCapabilities == 0x08,
            "Clearing shared-owner playback must discard previews without changing serialized capabilities.");

        var toggleOwner = new SceneObject { GaoKey = 46, InitialCapabilities = 0x8001 };
        var toggleWorld = new WorldData();
        toggleWorld.Objects.Add(toggleOwner);
        var toggleClip = new AnimationClip
        {
            Tracks = [new() { Bone = -1, Target = 0, Keys =
                [CinematicTestEvents.Call(1, toggleOwner.GaoKey, 8145, 0x10001)] }]
        };
        var toggleState = new CinematicObjectState(toggleClip, toggleWorld, []);
        toggleState.Evaluate(1);
        Require(toggleOwner.PreviewCapabilities == 0x8000,
            "OBJ_CapaNot must toggle the low 16-bit capability mask, matching the retail field width.");
        toggleState.Evaluate(0);
        Require(toggleOwner.PreviewCapabilities == 0x8001,
            "Reverse seeking before OBJ_CapaNot must restore the serialized capability state.");
        toggleState.Clear();
        var form = CinematicChannelFormEvent.Decode(CinematicTestEvents.Call(1, 99, 8263, 0, 2, 1, 11));
        Require(form?.TargetKey == 99 && form.Channel == 2 && form.Group == 11,
            "Facial channel events must preserve their explicit recipient on shared tracks");
        Console.WriteLine("PASS: conditional state and GLB visibility export, shared-owner capabilities, backward seek and preview cleanup");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
