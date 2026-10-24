namespace JadeView;

public sealed partial class CinematicPlayback
{
    private void EvaluateActors()
    {
        ClearScriptHierarchyWorldMatrices();
        EvaluateActorsBase();
        ApplyScriptHierarchyWorldMatrices();
    }

    private void EvaluateActorsBase()
    {
        localRotations.ResetTargets();
        gameScriptMoves.ResetTargets();
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

        eventTrackPreview.Evaluate(Time, world.Objects, sceneObjectTracksByTarget);
        orientTo.Evaluate(Time, sightSet);
        localRotations.Evaluate(Time);
        gameScriptMoves.Evaluate(Time);
        sightSet.Evaluate(Time, orientTo);
    }
}
