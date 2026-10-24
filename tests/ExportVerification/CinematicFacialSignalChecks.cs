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
        VerifyNestedTimelineMessagesUseScheduledClock();

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

    private static void VerifyNestedTimelineMessagesUseScheduledClock()
    {
        const uint worldKey = 0x201;
        const uint clipKey = 0x202;
        const uint eventOwnerKey = 0x203;
        const uint actorKey = 0x204;
        const uint cameraKey = 0x205;
        var actorRoot = new SceneObject
        {
            GaoKey = actorKey,
            GeometryKey = 100,
            Modifiers = [new(4, MorphPreviewChecks.Payload(1))]
        };
        var actor = new SceneActor { Root = actorRoot, Members = [actorRoot] };
        actorRoot.Actor = actor;
        var camera = new SceneObject { GaoKey = cameraKey, Name = "Camera" };
        var eventOwner = new SceneObject { GaoKey = eventOwnerKey };
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
                new() { Bone = -1, Target = eventOwnerKey, Keys = [CinematicTestEvents.Call(1, eventOwnerKey, 0x3B64, 0)] }
            ]
        };
        var nestedMessage = new CinematicTimelineMessage(.5f, eventOwnerKey, 0, 0,
            new Vector3(-4001, -4001, -4001), new Vector3(-4001, -4001, 0), 2, 3, 10, 0, 53);
        var nestedDialogueMessage = new CinematicTimelineMessage(.25f, eventOwnerKey, actorKey, 0,
            Vector3.Zero, Vector3.Zero, 0, 0, 0, 0, 30);
        var channel = new CinematicEmbeddedEventTrack(0, actorKey, -1, 0, 1, 2)
        {
            Messages = [(.25f, nestedDialogueMessage), (.5f, nestedMessage)]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(clipKey, clip);
        document.CinematicEmbeddedEventTracks[(worldKey, eventOwnerKey)] =
        [new(eventOwnerKey, "test owner", worldKey, "SceneEvents", clipKey, [channel])];
        var lipSyncOwner = new CinematicScriptOwner(eventOwnerKey, 0, 0, worldKey, uint.MaxValue,
            clipKey, "lip-sync receiver", ["Cine_lipsynch_auto_Lips_lecture_messages"],
            [new("des_o_acteur", actorKey, "instance", "facial actor")], []);
        document.CinematicScriptOwners[lipSyncOwner.Id] = lipSyncOwner;
        var world = new WorldData { Key = worldKey };
        world.Objects.AddRange([actorRoot, camera, eventOwner]);
        world.Actors.Add(actor);
        world.CinematicClips.Add(clipKey, clip);
        var resolver = new ResourceResolver();
        ((Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resolver)!)
            .Add(100, new Geometry { Key = 100, Positions = [Vector3.Zero] });

        var playback = CinematicPlayback.Discover(document, world, resolver).Single();
        var message = playback.ScriptMessages.Single(item => item.Message.Type == 53);
        var dialogueMessage = playback.ScriptMessages.Single(item => item.Message.Type == 30);
        var facial = CinematicFacialEvent.Decode(message.Message,
            CinematicFacialMessageHandler.AutomaticLipSync);
        var companionFacial = CinematicFacialEvent.Decode(message.Message,
            CinematicFacialMessageHandler.Companion);
        var sallyFacial = CinematicFacialEvent.Decode(message.Message,
            CinematicFacialMessageHandler.Sally);
        if (MathF.Abs(message.Time - 1.5f) > .001f || MathF.Abs(dialogueMessage.Time - 1.25f) > .001f
            || facial?.AutomaticMouth != true
            || facial.ActorTargetKey != 0 || facial.Commands.Any(command => command != null)
            || CinematicPlayback.ResolveFacialTargetKey(CinematicFacialMessageHandler.AutomaticLipSync,
                eventOwnerKey, message.Message.Gao, lipSyncOwner) != actorKey
            || CinematicPlayback.ResolveFacialTargetKey(CinematicFacialMessageHandler.AutomaticLipSync,
                eventOwnerKey, cameraKey, lipSyncOwner) != cameraKey
            || CinematicPlayback.ResolveFacialTargetKey(CinematicFacialMessageHandler.AutomaticLipSync,
                eventOwnerKey, 0, lipSyncOwner with
                {
                    ObjectReferences = [new("des_o_acteur", 0, "instance", null)]
                }) != eventOwnerKey
            || CinematicPlayback.ResolveFacialTargetKey(CinematicFacialMessageHandler.AutomaticLipSync,
                eventOwnerKey, 0, lipSyncOwner with
                {
                    ObjectReferences = [new("des_o_acteur", 1, "instance", "AI_MainActor slot 1")]
                }) != null
            || Channels(companionFacial) != "0,1,4" || Channels(sallyFacial) != "0,1,2,3,4")
            throw new InvalidDataException($"Nested type-53 EVE messages must retain their clock, resolve the callback-specific target, and decode only fields used by the receiver. time={message.Time}; actor={message.Message.Gao:X8}; auto={Channels(facial)}; companion={Channels(companionFacial)}; sally={Channels(sallyFacial)}.");
        playback.Activate();
        playback.Seek(1.5f);
        bool hasMouthEvent = playback.FacialDiagnostics.Any(item =>
            item.Contains("1 mouth start/stop commands", StringComparison.Ordinal));
        playback.Seek(1.25f);
        playback.Seek(1.5f);
        if (!hasMouthEvent || !playback.FacialDiagnostics.Any(item =>
                item.Contains("1 mouth start/stop commands", StringComparison.Ordinal)))
            throw new InvalidDataException("Nested type-53 EVE messages were exposed but did not enter the actor's facial playback timeline.");
        playback.Deactivate();

        document.CinematicScriptOwners[lipSyncOwner.Id] = lipSyncOwner with
        {
            CallbackNames = ["Sally_sa_exec_message_read"]
        };
        var resetMessage = CinematicFacialEvent.Decode(nestedMessage with { Int2 = -1 },
            CinematicFacialMessageHandler.Sally);
        if (resetMessage != null)
            throw new InvalidDataException("Sally type-53 Int2=-1 resets expression mode and must not decode vector fields as new morph commands.");
        var expressionPlayback = CinematicPlayback.Discover(document, world, resolver).Single();
        expressionPlayback.Activate();
        expressionPlayback.Seek(1.5f);
        if (expressionPlayback.FacialDiagnostics.Any()
            || expressionPlayback.ScriptMessages.All(item => item.Message.Type != 53))
            throw new InvalidDataException("Sally and companion facial messages target their execution actor, not the automatic-lipsync OVA override; this synthetic owner does not route the event to the other actor.");
        expressionPlayback.Deactivate();

        document.CinematicScriptOwners[lipSyncOwner.Id] = lipSyncOwner with
        {
            CallbackNames = ["Sally_sa_exec_message_read", "Cine_lipsynch_auto_Lips_lecture_messages"]
        };
        if (CinematicPlayback.Discover(document, world, resolver).Single().FacialDiagnostics.Any())
            throw new InvalidDataException("A script owner with multiple reachable type-53 receiver families must remain unresolved instead of choosing one decoder by priority.");
        Console.WriteLine("PASS: nested EVE message clock, actor routing and callback-specific type-53 semantics");
    }

    private static string Channels(CinematicFacialEvent? facialEvent) => facialEvent == null
        ? "none"
        : string.Join(',', facialEvent.Commands.Where(command => command != null).Select(command => command!.Channel));

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
