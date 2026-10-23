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

        for (int sampleIndex = 0; sampleIndex < 32; sampleIndex++)
            _ = timeline.Evaluate(sampleIndex / 60f);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sampleIndex = 0; sampleIndex < 1024; sampleIndex++)
            _ = timeline.Evaluate(sampleIndex / 60f);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocatedBytes == 0,
            $"Evaluating a video trigger track must not allocate per frame; got {allocatedBytes} bytes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
