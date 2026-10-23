namespace JadeView;

internal static class CinematicScriptNamedReferenceCatalog
{
    internal static CinematicScriptNamedReference[] ForDocument(SceneDocument document)
    {
        CinematicScriptNamedReference[] objects = document.Worlds
            .SelectMany(world => world.Objects.Concat(world.Actors.SelectMany(actor =>
                actor.Root == null ? actor.Members : actor.Members.Prepend(actor.Root)))
                .Select(sceneObject => new CinematicScriptNamedReference(world.Key,
                    sceneObject.GaoKey, sceneObject.Name, IsObject: true)))
            .ToArray();
        CinematicScriptNamedReference[] owners = document.CinematicScriptOwners.Values
            .Select(owner => new CinematicScriptNamedReference(owner.WorldKey, owner.OwnerKey,
                owner.Name, IsObject: false))
            .ToArray();

        return objects.Concat(owners)
            .DistinctBy(reference => (reference.WorldKey, reference.Key, reference.IsObject))
            .ToArray();
    }
}
