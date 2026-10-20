using System.Numerics;
using JadeView;

internal static class CinematicStateChecks
{
    internal static void Verify()
    {
        var effect = new SceneObject
        {
            Name = "Conditional effect",
            GaoKey = 99,
            ControlFlags = CinematicObjectState.Invisible | CinematicObjectState.Inactive
        };
        var root = new SceneObject { GaoKey = 1, Name = "Participant" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        var unrelatedRoot = new SceneObject { GaoKey = 2, Name = "Gameplay actor" };
        var unrelated = new SceneActor { Root = unrelatedRoot, Members = [unrelatedRoot] };
        unrelatedRoot.Actor = unrelated;
        var world = new WorldData();
        world.Objects.AddRange([root, unrelatedRoot, effect]);
        world.Actors.AddRange([actor, unrelated]);
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
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        Require(effect.IsControlHidden && unrelatedRoot.IsControlHidden, "Initial conditional objects must be hidden");
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
        var form = CinematicChannelFormEvent.Decode(CinematicTestEvents.Call(1, 99, 8263, 0, 2, 1, 11));
        Require(form?.TargetKey == 99 && form.Channel == 2 && form.Group == 11,
            "Facial channel events must preserve their explicit recipient on shared tracks");
        Console.WriteLine("PASS: conditional object flags, selected-clip actor isolation, backward seek and preview cleanup");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
