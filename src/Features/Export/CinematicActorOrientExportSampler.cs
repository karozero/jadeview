using System.Numerics;

namespace JadeView;

internal sealed class CinematicActorOrientExportSampler : IDisposable
{
    private readonly SceneActor primaryActor;
    private readonly WorldData world;
    private readonly Dictionary<uint, AnimationTrack[]> sceneObjectTracks;
    private readonly CinematicOrientToTimeline orientTo;
    private readonly CinematicSightSetTimeline sightSet;
    private readonly uint cinematicClipKey;
    private readonly ActorExportSnapshot[] actorSnapshots;
    private readonly (SceneObject Object, Matrix4x4? Preview)[] objectSnapshots;

    internal CinematicActorOrientExportSampler(SceneActor primaryActor, WorldData world,
        AnimationTrack[] tracks, CinematicOrientToTimeline orientTo,
        CinematicSightSetTimeline sightSet, uint cinematicClipKey)
    {
        this.primaryActor = primaryActor;
        this.world = world;
        sceneObjectTracks = tracks.Where(track => track.Bone < 0)
            .GroupBy(track => track.Target)
            .ToDictionary(group => group.Key, group => group.ToArray());
        this.orientTo = orientTo;
        this.sightSet = sightSet;
        this.cinematicClipKey = cinematicClipKey;
        actorSnapshots = world.Actors.Where(actor => actor != primaryActor)
            .Select(actor => new ActorExportSnapshot(actor)).ToArray();
        objectSnapshots = world.Objects.Where(sceneObject => sceneObject.Actor == null)
            .Select(sceneObject => (sceneObject, sceneObject.PreviewMatrix)).ToArray();
    }

    internal void Evaluate(float time)
    {
        EvaluateSceneObjectTracks(time);
        EvaluateReferenceActors(time);
        orientTo.Evaluate(time, sightSet);
        sightSet.Evaluate(time, orientTo);
    }

    private void EvaluateSceneObjectTracks(float time)
    {
        foreach (SceneObject sceneObject in world.Objects.Where(sceneObject => sceneObject.Actor == null))
        {
            sceneObject.PreviewMatrix = AnimationMath.FromRender(
                CinematicHierarchyTransformSampler.EvaluateWorldRender(sceneObject,
                    sceneObjectTracks, time, world.Objects));
        }
    }

    private void EvaluateReferenceActors(float time)
    {
        foreach (SceneActor actor in world.Actors)
        {
            if (actor == primaryActor || actor.Clip is not { Cinematic: true } clip
                || clip.Key != cinematicClipKey) continue;
            actor.Playing = false;
            actor.Loop = false;
            actor.Time = Math.Min(time, clip.Duration);
            actor.Evaluate(0);
        }
    }

    public void Dispose()
    {
        foreach (ActorExportSnapshot snapshot in actorSnapshots) snapshot.Dispose();
        foreach (var snapshot in objectSnapshots) snapshot.Object.PreviewMatrix = snapshot.Preview;
    }
}
