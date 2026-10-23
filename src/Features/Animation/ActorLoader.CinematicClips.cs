namespace JadeView;

internal static partial class ActorLoader
{
    private static void AddCinematicClips(SceneActor actor, SceneObject root,
        PcSceneLoader.Result replay, Dictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> cinematicWorlds,
        Dictionary<PcSceneLoader.Record, AnimationClip?> clipCache)
    {
        var memberKeys = actor.Members.Select(member => member.GaoKey).ToHashSet();

        var eventRecords = CinematicActorClipDiscovery.FindActorClips(replay.Records,
            cinematicWorlds.Keys.ToHashSet(), memberKeys);
        foreach (var eventRecord in eventRecords)
        {
            if (actor.Clips.Any(clip => clip.Key == eventRecord.Key))
            {
                continue;
            }

            if (!clipCache.TryGetValue(eventRecord, out var sceneClip))
            {
                sceneClip = TryReadClip(eventRecord, origins);
                clipCache[eventRecord] = sceneClip;
            }

            if (sceneClip == null)
            {
                continue;
            }

            var actorTracks = sceneClip.Tracks
                .Where(track => IsActorTrack(track, root, memberKeys))
                .ToList();
            if (actorTracks.Count == 0)
            {
                continue;
            }

            actor.Clips.Add(new()
            {
                Key = eventRecord.Key,
                Name = $"Cinematic · {cinematicWorlds[eventRecord.LoadWorldKey]} · {eventRecord.Key:X8}",
                SceneMotion = true,
                Cinematic = true,
                Loaded = true,
                Tracks = actorTracks,
                Duration = sceneClip.Duration,
                Unsupported = sceneClip.Unsupported
            });
        }
    }

    private static AnimationClip? TryReadClip(PcSceneLoader.Record eventRecord,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        try
        {
            return AnimationReader.Read(origins[eventRecord], eventRecord.Offset, eventRecord.Size);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException
            or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static bool IsActorTrack(AnimationTrack track, SceneObject root, HashSet<uint> memberKeys) =>
        track.Bone >= 0 ? track.Target == root.GaoKey : memberKeys.Contains(track.Target);
}
