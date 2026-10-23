namespace JadeView;

public sealed partial class CinematicPlayback
{
    private void EvaluateActors()
    {
        localRotations.ResetTargets();
        int actorIndex = 0;
        foreach (var (actor, clip) in actorClips)
        {
            actor.Clip = clip;
            actor.Playing = false;
            actor.Time = Math.Min(Time, clip.Duration);
            long allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
            actor.Evaluate(0);
            if (CaptureAllocationDiagnostics)
                allocatedByActor[actorIndex] += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            actorIndex++;
        }

        foreach (var track in objectTracks)
            EvaluateObject(track.Object, track.Tracks, Time);

        eventTrackPreview.Evaluate(Time);
        orientTo.Evaluate(Time);
        localRotations.Evaluate(Time);
    }
}
