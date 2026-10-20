namespace JadeView;

public sealed partial class CinematicPlayback
{
    private void EvaluateActors()
    {
        foreach (var (actor, clip) in actorClips)
        {
            actor.Clip = clip;
            actor.Playing = false;
            actor.Time = Math.Min(Time, clip.Duration);
            actor.Evaluate(0);
        }

        foreach (var track in objectTracks)
            EvaluateObject(track.Object, track.Tracks, Time);
    }
}
