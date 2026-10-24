namespace JadeView;

internal sealed class CinematicObjectState
{
    internal const uint Invisible = GameObjectRuntimeFlags.Invisible;
    internal const uint Inactive = GameObjectRuntimeFlags.Inactive;
    private const int ObjectDestroy = 8113;
    private const int ObjectCapabilitiesSet = 8133, ObjectCapabilitiesToggle = 8145;
    private readonly SceneObject[] objects;
    private readonly (StateChange Change, SceneObject[] Targets)[] events;
    private readonly (CapabilityChange Change, SceneObject Target)[] capabilityEvents;
    private readonly HashSet<SceneActor> participants;
    private int appliedEventCount;
    private int appliedCapabilityEventCount;
    private bool hasEvaluation;
    private float lastEvaluationTime;

    internal IEnumerable<float> VisibilityChangeTimes => events
        .Where(item => ((item.Change.SetMask | item.Change.ClearMask) & Invisible) != 0)
        .Select(item => item.Change.Time).Distinct();
    internal IEnumerable<SceneObject> VisibilityTargets => events
        .Where(item => ((item.Change.SetMask | item.Change.ClearMask) & Invisible) != 0)
        .SelectMany(item => item.Targets).Distinct();

    internal CinematicObjectState(AnimationClip source, WorldData world, IEnumerable<SceneActor> actors,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedEventCommands = null)
    {
        objects = world.Objects.ToArray();
        participants = actors.ToHashSet();
        var calls = CinematicLiteralCall.ReadTimeline(source)
            .Concat(nestedEventCommands ?? [])
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();
        events = calls
            .Select(item => Decode(item.Time, item.Call))
            .OfType<StateChange>().OrderBy(change => change.Time)
            .Select(change => (change, Targets(change).ToArray())).ToArray();
        capabilityEvents = calls
            .Where(item => (item.Call.Selector == ObjectCapabilitiesSet && item.Call.Arguments.Length == 2)
                || (item.Call.Selector == ObjectCapabilitiesToggle && item.Call.Arguments.Length == 1))
            .SelectMany(item => objects.Where(sceneObject => sceneObject.GaoKey == item.Call.Target)
                .Select(target => (Change: new CapabilityChange(item.Time,
                    item.Call.Selector == ObjectCapabilitiesSet ? item.Call.Arguments[0] : 0,
                    item.Call.Selector == ObjectCapabilitiesSet ? item.Call.Arguments[1] : 0,
                    item.Call.Selector == ObjectCapabilitiesToggle ? item.Call.Arguments[0] : 0), Target: target)))
            .OrderBy(item => item.Change.Time).ToArray();
    }

    internal void Evaluate(float time)
    {
        float requestedTime = float.IsFinite(time) ? Math.Max(0, time) : 0;
        if (!hasEvaluation || requestedTime < lastEvaluationTime)
        {
            ResetToSourceState();
            appliedEventCount = 0;
            appliedCapabilityEventCount = 0;
            hasEvaluation = true;
        }

        lastEvaluationTime = requestedTime;
        while (appliedEventCount < events.Length && events[appliedEventCount].Change.Time <= requestedTime)
        {
            var (change, targets) = events[appliedEventCount++];
            foreach (var target in targets)
            {
                target.PreviewControlFlags = GameObjectRuntimeFlags.Apply(
                    target.PreviewControlFlags!.Value, change.SetMask, change.ClearMask);
                if ((change.ClearMask & (Invisible | Inactive)) != 0) target.CinematicExcluded = false;
            }
        }
        while (appliedCapabilityEventCount < capabilityEvents.Length
            && capabilityEvents[appliedCapabilityEventCount].Change.Time <= requestedTime)
        {
            var (change, target) = capabilityEvents[appliedCapabilityEventCount++];
            uint capabilities = target.PreviewCapabilities!.Value & ushort.MaxValue;
            capabilities = change.ToggleMask != 0
                ? capabilities ^ (change.ToggleMask & ushort.MaxValue)
                : (capabilities & ~(change.RemoveMask & ushort.MaxValue)) | (change.AddMask & ushort.MaxValue);
            target.PreviewCapabilities = capabilities & ushort.MaxValue;
        }
    }

    private void ResetToSourceState()
    {
        foreach (var sceneObject in objects)
        {
            sceneObject.PreviewControlFlags = sceneObject.ControlFlags;
            sceneObject.PreviewCapabilities = sceneObject.InitialCapabilities;
            sceneObject.CinematicExcluded = sceneObject.Actor is { } actor
                && sceneObject == actor.Root && !actor.IsCinematicWorldResident
                && !participants.Contains(actor);
        }
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
        appliedEventCount = 0;
        appliedCapabilityEventCount = 0;
        hasEvaluation = false;
        lastEvaluationTime = 0;
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
        var change = GameScriptObjectFlags.Decode(call.Target, call.Selector, call.Arguments);
        return change == null ? null : new(time, change.TargetKey,
            change.SetMask, change.ClearMask, change.TargetsGroup);
    }

    private sealed record StateChange(float Time, uint Target, uint SetMask, uint ClearMask, bool Group = false);
    private sealed record CapabilityChange(float Time, uint AddMask, uint RemoveMask, uint ToggleMask);
}
