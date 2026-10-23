using System.Numerics;
using JadeView;

internal static class CinematicCompanionActionRequestChecks
{
    internal static void Verify()
    {
        const uint worldKey = 0x100;
        const uint actionKitKey = 0x200;
        var root = new SceneObject { GaoKey = 0x7100596B, Name = "Companion" };
        var bone = new SceneObject
        {
            GaoKey = 0x7100596C,
            Name = "Companion bone",
            ActorNode = new() { Parent = root.GaoKey, Local = Matrix4x4.Identity }
        };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        root.Actor = bone.Actor = actor;

        var companionOwner = Owner(root.GaoKey, worldKey, ["companion_co_exec_get_message"]);
        var unrelatedOwner = Owner(0x300, worldKey, ["unrelated_callback"]);
        var boneOwnerWithoutHandler = Owner(bone.GaoKey, worldKey, ["unrelated_callback"]);
        var candidates = new[]
        {
            Message(root.GaoKey, messageType: 49, status: 3, action: 201),
            Message(root.GaoKey, messageType: 49, status: 4, action: 202),
            Message(root.GaoKey, messageType: 49, status: 3, action: -1),
            Message(root.GaoKey, messageType: 47, status: 3, action: 203),
            Message(unrelatedOwner.OwnerKey, messageType: 49, status: 3, action: 204),
            Message(bone.GaoKey, messageType: 49, status: 3, action: 205)
        };
        var resolvedCalls = CinematicCompanionActionRequestResolver.Resolve(candidates, actor, worldKey,
            [companionOwner, unrelatedOwner, boneOwnerWithoutHandler]);
        Require(resolvedCalls is [{ Time: 0.5f, Call.Target: 0x7100596B,
                Call.Selector: 0x3391, Call.Arguments: [201] }],
            "Only an immediate companion status message with a resolved companion receiver callback may request a body action.");

        var track = new AnimationTrack
        {
            Bone = -1,
            Target = root.GaoKey,
            Keys = [new() { Time = 0, Duration = 2, Position = Vector3.Zero }]
        };
        var clip = new AnimationClip { Cinematic = true, Duration = 2, Tracks = [track] };
        var merge = new CinematicLiteralCall(root.GaoKey, 0x3398,
            [actionKitKey, 200, 230]);
        var kitActions = new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
        {
            [(worldKey, actionKitKey)] = new() { [201] = Action() }
        };
        var bodyTimeline = CinematicBodyTimeline.Create(clip, actor, worldKey, kitActions,
            [(.5f, merge), .. resolvedCalls]);
        if (bodyTimeline.ActionCount != 1 || !bodyTimeline.Sample(.5f).Any())
            throw new InvalidDataException("The event-track action-kit merge must resolve the following companion action request.");

        var document = new SceneDocument();
        var world = new WorldData { Key = worldKey, Name = "Companion event fixture" };
        var camera = new SceneObject { GaoKey = 0x500, Name = "Camera" };
        world.Objects.AddRange([root, bone, camera]);
        world.Actors.Add(actor);
        var parentClip = new AnimationClip
        {
            Key = 0x600,
            CinematicWorldKey = worldKey,
            Duration = 2,
            Loaded = true,
            Cinematic = true,
            SceneMotion = true,
            Tracks =
            [
                new() { Bone = -1, Target = 0x400, Keys = [CallAllEventTracks()] },
                new() { Bone = -1, Target = root.GaoKey,
                    Keys = [new() { Time = 0, Duration = 2, Position = Vector3.Zero }] },
                new() { Bone = -1, Target = camera.GaoKey,
                    Keys = [new() { Time = 0, Duration = 2, Position = Vector3.UnitX }] }
            ]
        };
        world.CinematicClips.Add(parentClip.Key, parentClip);
        document.Worlds.Add(world);
        document.CinematicClips.Add(parentClip.Key, parentClip);
        document.CinematicScriptOwners[new(worldKey, root.GaoKey)] = companionOwner;
        document.CinematicScriptOwners[new(worldKey, 0x400)] = Owner(0x400, worldKey, ["Cine_Events_cine_init"]) with
        {
            SceneEventsKey = 0x401
        };
        document.CinematicEmbeddedEventTracks[(worldKey, 0x400)] =
        [new CinematicEmbeddedEventTrackList(0x400, "scripted actor actions", worldKey, "SceneEvents", 0x401,
            [new CinematicEmbeddedEventTrack(0, root.GaoKey, -1, 0, 2, 2)
            {
                Calls = [(.5f, merge), .. resolvedCalls]
            }])];
        document.CinematicActionKitsByOwner[(worldKey, actionKitKey)] = kitActions[(worldKey, actionKitKey)];
        var playback = CinematicPlayback.Discover(document, world, new ResourceResolver()).Single();
        playback.Activate();
        bool playbackUsesMergedCompanionAction = actor.Clip?.CinematicBody?.ActionCount == 1;
        playback.Deactivate();
        if (!playbackUsesMergedCompanionAction)
            throw new InvalidDataException("Cinematic playback did not apply the nested EVE companion action request.");

        Console.WriteLine("PASS: callback-scoped companion messages resolve body actions after same-time EVE action-kit merges");
    }

    private static CinematicScriptOwner Owner(uint key, uint worldKey, string[] callbacks) => new(
        key, 0, 0, worldKey, uint.MaxValue, uint.MaxValue, "script owner", callbacks, [], []);

    private static (float Time, CinematicLiteralCall Call) Message(uint target, int messageType,
        int status, int action)
    {
        uint[] arguments = [unchecked((uint)messageType), 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            unchecked((uint)status), unchecked((uint)action)];
        return (.5f, new(target, 0x1F78, arguments));
    }

    private static CinematicAction Action() => new(0,
    [new(new AnimationClip
    {
        Loaded = true,
        Duration = 1,
        Tracks = [new() { Bone = 0, Keys =
            [new() { Time = 0, Duration = 1, Position = Vector3.Zero },
             new() { Time = 1, Position = Vector3.UnitY }] }]
    }, 0, 64, 1)]);

    private static AnimationKey CallAllEventTracks()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(4);
        writer.Write(0u); writer.Write(0x01000028u);
        writer.Write(0u); writer.Write(0x02000806u);
        writer.Write(0u); writer.Write(0x03003B60u);
        writer.Write(0u); writer.Write(0x1F000000u);
        return new() { Time = 0, OpaquePayloads = [new("word-pair-list", 0, stream.ToArray())] };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
