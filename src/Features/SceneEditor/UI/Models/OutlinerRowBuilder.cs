namespace JadeView;

internal enum OutlinerRowKind
{
    Actor,
    ActorComponent,
    SceneObject
}

internal enum OutlinerContentKind
{
    All,
    Actors,
    StandaloneObjects
}

internal readonly record struct OutlinerRow(OutlinerRowKind Kind, SceneObject Object,
    SceneActor? Actor = null);

internal static class OutlinerRowBuilder
{
    internal static void Build(WorldData world, Func<SceneObject, bool> matchesFilter,
        HashSet<SceneObject> expandedActors, SceneObject? revealObject,
        List<OutlinerRow> rows, HashSet<SceneObject> matchingObjects,
        OutlinerContentKind contentKind = OutlinerContentKind.All)
    {
        rows.Clear();
        matchingObjects.Clear();
        foreach (SceneObject sceneObject in world.Objects)
            if (matchesFilter(sceneObject))
                matchingObjects.Add(sceneObject);

        if (revealObject?.Actor is { } revealedActor && revealObject != revealedActor.Root)
            expandedActors.Add(revealedActor.Root);

        if (contentKind != OutlinerContentKind.StandaloneObjects)
        {
            foreach (SceneActor actor in world.Actors)
            {
                bool hasMatchingMember = actor.Members.Any(matchingObjects.Contains);
                if (!hasMatchingMember) continue;

                rows.Add(new(OutlinerRowKind.Actor, actor.Root, actor));
                if (!expandedActors.Contains(actor.Root)) continue;
                foreach (SceneObject member in actor.Members)
                    if (member != actor.Root && matchingObjects.Contains(member))
                        rows.Add(new(OutlinerRowKind.ActorComponent, member));
            }
        }

        if (contentKind != OutlinerContentKind.Actors)
        {
            foreach (SceneObject sceneObject in world.Objects)
                if (sceneObject.Actor == null && matchingObjects.Contains(sceneObject))
                    rows.Add(new(OutlinerRowKind.SceneObject, sceneObject));
        }
    }
}
