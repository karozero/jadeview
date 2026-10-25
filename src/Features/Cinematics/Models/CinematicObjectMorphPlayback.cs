using System.Numerics;

namespace JadeView;

internal sealed class CinematicObjectMorphPlayback
{
    private sealed record Snapshot(SceneObject Target, FaceMorphPreview Preview,
        float[] Weights, FaceMorphChannelState[]? Channels);

    private sealed record Signal(SceneObject Target, FaceMorphPreview Preview, int Channel,
        TransformData CarrierBaseline, AnimationTrack[] CarrierTracks, float Start, float End);

    private readonly Snapshot[] snapshots;
    private readonly Signal[] signals;
    private readonly SceneObject[] targets;
    private float lastTime;

    private CinematicObjectMorphPlayback(Snapshot[] snapshots, Signal[] signals)
    {
        this.snapshots = snapshots;
        this.signals = signals;
        targets = snapshots.Select(snapshot => snapshot.Target).ToArray();
    }

    internal IReadOnlyCollection<SceneObject> Targets => targets;
    internal IEnumerable<string> Diagnostics => targets.Select(target =>
        $"{target.Name}: {signals.Count(signal => signal.Target == target)} EVE morph-channel signal(s); AI initialization and callback visibility gates are not simulated");

    internal static CinematicObjectMorphPlayback? Create(AnimationClip clip, WorldData world,
        ResourceResolver resolver, IReadOnlyList<(float Time, CinematicLiteralCall Call)> nestedCommands,
        IReadOnlyList<AnimationTrack> sourceTracks)
    {
        var sceneObjects = world.Objects
            .Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct()
            .ToArray();
        var calls = CinematicLiteralCall.ReadTimelineWithDuration(clip)
            .Concat(nestedCommands.Select(item => (item.Time, Duration: 0f, item.Call)))
            .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector)
                == "OBJ_AnimMorphFactorNoKey")
            .OrderBy(item => item.Time)
            .ThenBy(item => item.Call.Target)
            .ToArray();

        var signals = new List<Signal>();
        foreach (var item in calls)
        {
            if (!TryResolveSignal(item.Call, item.Time, item.Duration, sceneObjects,
                    sourceTracks, resolver, out Signal? signal) || signal == null) continue;
            signals.Add(signal);
        }

        if (signals.Count == 0) return null;
        Signal[] orderedSignals = signals
            .OrderBy(signal => signal.Start)
            .ThenBy(signal => signal.Target.GaoKey)
            .ThenBy(signal => signal.Channel)
            .ToArray();
        Snapshot[] snapshots = orderedSignals.Select(signal => signal.Target)
            .Distinct()
            .Select(target =>
            {
                var preview = target.MorphPreview!;
                return new Snapshot(target, preview, preview.CopyWeights(), preview.CopyChannelState());
            })
            .ToArray();
        return new(snapshots, orderedSignals);
    }

    internal void Reset()
    {
        lastTime = 0;
        foreach (Snapshot snapshot in snapshots) Restore(snapshot);
    }

    internal void Advance(float time)
    {
        if (!float.IsFinite(time)) return;
        if (time < lastTime) Reset();

        foreach (Signal signal in signals)
        {
            if (time < signal.Start || (uint)signal.Channel >= (uint)signal.Preview.ChannelCount) continue;
            float sampleTime = signal.End > signal.Start && time >= signal.End
                ? MathF.BitDecrement(signal.End)
                : Math.Clamp(time, signal.Start, Math.Max(signal.Start, signal.End));
            Matrix4x4 carrierPose = CinematicTransformSampler.Evaluate(
                signal.CarrierBaseline, signal.CarrierTracks, sampleTime);
            float weight = carrierPose.M41;
            float progression = carrierPose.M42 + 1;
            if (!float.IsFinite(weight) || !float.IsFinite(progression)
                || progression < 0 || progression >= 2147483648f) continue;
            signal.Preview.ApplyCinematicChannelState(signal.Channel, weight, progression);
        }

        lastTime = time;
    }

    internal FaceMorphChannelState? SampleChannelState(SceneObject target, int channel, float time)
    {
        FaceMorphChannelState? sampledState = null;
        foreach (Signal signal in signals.Where(signal => signal.Target == target
                     && signal.Channel == channel && time >= signal.Start))
        {
            float sampleTime = signal.End > signal.Start && time >= signal.End
                ? MathF.BitDecrement(signal.End)
                : Math.Clamp(time, signal.Start, Math.Max(signal.Start, signal.End));
            Matrix4x4 carrierPose = CinematicTransformSampler.Evaluate(
                signal.CarrierBaseline, signal.CarrierTracks, sampleTime);
            float weight = carrierPose.M41;
            float progression = carrierPose.M42 + 1;
            if (float.IsFinite(weight) && float.IsFinite(progression)
                && progression >= 0 && progression < 2147483648f)
                sampledState = new(weight, progression);
        }
        return sampledState;
    }

    internal void RestoreOriginalState()
    {
        Reset();
    }

    private static bool TryResolveSignal(CinematicLiteralCall call, float time, float duration,
        IReadOnlyCollection<SceneObject> sceneObjects, IReadOnlyList<AnimationTrack> sourceTracks,
        ResourceResolver resolver, out Signal? signal)
    {
        signal = null;
        if (call.Arguments.Length != 3) return false;

        uint targetKey = call.Arguments[0];
        int moduleSelector = unchecked((int)call.Arguments[1]);
        uint channelValue = call.Arguments[2];
        if (moduleSelector != -1 || channelValue > int.MaxValue) return false;

        SceneObject[] targetMatches = sceneObjects
            .Where(candidate => candidate.GaoKey == targetKey && candidate.Actor == null)
            .ToArray();
        SceneObject[] carrierMatches = sceneObjects
            .Where(candidate => candidate.GaoKey == call.Target)
            .ToArray();
        if (targetMatches.Length != 1 || carrierMatches.Length != 1) return false;

        SceneObject target = targetMatches[0];
        SceneObject carrier = carrierMatches[0];
        if (!resolver.Geometries.TryGetValue(target.RenderGeometryKey, out Geometry? geometry)) return false;
        FaceMorphPreview? preview = FaceMorphPreview.Get(target, geometry);
        if (preview is not { ChannelCount: > 0 } || channelValue >= preview.ChannelCount) return false;

        AnimationTrack[] carrierTracks = sourceTracks
            .Where(track => track.Target == carrier.GaoKey && track.Bone < 0)
            .ToArray();
        if (carrierTracks.Length == 0) return false;

        signal = new(target, preview, (int)channelValue, carrier.Transform.Clone(), carrierTracks,
            time, time + Math.Max(duration, 0));
        return true;
    }

    private static void Restore(Snapshot snapshot)
    {
        snapshot.Preview.Reset();
        if (snapshot.Channels != null)
        {
            snapshot.Preview.RestoreChannelState(snapshot.Channels);
            return;
        }
        for (int group = 0; group < snapshot.Weights.Length; group++)
            if (snapshot.Weights[group] != 0)
                snapshot.Preview.SetWeight(group, snapshot.Weights[group]);
    }
}
