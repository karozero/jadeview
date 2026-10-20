using System.Numerics;

namespace JadeView;

internal sealed class CinematicLightningTimeline
{
    private const int CapabilitySet = 8133;
    private readonly LightningTrack[] tracks;

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
            return new LightningTrack(controller, owner, destination, capabilityActor, controllerChanges);
        }).OfType<LightningTrack>().ToArray();
    }

    internal CinematicLightningState[] Evaluate(float time)
    {
        var states = new List<CinematicLightningState>();
        foreach (var track in tracks)
        {
            var transitions = Transitions(track, time);
            if (!TrySample(track.Controller, transitions, time, out uint color, out float opacity)) continue;
            var start = track.Owner.RenderMatrix.Translation;
            var end = track.Destination.RenderMatrix.Translation;
            if (!float.IsFinite(Vector3.DistanceSquared(start, end))) continue;
            states.Add(new(start, end, color, opacity, track.Controller.Width,
                track.Controller.Radius, track.Controller.SegmentCount, track.Controller.OwnerKey, time));
        }
        return states.ToArray();
    }

    private static List<(float Time, bool Active)> Transitions(LightningTrack track, float until)
    {
        var controller = track.Controller;
        uint capabilities = track.CapabilityActor.InitialCapabilities;
        bool capabilityActive = HasCapability(capabilities, controller.CapabilityMask);
        bool active = controller.Mode == 1 || capabilityActive;
        var transitions = new List<(float Time, bool Active)> { (0, active) };
        if (controller.Mode == 1)
        {
            AddPeriodicTransitions(transitions, controller, until);
            return transitions;
        }

        if (controller.Mode == 0)
        {
            foreach (var change in track.Changes.Where(change => change.Time <= until))
            {
                bool wasEnabled = capabilityActive;
                capabilityActive = ApplyCapability(ref capabilities, change, controller.CapabilityMask);
                if (wasEnabled != capabilityActive) transitions.Add((change.Time, capabilityActive));
            }
            return transitions;
        }

        float nextToggle = capabilityActive ? DelayAfter(true, controller) : float.PositiveInfinity;
        foreach (var change in track.Changes.Where(change => change.Time <= until))
        {
            AdvanceTimerUntil(change.Time);
            bool wasEnabled = capabilityActive;
            capabilityActive = ApplyCapability(ref capabilities, change, controller.CapabilityMask);
            if (wasEnabled == capabilityActive) continue;
            active = capabilityActive;
            transitions.Add((change.Time, active));
            nextToggle = capabilityActive ? change.Time + DelayAfter(true, controller) : float.PositiveInfinity;
        }
        AdvanceTimerUntil(until);
        return transitions.OrderBy(item => item.Time).ToList();

        void AdvanceTimerUntil(float time)
        {
            int iterations = 0;
            while (capabilityActive && nextToggle <= time && iterations++ < 4096)
            {
                active = !active;
                transitions.Add((nextToggle, active));
                float delay = DelayAfter(active, controller);
                nextToggle = delay > 0 ? nextToggle + delay : float.PositiveInfinity;
            }
        }
    }

    private static void AddPeriodicTransitions(List<(float Time, bool Active)> transitions,
        CinematicLightningController controller, float until)
    {
        bool active = transitions[^1].Active;
        float delay = DelayAfter(active, controller);
        float nextToggle = delay > 0 ? delay : float.PositiveInfinity;
        int iterations = 0;
        while (nextToggle <= until && iterations++ < 4096)
        {
            active = !active;
            transitions.Add((nextToggle, active));
            delay = DelayAfter(active, controller);
            nextToggle = delay > 0 ? nextToggle + delay : float.PositiveInfinity;
        }
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
        IReadOnlyList<(float Time, bool Active)> transitions, float time, out uint color, out float opacity)
    {
        bool active = transitions[0].Active;
        bool wasEverActive = active;
        float changedAt = transitions[0].Time;
        for (int index = 1; index < transitions.Count && transitions[index].Time <= time; index++)
        {
            active = transitions[index].Active;
            wasEverActive |= active;
            changedAt = transitions[index].Time;
        }
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

    private sealed record LightningTrack(CinematicLightningController Controller, SceneObject Owner,
        SceneObject Destination, SceneObject CapabilityActor, CapabilityChange[] Changes);
    private sealed record CapabilityChange(float Time, uint SetMask, uint RemoveMask);
}
