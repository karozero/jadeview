namespace JadeView;

public sealed partial class CinematicPlayback
{
    internal IEnumerable<string> FacialDiagnostics => facialPlaybacks.Select(playback =>
        {
            int mouthCommands = playback.Events.Count(item => item.Mouth?.AutomaticMouth != null);
            int blinkCommands = playback.Events.Count(item => item.Mouth?.BlinkRequested == true);
            bool missingController = playback.Events.Any(item => item.Mouth?.AutomaticMouth == true)
                && !playback.Previews.Any(snapshot => snapshot.Preview.MouthCycleDefinition != null);
            return $"{playback.Actor.Root.Name}: {playback.Signals.Length} animated facial signals, {mouthCommands} mouth start/stop commands, {blinkCommands} explicit blink triggers"
                + (missingController ? "; mouth controller binding unavailable" : "");
        })
        .Concat(EnsureScriptPlayback().MorphPlayback?.Diagnostics ?? []);

    private static PreviewSnapshot[] CreatePreviewSnapshots(SceneActor actor, ResourceResolver resolver)
    {
        var previews = new List<PreviewSnapshot>();
        foreach (var member in actor.Members.Where(actor.HasMorphAppearance))
        {
            actor.ResolveMorphAppearance(member, candidate =>
                resolver.Geometries.TryGetValue(candidate.RenderGeometryKey, out var geometry)
                    ? FaceMorphPreview.Get(candidate, geometry)?.Expressions.Count ?? 0
                    : 0);
            if (!resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var mesh)) continue;

            var preview = FaceMorphPreview.Get(member, mesh);
            if (preview?.Data is not { Channels.Length: > 0 }) continue;
            previews.Add(new(member, preview, preview.CopyWeights(), preview.CopyChannelState()));
        }
        return previews.ToArray();
    }

    public Dictionary<SceneObject, FaceMorphKeyframe[]> SampleFacialMorphTimelines()
    {
        EnsureScriptPlayback();
        var result = facialPlaybacks.SelectMany(playback => playback.Previews)
            .ToDictionary(snapshot => snapshot.Target, _ => new List<FaceMorphKeyframe>());
        foreach (SceneObject target in objectMorphPlayback?.Targets ?? [])
            result.TryAdd(target, []);
        if (result.Count == 0 || Duration <= 0)
            return result.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        bool resumePlayback = Playing;
        float restoreTime = Time;
        Pause();
        ResetFacialPlayback();
        Time = 0;

        int frameCount = (int)MathF.Ceiling(Duration * FacialSampleRate);
        for (int frame = 0; frame <= frameCount; frame++)
        {
            float sampleTime = Math.Min(frame / FacialSampleRate, Duration);
            Time = sampleTime;
            AdvanceFacialPlayback(sampleTime);
            objectMorphPlayback?.Advance(sampleTime);
            foreach (var playback in facialPlaybacks)
                foreach (var snapshot in playback.Previews)
                    result[snapshot.Target].Add(new(sampleTime, snapshot.Preview.CopyWeights()));
            if (objectMorphPlayback != null)
                foreach (SceneObject target in objectMorphPlayback.Targets)
                    if (target.MorphPreview is { } preview)
                        result[target].Add(new(sampleTime, preview.CopyWeights()));
        }

        Seek(restoreTime);
        if (resumePlayback) Play();
        return result.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private void ResetFacialPlayback()
    {
        objectMorphPlayback?.Reset();
        foreach (var playback in facialPlaybacks)
        {
            playback.NextEvent = 0;
            playback.LastTime = 0;
            playback.ActivePreview = null;
            playback.SallyBlinkStartTime = null;
            playback.SallyBlinkTarget = null;
            foreach (var snapshot in playback.Previews)
            {
                snapshot.Preview.ClearCinematicBlinkOverlay();
                snapshot.Transitions.Clear();
                Restore(snapshot);
                if (snapshot.Preview.MouthCycleDefinition is not { AutoStart: true } definition) continue;
                snapshot.Preview.StartMouthCycle(definition.DefaultFactor, definition.DefaultSpeed);
                playback.ActivePreview = snapshot.Preview;
            }
        }
    }

    private void ClearFacialOverlays()
    {
        foreach (var playback in facialPlaybacks)
        {
            playback.SallyBlinkStartTime = null;
            playback.SallyBlinkTarget = null;
            foreach (var snapshot in playback.Previews)
                snapshot.Preview.ClearCinematicBlinkOverlay();
        }
    }

    private void AdvanceFacialPlayback(float targetTime)
    {
        foreach (var playback in facialPlaybacks)
        {
            if (playback.Previews.Length == 0) continue;
            foreach (var snapshot in playback.Previews)
                snapshot.Preview.ClearCinematicBlinkOverlay();
            while (playback.NextEvent < playback.Events.Length
                && playback.Events[playback.NextEvent].Time <= targetTime)
            {
                var facialEvent = playback.Events[playback.NextEvent++];
                AdvanceFacialState(playback, facialEvent.Time - playback.LastTime);
                playback.LastTime = facialEvent.Time;
                ApplyFacialEvent(playback, facialEvent);
            }
            foreach (var signal in playback.Signals)
                if (targetTime >= signal.Start && targetTime < signal.End)
                    ApplyMorphFactor(playback, signal, targetTime);
            AdvanceFacialState(playback, targetTime - playback.LastTime);
            playback.LastTime = targetTime;
            ApplySallyBlink(playback, targetTime);
            // Facial signals update the mesh preview directly. Invalidate the
            // renderer's skinned-geometry cache without evaluating the full rig
            // a second time in the same cinematic frame.
            if (playback.Events.Length > 0) playback.Actor.InvalidatePose();
        }
    }

    private static void ApplySallyBlink(FacialPlayback playback, float targetTime)
    {
        if (playback.SallyBlinkStartTime is not { } startTime) return;
        float elapsed = targetTime - startTime;
        if (elapsed < 0) return;
        float phase = elapsed * FaceMorphBlinkCurve.PhaseSpeed;
        if (!float.IsFinite(phase) || phase >= FaceMorphBlinkCurve.PhaseLimit)
        {
            playback.SallyBlinkStartTime = null;
            playback.SallyBlinkTarget = null;
            return;
        }

        var targetPreview = playback.Previews.FirstOrDefault(snapshot =>
            snapshot.Target == playback.SallyBlinkTarget && snapshot.Preview.SupportsSallyBlink);
        targetPreview?.Preview.ApplyCinematicBlinkOverlay(phase);
    }

    private static void AdvanceFacialState(FacialPlayback playback, float delta)
    {
        AdvanceMouth(playback, delta);
        if (delta <= 0) return;

        int steps = GetFacialSteps(delta);
        float step = delta / Math.Max(steps, 1);
        foreach (var snapshot in playback.Previews)
            AdvanceTransitions(snapshot, steps, step);
    }

    private static void AdvanceTransitions(PreviewSnapshot snapshot, int steps, float step)
    {
        snapshot.TransitionChannels.Clear();
        foreach (int channel in snapshot.Transitions.Keys)
            snapshot.TransitionChannels.Add(channel);
        foreach (int channel in snapshot.TransitionChannels)
        {
            var transition = snapshot.Transitions[channel];
            for (int index = 0; index < steps && transition.Phase != PcMorphTransitionPhase.Settled; index++)
                transition = snapshot.Preview.AdvanceChannelTransition(channel, transition, step);
            if (transition.Phase == PcMorphTransitionPhase.Settled) snapshot.Transitions.Remove(channel);
            else snapshot.Transitions[channel] = transition;
        }
    }

    private static void AdvanceMouth(FacialPlayback playback, float delta)
    {
        if (playback.ActivePreview == null || delta <= 0) return;
        int steps = GetFacialSteps(delta);
        float step = delta / Math.Max(steps, 1);
        for (int index = 0; index < steps; index++) playback.ActivePreview.Advance(step);
    }

    private static int GetFacialSteps(float delta)
    {
        int steps = (int)MathF.Ceiling(delta * FacialSampleRate);
        if (steps > MaximumFacialSteps)
            throw new InvalidDataException("Cinematic facial seek exceeds the bounded playback interval.");
        return steps;
    }

    private static void Restore(PreviewSnapshot snapshot)
    {
        snapshot.Preview.Reset();
        if (snapshot.Channels != null)
        {
            snapshot.Preview.RestoreChannelState(snapshot.Channels);
            return;
        }
        for (int group = 0; group < snapshot.Weights.Length; group++)
            snapshot.Preview.SetWeight(group, snapshot.Weights[group]);
    }

    private sealed record PreviewSnapshot(SceneObject Target, FaceMorphPreview Preview,
        float[] Weights, FaceMorphChannelState[]? Channels)
    {
        internal Dictionary<int, PcMorphTransitionState> Transitions { get; } = [];
        internal List<int> TransitionChannels { get; } = [];
    }

    private sealed record FacialEvent(float Time, uint TrackTarget, CinematicFacialEvent? Mouth,
        CinematicChannelFormEvent? ChannelForm, CinematicMorphFactorChange? MorphFactor,
        CinematicChannelStateChange? ChannelState = null);

    private sealed record CinematicMorphFactorChange(SceneObject Member, int Channel,
        TransformData Baseline, AnimationTrack[] Tracks, float Start, float End);

    private sealed record CinematicChannelStateChange(SceneObject Member, int Channel,
        bool IsWeight, float Value);

    private sealed class FacialPlayback(SceneActor actor, FacialEvent[] events)
    {
        internal SceneActor Actor { get; } = actor;
        internal PreviewSnapshot[] Previews { get; set; } = [];
        internal FacialEvent[] Events { get; } = events;
        internal CinematicMorphFactorChange[] Signals { get; } = events
            .Select(item => item.MorphFactor).OfType<CinematicMorphFactorChange>().Distinct().ToArray();
        internal int NextEvent { get; set; }
        internal float LastTime { get; set; }
        internal FaceMorphPreview? ActivePreview { get; set; }
        internal float? SallyBlinkStartTime { get; set; }
        internal SceneObject? SallyBlinkTarget { get; set; }
    }

    private const float FacialSampleRate = 60;
    private const int MaximumFacialSteps = 6000;
}
