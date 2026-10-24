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
        var drawMaskEffect = new SceneObject
        {
            Name = "Draw-mask effect",
            GaoKey = 100,
            DrawFlags = 0x01000000
        };
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
        world.Objects.AddRange([root, unrelatedRoot, residentRoot, effect, drawMaskEffect]);
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
                    CinematicTestEvents.Call(3.5f, 99, 8115, 0, 0x00600000),
                    CinematicTestEvents.Call(1.25f, 100, 8177, 0, 8),
                    CinematicTestEvents.Call(1.75f, 100, 8177, 8, 0)
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
        Require((drawMaskEffect.EffectiveDrawFlags & 8) == 0,
            "The draw-mask fixture should start with its serialized hidden bit.");
        playback.Seek(1.5f);
        Require((drawMaskEffect.EffectiveDrawFlags & 8) == 0,
            "OBJDrawMaskSet must apply its clear mask to the preview draw mask.");
        playback.Seek(2);
        Require((drawMaskEffect.EffectiveDrawFlags & 8) != 0,
            "OBJDrawMaskSet must apply its set mask and restore visibility.");
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
        Require(effect.IsControlHidden && (drawMaskEffect.EffectiveDrawFlags & 8) == 0,
            "Backward seek must restore both control flags and draw mask to source state.");
        playback.Deactivate();
        Require(effect.PreviewControlFlags == null && !unrelatedRoot.CinematicExcluded,
            "Leaving cutscenes must clear preview-only state");
        Require(effect.ControlFlags == 0x00600000, "Source control flags were modified");
        Require(drawMaskEffect.DrawFlags == 0x01000000 && drawMaskEffect.PreviewDrawFlags == null,
            "Playback must not modify serialized draw flags or leave a preview overlay behind.");
        Require(!effect.IsControlHidden, "Asset mode must remain inspectable after the cinematic overlay is removed");

        string exportPath = Path.Combine(outputDirectory, "cinematic-conditional-effect.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = false,
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

            int drawMaskNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
                nodes[index].GetProperty("name").GetString() == drawMaskEffect.Name);
            var drawMaskScaleChannel = animation.GetProperty("channels").EnumerateArray().Single(channel =>
                channel.GetProperty("target").GetProperty("node").GetInt32() == drawMaskNode
                && channel.GetProperty("target").GetProperty("path").GetString() == "scale");
            var drawMaskSampler = animation.GetProperty("samplers")[drawMaskScaleChannel.GetProperty("sampler").GetInt32()];
            var drawMaskTimes = glb.Accessor(drawMaskSampler.GetProperty("input").GetInt32());
            var drawMaskScales = glb.Accessor(drawMaskSampler.GetProperty("output").GetInt32());
            Require(DrawScaleAt(0, drawMaskTimes, drawMaskScales) == 0
                && DrawScaleAt(2, drawMaskTimes, drawMaskScales) == 1,
                "GLB export must include a hidden-at-source object that OBJDrawMaskSet reveals during playback.");

            float ScaleAt(float time)
            {
                int index = Array.FindIndex(times, sample => sample[0] == time);
                return index < 0 ? -1 : scales[index][0];
            }
        }
        VerifyActorComponentVisibility(outputDirectory);

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
        Console.WriteLine("PASS: control/draw-mask state and GLB visibility export, shared-owner capabilities, backward seek and preview cleanup");
    }

    private static void VerifyActorComponentVisibility(string outputDirectory)
    {
        const uint actorKey = 200;
        const uint componentKey = 201;
        const uint geometryKey = 202;
        var root = new SceneObject { Name = "Visibility rig", GaoKey = actorKey };
        var component = new SceneObject
        {
            Name = "Hidden actor component",
            GaoKey = componentKey,
            GeometryKey = geometryKey,
            DrawFlags = 0x01000000
        };
        var actor = new SceneActor { Root = root, Members = [root, component] };
        root.Actor = actor;
        component.Actor = actor;
        component.ActorNode = new() { Parent = actorKey };
        var clip = new AnimationClip
        {
            Key = 203,
            Name = "Actor component reveal",
            Cinematic = true,
            Loaded = true,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = 0,
                Keys = [
                    CinematicTestEvents.Call(1, componentKey, 8177, 8, 0),
                    CinematicTestEvents.Call(1.5f, actorKey, 8177, 0, 8)
                ] }]
        };
        actor.Clip = clip;
        actor.Clips = [clip];
        var world = new WorldData();
        world.Objects.AddRange([root, component]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(clip.Key, clip);
        var resolver = new ResourceResolver();
        var geometries = (Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(resolver)!;
        geometries.Add(geometryKey, new Geometry
        {
            Key = geometryKey,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            Parts = [new(0, [0, 1, 2], [])]
        });
        string path = Path.Combine(outputDirectory, "cinematic-actor-component-visibility.glb");
        SceneExporter.Export(path, world, resolver, new()
        {
            Animations = true,
            GameHidden = false,
            CinematicClipKey = clip.Key,
            Textures = false
        }, root);

        using var glb = new GlbReader(path);
        var nodes = glb.Root.GetProperty("nodes");
        int visualNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
            nodes[index].TryGetProperty("extras", out var extras)
            && extras.TryGetProperty("actorComponentVisual", out var marker)
            && marker.GetBoolean()
            && extras.GetProperty("gaoKey").GetString() == componentKey.ToString("X8"));
        int parentNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
            nodes[index].TryGetProperty("name", out var name) && name.GetString() == component.Name);
        if (!nodes[parentNode].GetProperty("children").EnumerateArray()
            .Any(child => child.GetInt32() == visualNode))
            throw new InvalidDataException("Actor component visibility must be isolated on a render child, not its skeleton joint.");

        var animation = glb.Root.GetProperty("animations").EnumerateArray().Single();
        var channel = animation.GetProperty("channels").EnumerateArray().Single(item =>
            item.GetProperty("target").GetProperty("node").GetInt32() == visualNode
            && item.GetProperty("target").GetProperty("path").GetString() == "scale");
        var sampler = animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];
        var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
        var scales = glb.Accessor(sampler.GetProperty("output").GetInt32());
        int hiddenIndex = Array.FindIndex(times, sample => sample[0] == 0);
        int shownIndex = Array.FindIndex(times, sample => sample[0] == 1);
        if (hiddenIndex < 0 || shownIndex < 0 || scales[hiddenIndex][0] != 0 || scales[shownIndex][0] != 1)
            throw new InvalidDataException("Cinematic draw-mask commands must animate actor component mesh visibility in GLB.");

        int actorVisibilityNode = Enumerable.Range(0, nodes.GetArrayLength()).Single(index =>
            nodes[index].TryGetProperty("extras", out var extras)
            && extras.TryGetProperty("actorVisibility", out var marker)
            && marker.GetBoolean()
            && extras.GetProperty("gaoKey").GetString() == actorKey.ToString("X8"));
        var rootVisibilityChannel = animation.GetProperty("channels").EnumerateArray().Single(item =>
            item.GetProperty("target").GetProperty("node").GetInt32() == actorVisibilityNode
            && item.GetProperty("target").GetProperty("path").GetString() == "scale");
        var rootVisibilitySampler = animation.GetProperty("samplers")[
            rootVisibilityChannel.GetProperty("sampler").GetInt32()];
        var rootVisibilityTimes = glb.Accessor(rootVisibilitySampler.GetProperty("input").GetInt32());
        var rootVisibilityScales = glb.Accessor(rootVisibilitySampler.GetProperty("output").GetInt32());
        int rootVisibleIndex = Array.FindIndex(rootVisibilityTimes, sample => sample[0] == 0);
        int rootHiddenIndex = Array.FindIndex(rootVisibilityTimes, sample => sample[0] == 1.5f);
        if (rootVisibleIndex < 0 || rootHiddenIndex < 0
            || rootVisibilityScales[rootVisibleIndex][0] != 1
            || rootVisibilityScales[rootHiddenIndex][0] != 0)
            throw new InvalidDataException("Actor-root visibility must animate a wrapper, preserving skeletal joint transforms.");
    }

    private static float DrawScaleAt(float time, float[][] times, float[][] scales)
    {
        int index = Array.FindIndex(times, sample => sample[0] == time);
        return index < 0 ? -1 : scales[index][0];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
