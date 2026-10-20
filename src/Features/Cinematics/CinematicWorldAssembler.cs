namespace JadeView;

public static class CinematicWorldAssembler
{
    public static WorldData Assemble(SceneDocument document, WorldData cinematicWorld)
    {
        const string cinematicSuffix = "_cine";
        string environmentName = cinematicWorld.Name.EndsWith(cinematicSuffix, StringComparison.OrdinalIgnoreCase)
            ? cinematicWorld.Name[..^cinematicSuffix.Length] : cinematicWorld.Name;
        var sceneWorlds = new[] { cinematicWorld }.Concat(document.Worlds.Where(world => world != cinematicWorld
            && world.Name.Equals(environmentName, StringComparison.OrdinalIgnoreCase))).ToArray();
        var clips = CinematicSceneClips.ForWorld(document, cinematicWorld);
        var targetKeys = CinematicSceneClips.References(clips);
        var activeControllers = document.Worlds.SelectMany(world => world.Objects)
            .Where(item => (item.ControlFlags & CinematicObjectState.Inactive) == 0)
            .Select(item => item.GaoKey).ToHashSet();
        foreach (var controller in document.CinematicEnvironmentControllers.Values
            .Where(controller => activeControllers.Contains(controller.OwnerKey)))
            targetKeys.UnionWith(controller.ObjectKeys);
        CinematicSceneClips.IncludeGroupMembers(targetKeys, sceneWorlds.SelectMany(world => world.Objects)
            .Concat(document.Worlds.SelectMany(world => world.Objects)));
        var sceneActors = sceneWorlds.SelectMany(world => world.Actors).ToHashSet();
        var actors = sceneWorlds.SelectMany(world => world.Actors)
            .Concat(document.Worlds.SelectMany(world => world.Actors)).Distinct()
            .Where(actor => sceneActors.Contains(actor)
                || actor.Members.Any(member => targetKeys.Contains(member.GaoKey)))
            .DistinctBy(actor => actor.Root.GaoKey).ToArray();
        var includedActors = actors.ToHashSet();
        var objects = sceneWorlds.SelectMany(world => world.Objects)
            .Concat(actors.SelectMany(actor => actor.Members))
            .Concat(document.Worlds.SelectMany(world => world.Objects)
                .Where(sceneObject => sceneObject.Actor == null && targetKeys.Contains(sceneObject.GaoKey)))
            .Where(sceneObject => sceneObject.Actor == null || includedActors.Contains(sceneObject.Actor))
            .Distinct();
        var assembled = new WorldData { Key = cinematicWorld.Key, Name = cinematicWorld.Name + " · assembled" };
        assembled.Objects.AddRange(UniqueObjects(objects));
        assembled.Actors.AddRange(actors);
        foreach (var clip in clips) assembled.CinematicClips.Add(clip.Key, clip);
        return assembled;
    }

    private static IEnumerable<SceneObject> UniqueObjects(IEnumerable<SceneObject> objects)
    {
        // Actor components can share template keys across different instances.
        var seen = new HashSet<(SceneActor? Actor, uint Key)>();
        foreach (var sceneObject in objects)
            if (sceneObject.GaoKey is 0 or uint.MaxValue || seen.Add((sceneObject.Actor, sceneObject.GaoKey)))
                yield return sceneObject;
    }
}
