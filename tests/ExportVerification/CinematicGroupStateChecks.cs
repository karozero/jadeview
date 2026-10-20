using JadeView;

internal static class CinematicGroupStateChecks
{
    internal static void Verify()
    {
        const uint hiddenInactive = CinematicObjectState.Invisible | CinematicObjectState.Inactive;
        var owner = new SceneObject { GaoKey = 99, GroupMemberKeys = [101, 108], ControlFlags = hiddenInactive };
        var member = new SceneObject { GaoKey = 101, ControlFlags = hiddenInactive };
        var actor = new SceneActor { Root = owner, Members = [owner, member] };
        owner.Actor = member.Actor = actor;
        var unrelated = new SceneObject { GaoKey = 102 };
        var otherMember = new SceneObject { GaoKey = 101, ControlFlags = hiddenInactive };
        var otherActor = new SceneActor { Root = unrelated, Members = [unrelated, otherMember] };
        unrelated.Actor = otherMember.Actor = otherActor;
        var nested = new SceneObject { GaoKey = 108, GroupMemberKeys = [110], ControlFlags = hiddenInactive };
        var descendant = new SceneObject { GaoKey = 110, ControlFlags = hiddenInactive };
        var world = new WorldData();
        world.Objects.AddRange([owner, member, unrelated, otherMember, nested, descendant]);
        var source = new AnimationClip
        {
            Duration = 4,
            Tracks = [new() { Bone = -1, Target = 99, Keys =
            [CinematicTestEvents.Call(1, 99, 15300, 0), CinematicTestEvents.Call(1, 99, 15301, 0),
             CinematicTestEvents.Call(2, 99, 15301, 1), CinematicTestEvents.Call(3, 99, 15300, 1)] }]
        };
        var state = new CinematicObjectState(source, world, [actor]);
        state.Evaluate(1.5f);
        Require(member.PreviewControlFlags == 0 && nested.PreviewControlFlags == 0, "Group activation omitted table members.");
        Require(owner.IsControlHidden && descendant.IsControlHidden && otherMember.IsControlHidden,
            "Group activation affected an unlisted owner, a nested table or another actor instance.");
        state.Evaluate(2.5f);
        Require(!member.IsControlHidden && member.PreviewControlFlags == CinematicObjectState.Inactive,
            "Group inactivity was confused with invisibility.");
        state.Evaluate(3.5f);
        Require(member.IsControlHidden, "Group hide was ignored.");
        state.Evaluate(.5f);
        Require(member.PreviewControlFlags == hiddenInactive, "Backward seek did not restore group state.");
        state.Clear();
        Require(member.PreviewControlFlags == null && member.ControlFlags == hiddenInactive,
            "Group playback modified source state or failed to clear preview state.");
        Console.WriteLine("PASS: native group visibility/activity tables, instance isolation, nonrecursive scope and reverse seek");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
