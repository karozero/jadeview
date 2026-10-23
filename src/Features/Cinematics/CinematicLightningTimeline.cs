using System.Numerics;

namespace JadeView;

internal sealed class CinematicLightningTimeline
{
    private const int CapabilitySet = 8133;
    private const int MaximumTransitionsPerTrack = 4096;
    private readonly LightningTrack[] tracks;
    private readonly List<CinematicLightningState> activeStates = [];

    internal CinematicLightningTimeline(AnimationClip source, WorldData world,
        IReadOnlyDictionary<uint, CinematicLightningController> controllers)
    {
        var changes = CinematicLiteralCall.ReadTimeline(source)
            .Where(item => item.Call is { Selector: CapabilitySet, Arguments.Length: 2 }).ToArray();
        tracks = controllers.Values.Select(controller =>
        {
            var owner = world.Objects.FirstOrDefault(item => item.GaoKey == controller.OwnerKey);
            if (owner == null) return null;
            var destination = world.Objects.FirstOrDefault(item => item.GaoKey == controller.DestinationKey) ?? owner;
            var capabilityActor = world.Objects.FirstOrDefault(item => item.GaoKey == controller.CapabilityActorKey) ?? owner;
            var controllerChanges = changes.Where(item => item.Call!.Target == capabilityActor.GaoKey)
                .Select(item => new CapabilityChange(item.Time,
                    unchecked((uint)item.Call!.Arguments[0]), unchecked((uint)item.Call.Arguments[1])))
                .ToArray();
            var track = new LightningTrack(controller, owner, destination, capabilityActor, controllerChanges);
            float duration = float.IsFinite(source.Duration) ? Math.Max(0, source.Duration) : 0;
            track.Transitions = BuildTransitions(track, duration);
            return track;
        }).OfType<LightningTrack>().ToArray();
    }

    internal IReadOnlyList<CinematicLightningState> Evaluate(float time)
    {
        activeStates.Clear();
        foreach (var track in tracks)
        {
            if (!TrySample(track.Controller, track.Transitions, time, out uint color, out float opacity)) continue;
            var start = track.Owner.RenderMatrix.Translation;
            var end = track.Destination.RenderMatrix.Translation;
            if (!float.IsFinite(Vector3.DistanceSquared(start, end))) continue;
            activeStates.Add(new(start, end, color, opacity, track.Controller.Width,
                track.Controller.Radius, track.Controller.SegmentCount, track.Controller.OwnerKey, time));
        }
        return activeStates;
    }

    private static LightningTransition[] BuildTransitions(LightningTrack track, float until)
    {
        var controller = track.Controller;
        uint capabilities = track.CapabilityActor.InitialCapabilities;
        bool capabilityActive = HasCapability(capabilities, controller.CapabilityMask);
        bool active = controller.Mode == 1 || capabilityActive;
        var transitions = new List<LightningTransition> { new(0, active, active) };
        if (controller.Mode == 1)
        {
            AddPeriodicTransitions(transitions, controller, until);
            return transitions.ToArray();
        }

        if (controller.Mode == 0)
        {
            foreach (var change in track.Changes.Where(change => change.Time <= until))
            {
                bool wasEnabled = capabilityActive;
                capabilityActive = ApplyCapability(ref capabilities, change, controller.CapabilityMask);
                if (wasEnabled != capabilityActive)
                    AddTransition(transitions, change.Time, capabilityActive);
            }
            return transitions.ToArray();
        }

        float nextToggle = capabilityActive ? DelayAfter(true, controller) : float.PositiveInfinity;
        foreach (var change in track.Changes.Where(change => change.Time <= until))
        {
            AdvanceTimerUntil(change.Time);
            bool wasEnabled = capabilityActive;
            capabilityActive = ApplyCapability(ref capabilities, change, controller.CapabilityMask);
            if (wasEnabled == capabilityActive) continue;
            active = capabilityActive;
            AddTransition(transitions, change.Time, active);
            nextToggle = capabilityActive ? change.Time + DelayAfter(true, controller) : float.PositiveInfinity;
        }
        AdvanceTimerUntil(until);
        return transitions.OrderBy(item => item.Time).ToArray();

        void AdvanceTimerUntil(float time)
        {
            int iterations = 0;
            while (capabilityActive && nextToggle <= time && iterations++ < MaximumTransitionsPerTrack)
            {
                active = !active;
                AddTransition(transitions, nextToggle, active);
                float delay = DelayAfter(active, controller);
                nextToggle = delay > 0 ? nextToggle + delay : float.PositiveInfinity;
            }
        }
    }

    private static void AddPeriodicTransitions(List<LightningTransition> transitions,
        CinematicLightningController controller, float until)
    {
        bool active = transitions[^1].Active;
        float delay = DelayAfter(active, controller);
        float nextToggle = delay > 0 ? delay : float.PositiveInfinity;
        int iterations = 0;
        while (nextToggle <= until && iterations++ < MaximumTransitionsPerTrack)
        {
            active = !active;
            AddTransition(transitions, nextToggle, active);
            delay = DelayAfter(active, controller);
            nextToggle = delay > 0 ? nextToggle + delay : float.PositiveInfinity;
        }
    }

    private static void AddTransition(List<LightningTransition> transitions, float time, bool active)
    {
        bool wasEverActive = transitions.Count > 0 && transitions[^1].WasEverActive || active;
        transitions.Add(new(time, active, wasEverActive));
    }

    private static float DelayAfter(bool active, CinematicLightningController controller) => active
        ? NextDelay(controller.OnDelayMin, controller.OnDelayMax)
        : NextDelay(controller.OffDelayMin, controller.OffDelayMax);

    private static bool ApplyCapability(ref uint capabilities, CapabilityChange change, int mask)
    {
        capabilities = (capabilities | (change.SetMask & ushort.MaxValue))
            & ~(change.RemoveMask & ushort.MaxValue);
        return HasCapability(capabilities, mask);
    }

    private static bool HasCapability(uint capabilities, int mask) =>
        (ushort)mask != 0 && (capabilities & (ushort)mask) != 0;

    private static bool TrySample(CinematicLightningController controller,
        IReadOnlyList<LightningTransition> transitions, float time, out uint color, out float opacity)
    {
        int transitionIndex = LastTransitionAtOrBefore(transitions, time);
        transitionIndex = Math.Max(transitionIndex, 0);
        var transition = transitions[transitionIndex];
        bool active = transition.Active;
        bool wasEverActive = transition.WasEverActive;
        float changedAt = transition.Time;
        float fade = active ? controller.FadeIn : controller.FadeOut;
        float amount = fade <= 0 ? 1 : Math.Clamp((time - changedAt) / fade, 0, 1);
        if (!active && controller.OffColor == 0)
        {
            color = 0;
            if (!wasEverActive)
            {
                opacity = 0;
                return false;
            }
            opacity = 1 - amount;
            return opacity > 0;
        }
        if (!active)
        {
            color = controller.OffColor;
            opacity = 1;
            return true;
        }
        uint onColor = controller.OnColorFirst;
        if (controller.LightningMode != 0)
        {
            float blend = (MathF.Sin(time * controller.Frequency * MathF.PI) + 1) * .5f;
            onColor = Blend(controller.OnColorFirst, controller.OnColorSecond, blend);
        }
        color = onColor;
        opacity = amount;
        return opacity > 0;
    }

    private static int LastTransitionAtOrBefore(IReadOnlyList<LightningTransition> transitions,
        float time)
    {
        int low = 0;
        int high = transitions.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (transitions[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private static uint Blend(uint first, uint second, float amount)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int channel = (int)((first >> shift) & 255);
            int other = (int)((second >> shift) & 255);
            result |= (uint)Math.Clamp((int)MathF.Round(channel + (other - channel) * amount), 0, 255) << shift;
        }
        return result;
    }

    private static float NextDelay(float minimum, float maximum) => Math.Max(0, (minimum + maximum) * .5f);

    private sealed class LightningTrack(CinematicLightningController controller, SceneObject owner,
        SceneObject destination, SceneObject capabilityActor, CapabilityChange[] changes)
    {
        internal CinematicLightningController Controller { get; } = controller;
        internal SceneObject Owner { get; } = owner;
        internal SceneObject Destination { get; } = destination;
        internal SceneObject CapabilityActor { get; } = capabilityActor;
        internal CapabilityChange[] Changes { get; } = changes;
        internal LightningTransition[] Transitions { get; set; } = [];
    }
    private readonly record struct LightningTransition(float Time, bool Active, bool WasEverActive);
    private sealed record CapabilityChange(float Time, uint SetMask, uint RemoveMask);
}
