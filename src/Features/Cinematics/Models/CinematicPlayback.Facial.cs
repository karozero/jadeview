namespace JadeView;

public sealed partial class CinematicPlayback
{
    internal IEnumerable<string> FacialDiagnostics => facialPlaybacks.Select(playback =>
    {
        int mouthCommands = playback.Events.Count(item => item.Mouth?.AutomaticMouth != null);
        bool missingController = playback.Events.Any(item => item.Mouth?.AutomaticMouth == true)
            && !playback.Previews.Any(snapshot => snapshot.Preview.MouthCycleDefinition != null);
        return $"{playback.Actor.Root.Name}: {playback.Signals.Length} animated facial signals, {mouthCommands} mouth start/stop commands"
            + (missingController ? "; mouth controller binding unavailable" : "");
    });

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
        var result = facialPlaybacks.SelectMany(playback => playback.Previews)
            .ToDictionary(snapshot => snapshot.Target, _ => new List<FaceMorphKeyframe>());
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
            foreach (var playback in facialPlaybacks)
                foreach (var snapshot in playback.Previews)
                    result[snapshot.Target].Add(new(sampleTime, snapshot.Preview.CopyWeights()));
        }

        Seek(restoreTime);
        if (resumePlayback) Play();
        return result.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private void ResetFacialPlayback()
    {
        foreach (var playback in facialPlaybacks)
        {
            playback.NextEvent = 0;
            playback.LastTime = 0;
            playback.ActivePreview = null;
            foreach (var snapshot in playback.Previews)
            {
                snapshot.Transitions.Clear();
                Restore(snapshot);
                if (snapshot.Preview.MouthCycleDefinition is not { AutoStart: true } definition) continue;
                snapshot.Preview.StartMouthCycle(definition.DefaultFactor, definition.DefaultSpeed);
                playback.ActivePreview = snapshot.Preview;
            }
        }
    }

    private void AdvanceFacialPlayback(float targetTime)
    {
        foreach (var playback in facialPlaybacks)
        {
            if (playback.Previews.Length == 0) continue;
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
            if (playback.Events.Length > 0) playback.Actor.Evaluate(0);
        }
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
        foreach (int channel in snapshot.Transitions.Keys.ToArray())
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
    }

    private sealed record FacialEvent(float Time, uint TrackTarget, CinematicFacialEvent? Mouth,
        CinematicChannelFormEvent? ChannelForm, CinematicMorphFactorChange? MorphFactor);

    private sealed record CinematicMorphFactorChange(SceneObject Member, int Channel,
        TransformData Baseline, AnimationTrack[] Tracks, float Start, float End);

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
    }

    private const float FacialSampleRate = 60;
    private const int MaximumFacialSteps = 6000;
}
