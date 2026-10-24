namespace JadeView;

public static class CinematicWorldAssembler
{
    public static WorldData Assemble(SceneDocument document, WorldData cinematicWorld)
    {
        const string cinematicSuffix = "_cine";
        bool hasCinematicSuffix = cinematicWorld.Name.EndsWith(cinematicSuffix, StringComparison.OrdinalIgnoreCase);
        string environmentName = hasCinematicSuffix
            ? cinematicWorld.Name[..^cinematicSuffix.Length] : cinematicWorld.Name;
        var sceneWorlds = new[] { cinematicWorld }.Concat(document.Worlds.Where(world => world != cinematicWorld
            && world.Name.Equals(environmentName, StringComparison.OrdinalIgnoreCase))).ToArray();
        var environmentVariantWorlds = document.Worlds.Where(world => hasCinematicSuffix
            && !sceneWorlds.Contains(world)
            && world.Name.StartsWith(environmentName + "_", StringComparison.OrdinalIgnoreCase)).ToArray();
        var environmentWorldKeys = sceneWorlds.Concat(environmentVariantWorlds)
            .Select(world => world.Key).ToHashSet();
        CinematicEnvironmentController[] environmentControllers = document.CinematicEnvironmentControllers.Values
            .Where(controller => environmentWorldKeys.Contains(controller.WorldKey)).ToArray();
        var clips = CinematicSceneClips.ForWorld(document, cinematicWorld);
        var targetKeys = CinematicSceneClips.References(clips);
        var nestedTimelines = clips.Select(clip =>
            CinematicNestedEventTransformTracks.ReadTimeline(clip, cinematicWorld, document)).ToArray();
        foreach (var timeline in nestedTimelines)
        {
            targetKeys.UnionWith(timeline.TransformTracks.Select(track => track.Target));
            targetKeys.UnionWith(timeline.Commands.Select(command => command.Call.Target));
        }
        IncludeScriptReferencedObjects(targetKeys, clips, cinematicWorld.Key, document);
        foreach (var controller in environmentControllers)
        {
            targetKeys.Add(controller.OwnerKey);
            targetKeys.UnionWith(controller.ObjectKeys);
        }
        CinematicSceneClips.IncludeGroupMembers(targetKeys, sceneWorlds.SelectMany(world => world.Objects)
            .Concat(document.Worlds.SelectMany(world => world.Objects)));
        var sceneActors = sceneWorlds.SelectMany(world => world.Actors).ToHashSet();
        foreach (var actor in document.Worlds.SelectMany(world => world.Actors))
            actor.IsCinematicWorldResident = sceneActors.Contains(actor);
        var actors = sceneWorlds.SelectMany(world => world.Actors)
            .Concat(document.Worlds.SelectMany(world => world.Actors)).Distinct()
            .Where(actor => sceneActors.Contains(actor)
                || actor.Members.Any(member => targetKeys.Contains(member.GaoKey)))
            .DistinctBy(actor => actor.Root.GaoKey).ToArray();
        var includedActors = actors.ToHashSet();
        var objects = sceneWorlds.SelectMany(world => world.Objects)
            .Concat(environmentVariantWorlds.SelectMany(world => world.Objects)
                .Where(IsVisibleStaticGeometry))
            .Concat(actors.SelectMany(actor => actor.Members))
            .Concat(document.Worlds.SelectMany(world => world.Objects)
                .Where(sceneObject => sceneObject.Actor == null && targetKeys.Contains(sceneObject.GaoKey)))
            .Where(sceneObject => sceneObject.Actor == null || includedActors.Contains(sceneObject.Actor))
            .Distinct();
        var assembled = new WorldData
        {
            Key = cinematicWorld.Key,
            Name = cinematicWorld.Name + " · assembled",
            CinematicEnvironmentControllers = environmentControllers,
            // Native WOL merge replaces ambient with each incoming WOW header.
            // A cinematic definition's header is not the final merged environment.
            Lighting = document.Worlds.Select(world => world.Lighting).LastOrDefault(lighting => lighting != null)
                ?? cinematicWorld.Lighting
        };
        assembled.Objects.AddRange(UniqueObjects(objects));
        assembled.Actors.AddRange(actors);
        foreach (var clip in clips) assembled.CinematicClips.Add(clip.Key, clip);
        return assembled;
    }

    private static void IncludeScriptReferencedObjects(HashSet<uint> targetKeys,
        IReadOnlyCollection<AnimationClip> clips, uint preferredWorldKey, SceneDocument document)
    {
        var owners = document.CinematicScriptOwners.Values.ToArray();
        var ownerIds = new HashSet<CinematicScriptOwnerId>();
        var pendingKeys = new Queue<(uint Key, uint WorldKey)>(targetKeys
            .Where(IsValidObjectKey).Select(key => (key, preferredWorldKey)));
        var pendingOwners = new Queue<CinematicScriptOwner>();
        var clipKeys = clips.Select(clip => clip.Key).ToHashSet();

        foreach (var clip in clips)
        {
            if (IsValidObjectKey(clip.CinematicOwnerKey))
                EnqueueOwnerKey(clip.CinematicOwnerKey, preferredWorldKey);
            foreach (uint ownerKey in clip.CinematicOwnerKeys)
                EnqueueOwnerKey(ownerKey, preferredWorldKey);
        }

        foreach (var owner in owners.Where(owner => owner.WorldKey == preferredWorldKey
            && (clipKeys.Contains(owner.EventsKey) || clipKeys.Contains(owner.SceneEventsKey))))
            EnqueueOwnerRecord(owner);

        while (pendingOwners.Count > 0 || pendingKeys.Count > 0)
        {
            while (pendingKeys.TryDequeue(out var pendingKey))
                EnqueueOwnerKey(pendingKey.Key, pendingKey.WorldKey);
            if (!pendingOwners.TryDequeue(out var owner)) continue;

            foreach (var reference in EffectiveObjectReferences(owner))
            {
                if (!IsValidObjectKey(reference.TargetKey)) continue;
                if (targetKeys.Add(reference.TargetKey))
                    pendingKeys.Enqueue((reference.TargetKey, owner.WorldKey));
            }
        }

        void EnqueueOwnerKey(uint ownerKey, uint worldKey)
        {
            var owner = CinematicScriptOwnerResolver.Resolve(ownerKey, worldKey, owners);
            if (owner != null) EnqueueOwnerRecord(owner);
        }

        void EnqueueOwnerRecord(CinematicScriptOwner owner)
        {
            if (ownerIds.Add(owner.Id)) pendingOwners.Enqueue(owner);
        }
    }

    private static IEnumerable<CinematicScriptObjectReference> EffectiveObjectReferences(
        CinematicScriptOwner owner) => owner.ObjectReferences
        .GroupBy(reference => reference.VariableName, StringComparer.OrdinalIgnoreCase)
        .SelectMany(references =>
        {
            var instanceReferences = references.Where(reference => reference.Source == "instance").ToArray();
            return instanceReferences.Length > 0
                ? instanceReferences
                : references.Where(reference => reference.Source == "model");
        });

    private static bool IsValidObjectKey(uint key) => key is > 2 and < uint.MaxValue;

    private static bool IsVisibleStaticGeometry(SceneObject sceneObject) =>
        sceneObject.Actor == null
        && sceneObject.GeometryKey is not (0 or uint.MaxValue)
        && (sceneObject.DrawFlags & 0x8) != 0;

    private static IEnumerable<SceneObject> UniqueObjects(IEnumerable<SceneObject> objects)
    {
        // Actor components can share template keys across different instances.
        var seen = new HashSet<(SceneActor? Actor, uint Key)>();
        foreach (var sceneObject in objects)
            if (sceneObject.GaoKey is 0 or uint.MaxValue || seen.Add((sceneObject.Actor, sceneObject.GaoKey)))
                yield return sceneObject;
    }
}
