namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private const byte DefaultTransitionFlag = 0x10;
    private const byte ForwardPhaseFlag = 0x01, InversePhaseFlag = 0x02;
    private void QueueAction(CinematicActionEvent request, CinematicAction destination,
        Dictionary<int, CinematicAction> kit)
    {
        var previous = segments.LastOrDefault();
        var sample = previous?.SampleAt(request.Time);
        var outgoing = sample is { } pose
            ? previous!.Action.Items[pose.ItemIndex] : null;
        var transition = outgoing?.Transitions.FirstOrDefault(entry => entry.RequestedAction == request.Action);
        var incomingDefault = destination.Items.FirstOrDefault()?.Transitions
            .FirstOrDefault(entry => entry.RequestedAction == request.Action && (entry.Flags & DefaultTransitionFlag) != 0);
        if (previous != null) transition ??= incomingDefault;
        if (transition is { BlendFrames: > 0 })
            diagnostics.Add($"Source transition at {request.Time:F3}s: {previous?.ActionId} → {request.Action}, "
                + $"{transition.BlendFrames} blend frames (stock skeleton poses).");
        bool redirected = transition != null && transition.Action != request.Action;
        if (redirected && kit.TryGetValue(transition!.Action, out var bridge))
        {
            float startFrame = TransitionStartFrame(request, bridge, transition, previous);
            float duration = bridge.CompletionDuration(request.Frequency, startFrame);
            if (duration > 0 && float.IsFinite(duration))
            {
                segments.Add(WithBlend(new(request.Time, transition.Action, bridge, request.Frequency, startFrame)
                { PendingActionId = request.Action }, transition));
                segments.Add(new(request.Time + duration, request.Action, destination, null, 0));
                return;
            }
        }
        if (redirected)
            diagnostics.Add($"Missing or unplayable transition action {transition!.Action} at {request.Time:F3}s.");
        float destinationFrame = TransitionStartFrame(request, destination, transition, previous);
        segments.Add(WithBlend(new(request.Time, request.Action, destination, request.Frequency, destinationFrame), transition));
    }

    private static float TransitionStartFrame(CinematicActionEvent request, CinematicAction destination,
        CinematicActionTransition? transition, Segment? previous)
    {
        // ACTcompute applies progressive phase first; an explicit nonzero frame overrides it.
        bool mapsPhase = transition != null && (transition.Flags & (ForwardPhaseFlag | InversePhaseFlag)) != 0;
        if (request.StartFrame != 0 || !mapsPhase)
            return request.StartFrame;
        var sample = previous?.SampleAt(request.Time);
        if (sample == null || sample.Value.Clip.Duration <= 0 || destination.Items.Length == 0) return 0;
        var incoming = destination.Items[0].Clip;
        incoming.EnsureLoaded();
        float phase = Math.Clamp(sample.Value.Time / sample.Value.Clip.Duration, 0, 1);
        if ((transition!.Flags & ForwardPhaseFlag) == 0) phase = 1 - phase;
        return phase * incoming.Duration * 60;
    }
}
