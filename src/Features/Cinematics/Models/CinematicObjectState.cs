namespace JadeView;

internal sealed class CinematicObjectState
{
    internal const uint Invisible = 0x00200000;
    internal const uint Inactive = 0x00400000;
    private const int ObjectDestroy = 8113, ObjectFlagsSet = 8115;
    private const int ObjectCapabilitiesSet = 8133;
    private const int ObjectInvisibleSet = 8152, ObjectInactiveSet = 8153;
    private const int GroupInvisibleSet = 15300, GroupInactiveSet = 15301;
    private readonly SceneObject[] objects;
    private readonly (StateChange Change, SceneObject[] Targets)[] events;
    private readonly (CapabilityChange Change, SceneObject Target)[] capabilityEvents;
    private readonly HashSet<SceneActor> participants;

    internal IEnumerable<float> VisibilityChangeTimes => events
        .Where(item => ((item.Change.SetMask | item.Change.ClearMask) & Invisible) != 0)
        .Select(item => item.Change.Time).Distinct();
    internal IEnumerable<SceneObject> VisibilityTargets => events
        .Where(item => ((item.Change.SetMask | item.Change.ClearMask) & Invisible) != 0)
        .SelectMany(item => item.Targets).Distinct();

    internal CinematicObjectState(AnimationClip source, WorldData world, IEnumerable<SceneActor> actors)
    {
        objects = world.Objects.ToArray();
        participants = actors.ToHashSet();
        var calls = CinematicLiteralCall.ReadTimeline(source);
        events = calls
            .Select(item => Decode(item.Time, item.Call))
            .OfType<StateChange>().OrderBy(change => change.Time)
            .Select(change => (change, Targets(change).ToArray())).ToArray();
        capabilityEvents = calls
            .Where(item => item.Call is { Selector: ObjectCapabilitiesSet, Arguments.Length: 2 })
            .SelectMany(item => objects.Where(sceneObject => sceneObject.GaoKey == item.Call.Target)
                .Select(target => (Change: new CapabilityChange(item.Time, item.Call.Arguments[0], item.Call.Arguments[1]), Target: target)))
            .OrderBy(item => item.Change.Time).ToArray();
    }

    internal void Evaluate(float time)
    {
        foreach (var sceneObject in objects)
        {
            sceneObject.PreviewControlFlags = sceneObject.ControlFlags;
            sceneObject.PreviewCapabilities = sceneObject.InitialCapabilities;
            sceneObject.CinematicExcluded = sceneObject.Actor is { } actor
                && sceneObject == actor.Root && !actor.IsCinematicWorldResident
                && !participants.Contains(actor);
        }
        foreach (var (change, targets) in events.TakeWhile(item => item.Change.Time <= time))
            foreach (var target in targets)
            {
                target.PreviewControlFlags = (target.PreviewControlFlags!.Value & ~change.ClearMask) | change.SetMask;
                if ((change.ClearMask & (Invisible | Inactive)) != 0) target.CinematicExcluded = false;
            }
        foreach (var (change, target) in capabilityEvents.TakeWhile(item => item.Change.Time <= time))
            target.PreviewCapabilities = (target.PreviewCapabilities!.Value & ~change.RemoveMask) | change.AddMask;
    }

    private IEnumerable<SceneObject> Targets(StateChange change)
    {
        var owners = objects.Where(sceneObject => sceneObject.GaoKey == change.Target);
        if (!change.Group) return owners;
        // GRP_Flag*Set visits the group's explicit table, not hierarchy descendants
        // or every object in an actor. An owner is affected only if listed itself.
        return owners.SelectMany(GroupTargets).Distinct();
    }

    private IEnumerable<SceneObject> GroupTargets(SceneObject owner)
    {
        var memberKeys = owner.GroupMemberKeys.ToHashSet();
        return objects.Where(sceneObject => memberKeys.Contains(sceneObject.GaoKey)
            && (sceneObject.Actor == null || sceneObject == sceneObject.Actor.Root || sceneObject.Actor == owner.Actor));
    }

    internal void Clear()
    {
        foreach (var sceneObject in objects)
        {
            sceneObject.PreviewControlFlags = null;
            sceneObject.PreviewCapabilities = null;
            sceneObject.CinematicExcluded = false;
        }
    }

    private static StateChange? Decode(float time, CinematicLiteralCall? call)
    {
        if (call == null) return null;
        // OBJDestroy hides/deactivates authored objects; it does not remove
        // their archive records. Runtime-generated objects also disappear.
        if (call.Selector == ObjectDestroy && call.Arguments.Length == 0)
            return new(time, call.Target, Invisible | Inactive, 0);
        if (call.Selector == ObjectFlagsSet && call.Arguments.Length == 2)
            return new(time, call.Target, call.Arguments[0] & 0xffff0000, call.Arguments[1] & 0xffff0000);
        bool group = call.Selector is GroupInvisibleSet or GroupInactiveSet;
        bool supported = group || call.Selector is ObjectInvisibleSet or ObjectInactiveSet;
        if (call.Arguments.Length != 1 || !supported) return null;
        uint mask = call.Selector is ObjectInvisibleSet or GroupInvisibleSet ? Invisible : Inactive;
        return call.Arguments[0] != 0 ? new(time, call.Target, mask, 0, group) : new(time, call.Target, 0, mask, group);
    }

    private sealed record StateChange(float Time, uint Target, uint SetMask, uint ClearMask, bool Group = false);
    private sealed record CapabilityChange(float Time, uint AddMask, uint RemoveMask);
}
