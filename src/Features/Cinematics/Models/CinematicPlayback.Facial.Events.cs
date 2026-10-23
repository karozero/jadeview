using System.Numerics;

namespace JadeView;

public sealed partial class CinematicPlayback
{
    private const int SetMorphChannelFactorSelector = 8253;
    private const int SetMorphChannelProgressionSelector = 8255;

    private static FacialPlayback? CreateFacialPlayback(SceneActor actor, AnimationClip clip,
        IReadOnlyCollection<SceneObject> sceneObjects)
    {
        var events = clip.Tracks
            .SelectMany(track => track.Keys.SelectMany(key =>
                CinematicOwnerContext.ForTrack(clip, track)
                    .SelectMany(context => DecodeFacialEvents(actor, track, key, sceneObjects, clip, context))))
            .Where(item => item.ChannelForm != null
                ? item.TrackTarget == actor.Root.GaoKey
                : item.Mouth != null || item.MorphFactor != null || item.ChannelState != null)
            .OrderBy(item => item.Time)
            .ThenBy(GetFacialEventPriority)
            .ToArray();
        return events.Length > 0 ? new(actor, events) : null;
    }

    private static IEnumerable<FacialEvent> DecodeFacialEvents(SceneActor actor,
        AnimationTrack track, AnimationKey key, IReadOnlyCollection<SceneObject> sceneObjects,
        AnimationClip clip, uint trackTarget)
    {
        foreach (var payload in key.OpaquePayloads)
        {
            var call = CinematicLiteralCall.Decode(payload, track.Target, trackTarget);
            var channelChange = DecodeChannelStateChange(actor, call);
            if (channelChange != null)
                yield return new(key.Time, channelChange.Member.GaoKey, null, null, null, channelChange);
        }

        var mouthEvent = CinematicFacialEvent.Decode(key);
        if (mouthEvent != null && TargetsFacialActor(trackTarget, mouthEvent.ActorTargetKey, actor))
            yield return new(key.Time, trackTarget, mouthEvent, null, null);

        var formEvent = CinematicChannelFormEvent.Decode(key);
        uint formTarget = formEvent?.TargetKey is > 0 and < uint.MaxValue ? formEvent.TargetKey : trackTarget;
        if (formEvent != null && formTarget == actor.Root.GaoKey)
            yield return new(key.Time, formTarget, null, formEvent, null);

        var factorCall = CinematicMorphFactorCall.Decode(key);
        if (factorCall == null || !TargetsActor(actor, factorCall.TargetKey)) yield break;
        var member = factorCall.ModuleSelector == -1
            ? actor.Root
            : actor.FindModule((byte)factorCall.ModuleSelector);
        var helper = sceneObjects.FirstOrDefault(sceneObject => sceneObject.GaoKey == track.Target);
        if (member == null || helper == null) yield break;

        var helperTracks = clip.Tracks.Where(candidate => candidate.Target == helper.GaoKey && candidate.Bone < 0).ToArray();
        var morphFactor = new CinematicMorphFactorChange(member, factorCall.Channel,
            helper.Transform.Clone(), helperTracks, key.Time, key.Time + key.Duration);
        yield return new(key.Time, track.Target, null, null, morphFactor);
        if (key.Duration > 0)
            yield return new(key.Time + key.Duration, track.Target, null, null, morphFactor);
    }

    private static bool TargetsActor(SceneActor actor, uint targetKey) =>
        targetKey == actor.Root.GaoKey || actor.Members.Any(member => member.GaoKey == targetKey);

    private static CinematicChannelStateChange? DecodeChannelStateChange(SceneActor actor,
        CinematicLiteralCall? call)
    {
        if (call is not { Arguments.Length: 2 }
            || call.Selector is not (SetMorphChannelFactorSelector or SetMorphChannelProgressionSelector)
            || call.Arguments[0] > int.MaxValue) return null;

        var member = actor.Members.FirstOrDefault(candidate => candidate.GaoKey == call.Target);
        float value = BitConverter.UInt32BitsToSingle(call.Arguments[1]);
        if (member == null || !float.IsFinite(value)) return null;
        if (call.Selector == SetMorphChannelProgressionSelector
            && (value < 0 || value >= 2147483648f)) return null;

        return new(member, (int)call.Arguments[0],
            call.Selector == SetMorphChannelFactorSelector, value);
    }

    private static bool TargetsFacialActor(uint trackTarget, uint messageTarget, SceneActor actor)
    {
        if (messageTarget is 0 or uint.MaxValue) return trackTarget == actor.Root.GaoKey;
        return TargetsActor(actor, messageTarget);
    }

    private static int GetFacialEventPriority(FacialEvent facialEvent) =>
        facialEvent.ChannelForm != null ? 0 : facialEvent.MorphFactor != null ? 1 : 2;

    private static void ApplyFacialEvent(FacialPlayback playback, FacialEvent facialEvent)
    {
        if (facialEvent.ChannelForm is { } formEvent)
            ApplyChannelFormEvent(playback, formEvent);

        if (facialEvent.MorphFactor is { } morphFactor)
            ApplyMorphFactor(playback, morphFactor, facialEvent.Time);

        if (facialEvent.ChannelState is { } channelState)
            ApplyChannelStateChange(playback, channelState);

        if (facialEvent.Mouth is { } mouthEvent)
        {
            ApplyMouthEvent(playback, mouthEvent);
            ApplyChannelCommands(playback, mouthEvent);
        }
    }

    private static void ApplyChannelStateChange(FacialPlayback playback,
        CinematicChannelStateChange change)
    {
        var snapshot = playback.Previews.FirstOrDefault(item => item.Target == change.Member);
        if (snapshot == null || (uint)change.Channel >= (uint)snapshot.Preview.ChannelCount) return;

        var current = snapshot.Preview.GetChannel(change.Channel);
        float weight = change.IsWeight ? change.Value : current.Weight;
        float progression = change.IsWeight ? current.Progression : change.Value;
        snapshot.Preview.ApplyCinematicChannelState(change.Channel, weight, progression);
    }

    private static void ApplyChannelFormEvent(FacialPlayback playback, CinematicChannelFormEvent formEvent)
    {
        var target = formEvent.ModuleSelector == -1
            ? playback.Actor.Root
            : playback.Actor.FindModule((byte)formEvent.ModuleSelector);
        var snapshot = playback.Previews.FirstOrDefault(item => item.Target == target);
        snapshot?.Preview.SetCinematicChannelGroupReference(
            formEvent.Channel, formEvent.Slot, formEvent.Group);
    }

    private static void ApplyMorphFactor(FacialPlayback playback, CinematicMorphFactorChange change, float time)
    {
        var snapshot = playback.Previews.FirstOrDefault(item => item.Target == change.Member);
        if (snapshot == null || change.Channel >= snapshot.Preview.ChannelCount) return;
        float sampleTime = time == change.End && change.End > change.Start ? MathF.BitDecrement(time) : time;
        Matrix4x4 helperPose = CinematicTransformSampler.Evaluate(change.Baseline, change.Tracks, sampleTime);
        float factor = helperPose.M41;
        float progression = helperPose.M42 + 1;
        if (!float.IsFinite(factor) || !float.IsFinite(progression) || progression < 0
            || progression >= 2147483648f) return;
        snapshot.Preview.ApplyCinematicChannelState(change.Channel, factor, progression);
    }

    private static void ApplyMouthEvent(FacialPlayback playback, CinematicFacialEvent facialEvent)
    {
        foreach (var snapshot in playback.Previews)
        {
            if (snapshot.Preview.MouthCycleDefinition == null) continue;
            if (facialEvent.AutomaticMouth == true)
            {
                snapshot.Preview.StartMouthCycle(facialEvent.MouthFactor, facialEvent.MouthSpeed);
                playback.ActivePreview = snapshot.Preview;
            }
            else if (facialEvent.AutomaticMouth == false)
            {
                snapshot.Preview.StopMouthCycle();
                if (ReferenceEquals(playback.ActivePreview, snapshot.Preview)) playback.ActivePreview = null;
            }
        }
    }

    private static void ApplyChannelCommands(FacialPlayback playback, CinematicFacialEvent facialEvent)
    {
        foreach (var command in facialEvent.Commands)
        {
            if (command == null) continue;
            foreach (var snapshot in playback.Previews)
            {
                var transition = snapshot.Preview.BeginChannelTransition(
                    command.Channel, command.Group, command.Factor, command.Speed);
                if (transition != null) snapshot.Transitions[command.Channel] = transition;
            }
        }
    }
}
