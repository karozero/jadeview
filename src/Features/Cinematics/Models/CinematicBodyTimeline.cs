using System.Numerics;

namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private readonly List<Segment> segments = [];
    public IReadOnlyList<string> Diagnostics => diagnostics;
    private readonly List<string> diagnostics = [];
    public int ActionCount => segments.Count;
    public float? FirstActionTime => segments.FirstOrDefault()?.Start;
    internal IEnumerable<uint> ShapeKeys => segments
        .SelectMany(segment => segment.Action.Items)
        .Select(item => item.ShapeKey)
        .Where(key => key is not 0 and not uint.MaxValue)
        .Distinct();

    internal static CinematicBodyTimeline Create(AnimationClip source, SceneActor actor,
        IReadOnlyDictionary<uint, Dictionary<int, CinematicAction>> libraries)
        => Create(source, actor, ownerKey => libraries.TryGetValue(ownerKey, out var actions)
            ? new(actions, null, "")
            : new(null, null, $"Missing body action kit {ownerKey:X8}."));

    internal static CinematicBodyTimeline Create(AnimationClip source, SceneActor actor,
        uint preferredWorldKey,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> libraries)
        => Create(source, actor, ownerKey => CinematicActionKitScopeResolver.Resolve(ownerKey,
            preferredWorldKey, libraries));

    private static CinematicBodyTimeline Create(AnimationClip source, SceneActor actor,
        Func<uint, CinematicActionKitResolution> resolveKit)
    {
        var timeline = new CinematicBodyTimeline();
        var initialKit = resolveKit(actor.Root.GaoKey).Actions;
        var kit = initialKit != null ? new Dictionary<int, CinematicAction>(initialKit) : [];
        var events = ReadActorEvents(source, actor).OrderBy(action => action.Time);
        foreach (var action in events)
        {
            if (action.Selector is 0x3398 or 0x3399)
            {
                timeline.MergeKit(kit, action, resolveKit);
                continue;
            }
            if (action.Release)
            {
                timeline.segments.RemoveAll(segment => segment.Start > action.Time);
                var running = timeline.segments.LastOrDefault();
                if (running != null) running.End = Math.Min(running.End, action.Time);
                timeline.diagnostics.Add($"Gameplay handoff at {action.Time:F3}s for {actor.Root.Name}; subsequent brain actions are not simulated.");
                continue;
            }
            if (action.FrequencyOnly)
            {
                timeline.ChangeFrequency(action);
                continue;
            }
            if (action.Action < 0 || action.Frequency is < 0 or > 255) continue;
            if (!kit.TryGetValue(action.Action, out var definition))
            {
                timeline.diagnostics.Add($"Missing body action {action.Action} at {action.Time:F3}s for {actor.Root.Name}.");
                continue;
            }
            var current = timeline.segments.LastOrDefault(segment => segment.Start <= action.Time);
            if (!action.Force && current?.PendingActionId == action.Action) continue;
            timeline.segments.RemoveAll(segment => segment.Start > action.Time);
            var previous = timeline.segments.LastOrDefault();
            bool sameRunningAction = !action.Force && previous?.ActionId == action.Action
                && ReferenceEquals(previous.Action, definition)
                && action.Time < previous.End
                && !previous.HasFinishedAt(action.Time);
            if (sameRunningAction) continue;
            timeline.QueueAction(action, definition, kit);
        }
        timeline.DiagnoseUnresolvedBodyActions(source, actor);
        timeline.InitializeMotion(source, actor);
        return timeline;
    }

    private void DiagnoseUnresolvedBodyActions(AnimationClip source, SceneActor actor)
    {
        if (segments.Count > 0 || actor.Bones.Count == 0) return;
        var actionRequests = ReadActorEvents(source, actor)
            .Where(action => action.Selector == 0x3391 && !action.FrequencyOnly && !action.Release)
            .ToArray();
        if (actionRequests.Length == 0)
        {
            DiagnoseMissingSkeletalSource(source, actor);
            return;
        }

        diagnostics.Add($"No cinematic skeletal action resolved for {actor.Root.Name}; "
            + $"{actionRequests.Length} recognized body-action request(s) target this rig. "
            + "The pose may depend on gameplay brain/action state not evaluated by the preview.");
    }

    private void DiagnoseMissingSkeletalSource(AnimationClip source, SceneActor actor)
    {
        bool hasRootMotion = source.Tracks.Any(track => track.Bone < 0
            && track.Target == actor.Root.GaoKey && track.Keys.Count > 0);
        bool hasBonePose = source.Tracks.Any(track => track.Bone >= 0
            && track.Target == actor.Root.GaoKey && track.Keys.Count > 0);
        if (!hasRootMotion || hasBonePose) return;

        diagnostics.Add($"Root motion is present for {actor.Root.Name}, but the cinematic has no skeletal pose track or body-action request for this rig. "
            + "The preview holds its bind pose; gameplay brain/action state is not reconstructed.");
    }

    private static IEnumerable<CinematicActionEvent> ReadActorEvents(AnimationClip source, SceneActor actor) =>
        source.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads
                .SelectMany(payload => CinematicOwnerContext.ForTrack(source, track)
                    .Select(context => CinematicActionEvent.Decode(key, context, payload)))))
            .Where(action => action != null && IsActorTarget(action.Target, actor))
            .Cast<CinematicActionEvent>();

    private static bool IsActorTarget(uint target, SceneActor actor) =>
        target == actor.Root.GaoKey || actor.Members.Any(member => member.GaoKey == target);

    private void MergeKit(Dictionary<int, CinematicAction> destination, CinematicActionEvent action,
        Func<uint, CinematicActionKitResolution> resolveKit)
    {
        var kitResolution = resolveKit(action.KitOwner);
        if (!kitResolution.IsResolved)
        {
            diagnostics.Add($"Missing body action kit {action.KitOwner:X8} at {action.Time:F3}s: {kitResolution.Failure}");
            return;
        }
        var source = kitResolution.Actions!;
        if (action.Action < 0 || action.LastAction < action.Action || action.LastAction > 65535) return;
        for (int slot = action.Action; slot <= action.LastAction; slot++)
        {
            if (source.TryGetValue(slot, out var definition)) destination[slot] = definition;
            else if (action.Selector == 0x3399) destination.Remove(slot);
        }
    }

    internal IEnumerable<(AnimationTrack Track, float Time, Matrix4x4? RootInverse)> Sample(float time, int maxSegment = int.MaxValue)
    {
        var segment = segments.Take(maxSegment == int.MaxValue ? segments.Count : maxSegment + 1)
            .LastOrDefault(segment => segment.Start <= time);
        if (segment?.SampleAt(time) is not { } sample) yield break;
        if (sample.Clip.Error != null) yield break;
        var rootTracks = sample.Clip.Tracks.Where(track => track.Bone < 0 && track.Target == 0);
        var rootPose = CinematicTransformSampler.Evaluate(new TransformData(), rootTracks, sample.Time);
        Matrix4x4? rootInverse = Matrix4x4.Invert(rootPose, out var inverse)
            ? inverse : null;
        foreach (var track in sample.Clip.Tracks.Where(track => track.Bone >= 0))
            yield return (track, sample.Time, rootInverse);
    }

    internal uint? ShapeAt(float time)
    {
        var segment = segments.LastOrDefault(segment => segment.Start <= time);
        var sample = segment?.SampleAt(time);
        if (segment == null || sample is not { } current
            || current.ItemIndex < 0 || current.ItemIndex >= segment.Action.Items.Length)
            return null;
        return segment.Action.Items[current.ItemIndex].ShapeKey;
    }

}
