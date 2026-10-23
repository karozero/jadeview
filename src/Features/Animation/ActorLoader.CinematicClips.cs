namespace JadeView;

internal static partial class ActorLoader
{
    private static void AddLinkedSceneMotionClips(SceneActor actor,
        PcSceneLoader.Result replay, IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlySet<uint> cinematicWorldKeys,
        IReadOnlyDictionary<uint, string> cinematicWorlds,
        Dictionary<PcSceneLoader.Record, AnimationClip?> clipCache)
    {
        var memberKeys = actor.Members.Select(member => member.GaoKey).ToHashSet();
        var linkedRecords = CinematicActorClipDiscovery.FindActorClips(
            replay.Records, cinematicWorldKeys, memberKeys);

        foreach (var record in linkedRecords)
        {
            if (!clipCache.TryGetValue(record, out AnimationClip? sourceClip))
            {
                sourceClip = TryReadClip(record, origins);
                clipCache[record] = sourceClip;
            }

            if (sourceClip == null) continue;
            var actorTracks = sourceClip.Tracks
                .Where(track => IsActorTrack(track, actor.Root, memberKeys))
                .ToList();
            if (actorTracks.Count == 0) continue;

            bool isCinematic = cinematicWorlds.TryGetValue(record.LoadWorldKey, out string? worldName);
            string clipName = isCinematic
                ? $"Cinematic · {worldName} · {record.Key:X8}"
                : $"{record.Key:X8} (scene motion)";
            actor.Clips.Add(new()
            {
                Key = record.Key,
                Name = clipName,
                SceneMotion = true,
                Cinematic = isCinematic,
                Loaded = true,
                Tracks = actorTracks,
                Duration = sourceClip.Duration,
                Unsupported = sourceClip.Unsupported
            });
        }
    }

    private static void AddCinematicClips(SceneActor actor, SceneObject root,
        PcSceneLoader.Result replay, Dictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> cinematicWorlds,
        Dictionary<PcSceneLoader.Record, AnimationClip?> clipCache)
    {
        var memberKeys = actor.Members.Select(member => member.GaoKey).ToHashSet();

        var eventRecords = CinematicActorClipDiscovery.FindActorClips(replay.Records,
            cinematicWorlds.Keys.ToHashSet(), memberKeys);
        foreach (var eventRecord in eventRecords.Where(record =>
            cinematicWorlds.ContainsKey(record.LoadWorldKey)))
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
