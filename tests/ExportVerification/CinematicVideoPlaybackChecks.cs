using System.Globalization;
using JadeView;

internal static class CinematicVideoPlaybackChecks
{
    internal static void Verify()
    {
        int callingThread = Environment.CurrentManagedThreadId;
        int decoderThread = callingThread;
        CinematicVideoFrame decodedFrame = CinematicVideoFrameLoader.DecodeAsync(12, _ =>
        {
            decoderThread = Environment.CurrentManagedThreadId;
            return [1, 2, 3, 4];
        }).GetAwaiter().GetResult();
        Require(decodedFrame.Index == 12 && decodedFrame.Rgba.Length == 4
            && decodedFrame.Rgba[0] == 1 && decodedFrame.Rgba[3] == 4
            && decoderThread != callingThread,
            "Bink frame decoding must run off the render caller thread and retain the requested frame index.");

        var timeline = new CinematicVideoPlaybackTrack(false,
        [
            new(2, CinematicVideoControl.Toggle),
            new(4, CinematicVideoControl.Stop),
            new(5, CinematicVideoControl.Start)
        ]);

        CinematicVideoPlaybackSample beforeStart = timeline.Evaluate(1.5f);
        Require(!beforeStart.HasStarted && !beforeStart.IsPlaying,
            "A script-controlled video must remain stopped before the source trigger.");

        CinematicVideoPlaybackSample started = timeline.Evaluate(3.25f);
        Require(started.HasStarted && started.IsPlaying && Math.Abs(started.VideoTime - 1.25f) < .001f,
            "A toggle-on event must start video from media time zero and advance with the cinematic playhead.");

        CinematicVideoPlaybackSample stopped = timeline.Evaluate(4.5f);
        Require(stopped.HasStarted && !stopped.IsPlaying && Math.Abs(stopped.VideoTime - 2f) < .001f,
            "A stop event must retain the frame reached at the stop timestamp.");

        CinematicVideoPlaybackSample restarted = timeline.Evaluate(5.5f);
        Require(restarted.IsPlaying && Math.Abs(restarted.VideoTime - .5f) < .001f,
            "A later start event must restart media time from zero.");

        CinematicVideoPlaybackSample seekBack = timeline.Evaluate(2.5f);
        Require(seekBack.IsPlaying && Math.Abs(seekBack.VideoTime - .5f) < .001f,
            "Seeking backwards must recompute video state from the source event timeline.");

        const float clipEnd = 44.783333f;
        var endTriggeredVideo = new CinematicVideoPlaybackTrack(false,
            [new(clipEnd, CinematicVideoControl.Toggle)], stopAtEnd: true);
        CinematicVideoPlaybackSample postRollSample = endTriggeredVideo.Evaluate(clipEnd + .5f);
        Require(postRollSample.IsPlaying && Math.Abs(postRollSample.VideoTime - .5f) < .001f
            && CinematicVideoPostRoll.ShouldContinue(postRollSample, true,
                mediaLoadFailed: false, mediaMetadataAvailable: false, mediaReachedEnd: false)
            && CinematicVideoPostRoll.ShouldContinue(postRollSample, true,
                mediaLoadFailed: false, mediaMetadataAvailable: true, mediaReachedEnd: false)
            && !CinematicVideoPostRoll.ShouldContinue(postRollSample, true,
                mediaLoadFailed: false, mediaMetadataAvailable: true, mediaReachedEnd: true)
            && !CinematicVideoPostRoll.ShouldContinue(postRollSample, true,
                mediaLoadFailed: true, mediaMetadataAvailable: false, mediaReachedEnd: false),
            "A video started at clip end must retain post-roll while media loads, then finish at decoded media end.");
        Require(CinematicVideoPostRoll.ShouldContinue(postRollSample, stopAtMediaEnd: false,
                mediaLoadFailed: false, mediaMetadataAvailable: true, mediaReachedEnd: true)
            && !CinematicVideoPostRoll.ShouldContinue(timeline.Evaluate(4.5f), true,
                mediaLoadFailed: false, mediaMetadataAvailable: false, mediaReachedEnd: false),
            "Post-roll must respect continuous-video playback and stop events independently of decoder state.");

        VerifyMpegPlayerInitializationSourceGate();
        VerifyMpegOwnerVisibilityFollowsPlayback();

        for (int sampleIndex = 0; sampleIndex < 32; sampleIndex++)
            _ = timeline.Evaluate(sampleIndex / 60f);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sampleIndex = 0; sampleIndex < 1024; sampleIndex++)
            _ = timeline.Evaluate(sampleIndex / 60f);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocatedBytes == 0,
            $"Evaluating a video trigger track must not allocate per frame; got {allocatedBytes} bytes.");
    }

    private static void VerifyMpegPlayerInitializationSourceGate()
    {
        const uint ownerKey = 0x3500A101;
        const uint switchKey = 0x3500A102;
        var switchObject = new SceneObject { GaoKey = switchKey, Name = "video switch", InitialCapabilities = 0 };
        var owner = CreateOwner(ownerKey, switchKey, serializedPlaying: 1);
        CinematicVideoInitialPlaybackState offState = CinematicVideoPlaybackTimeline
            .ResolveInitialPlaybackState(owner, switchObject);
        Require(offState.IsResolved && !offState.InitiallyPlaying
            && offState.Evidence.Contains("i_Play is overwritten", StringComparison.Ordinal),
            "The normal Iris init path must derive initial playback from switch capability, not stale serialized i_Play.");

        switchObject.InitialCapabilities = 0x100;
        CinematicVideoInitialPlaybackState onState = CinematicVideoPlaybackTimeline
            .ResolveInitialPlaybackState(CreateOwner(ownerKey, switchKey, serializedPlaying: 0), switchObject);
        Require(onState.IsResolved && onState.InitiallyPlaying,
            "The normal Iris init path must start when the resolved switch has capability 0x100, even if i_Play is zero.");

        CinematicVideoInitialPlaybackState savedFlagState = CinematicVideoPlaybackTimeline
            .ResolveInitialPlaybackState(CreateOwner(ownerKey, switchKey, serializedPlaying: 0,
                savedFlagSwitch: 42), switchObject);
        Require(!savedFlagState.IsResolved && savedFlagState.InitiallyPlaying
            && savedFlagState.Evidence.Contains("saved flag 42", StringComparison.Ordinal),
            "A save-flag controlled start must stay estimated while preserving the legacy preview fallback.");

        CinematicVideoInitialPlaybackState soundGateState = CinematicVideoPlaybackTimeline
            .ResolveInitialPlaybackState(CreateOwner(ownerKey, switchKey, serializedPlaying: 0,
                soundModifier: 1), switchObject);
        Require(!soundGateState.IsResolved && soundGateState.InitiallyPlaying
            && soundGateState.Evidence.Contains("sound modifier 1", StringComparison.Ordinal),
            "A nonzero MPEG sound-status gate must be labeled estimated while retaining the switch-based visual fallback.");

        static CinematicScriptOwner CreateOwner(uint scriptOwnerKey, uint targetSwitchKey,
            int serializedPlaying, int savedFlagSwitch = 0, int soundModifier = 0) => new(scriptOwnerKey, 0, 0, 0, 0, 0,
            "mpeg player", ["mpegplayer_init"],
            [new("des_o_Interrupteur", targetSwitchKey, "instance", "video switch")],
            [new("i_Play", 0, "instance", serializedPlaying.ToString(CultureInfo.InvariantCulture)),
             new("des_i_sf_modifier_vrai", 0, "instance", "0"),
             new("des_i_sf_jesuisla", 0, "instance", "0"),
             new("des_i_sf_on_off", 0, "instance", savedFlagSwitch.ToString(CultureInfo.InvariantCulture)),
             new("des_i_SND_modifier", 0, "instance", soundModifier.ToString(CultureInfo.InvariantCulture))]);
    }

    private static void VerifyMpegOwnerVisibilityFollowsPlayback()
    {
        const uint videoOwnerKey = 0x3500A201;
        var owner = new SceneObject { GaoKey = videoOwnerKey, Name = "screen" };
        var world = new WorldData();
        world.Objects.Add(owner);
        var track = new CinematicVideoPlaybackTrack(videoOwnerKey,
            new CinematicVideoInitialPlaybackState(true, true, "normal init path"),
            [new(2, CinematicVideoControl.Toggle)]);
        var videoPlayback = new CinematicVideoPlaybackTimeline([track]);
        var state = new CinematicObjectState(new AnimationClip(), world, [],
            videoPlayback: videoPlayback);

        state.Evaluate(0);
        Require(!owner.IsControlHidden,
            "An MPEG player that starts in the normal init path must reveal its owner object.");
        state.Evaluate(2);
        Require(owner.IsControlHidden,
            "An MPEG-player toggle-off must hide its owner object, matching Iris mpegplayer_loop.");
        state.Evaluate(1);
        Require(!owner.IsControlHidden,
            "Seeking backward across an MPEG-player event must restore its initial owner visibility.");
        Require(state.VisibilityTargets.Contains(owner)
            && state.VisibilityChangeTimes.Contains(2),
            "MPEG-player visibility must be represented as a timeline target for scene export.");
        state.Clear();

        var estimatedOwner = new SceneObject { GaoKey = videoOwnerKey + 1, Name = "estimated screen" };
        var estimatedWorld = new WorldData();
        estimatedWorld.Objects.Add(estimatedOwner);
        var estimatedTrack = new CinematicVideoPlaybackTrack(videoOwnerKey + 1,
            new CinematicVideoInitialPlaybackState(false, false, "sound modifier gate unresolved"),
            [new(2, CinematicVideoControl.Toggle)]);
        var estimatedState = new CinematicObjectState(new AnimationClip(), estimatedWorld, [],
            videoPlayback: new CinematicVideoPlaybackTimeline([estimatedTrack]));
        estimatedState.Evaluate(0);
        Require(estimatedOwner.IsControlHidden,
            "An unresolved initial state must still use the same disclosed fallback as the procedural video preview.");
        estimatedState.Evaluate(2);
        Require(!estimatedOwner.IsControlHidden,
            "Serialized video triggers must update owner visibility consistently even when the initial gate is estimated.");
        estimatedState.Clear();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
