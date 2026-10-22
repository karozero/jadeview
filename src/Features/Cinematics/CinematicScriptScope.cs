namespace JadeView;

internal static class CinematicScriptScope
{
    internal static CinematicScriptOwner[] ForWorld(IEnumerable<CinematicScriptOwner> owners,
        uint cinematicWorldKey, IEnumerable<uint> worldObjectKeys,
        IEnumerable<CinematicScriptEventTrack>? ownerEventTracks = null) =>
        ForClip(owners, cinematicWorldKey, worldObjectKeys, [],
            ownerEventTracks: ownerEventTracks);

    internal static CinematicScriptOwner[] ForClip(IEnumerable<CinematicScriptOwner> owners,
        uint cinematicWorldKey, IEnumerable<uint> actorKeys, IEnumerable<uint> trackTargets,
        IEnumerable<CinematicTimelineMessage>? trackMessages = null,
        CinematicScriptOwnerId? selectedOwnerId = null,
        IEnumerable<CinematicScriptEventTrack>? ownerEventTracks = null)
    {
        var allOwners = owners.ToArray();
        var ownersById = allOwners.ToDictionary(owner => owner.Id);
        var worldOwners = allOwners.Where(owner => owner.WorldKey == cinematicWorldKey).ToArray();
        var includedOwnerIds = worldOwners.Select(owner => owner.Id).ToHashSet();
        var visitedOwnerIds = new HashSet<CinematicScriptOwnerId>();
        var pendingOwnerIds = new Queue<CinematicScriptOwnerId>();

        foreach (var owner in worldOwners)
            EnqueueOwner(owner);
        foreach (uint key in actorKeys.Concat(trackTargets))
            Enqueue(key, cinematicWorldKey);
        if (trackMessages != null)
            foreach (uint key in trackMessages.SelectMany(message =>
                         new[] { message.ExecutionObjectKey, message.Gao, message.Gao2 }))
                Enqueue(key, cinematicWorldKey);
        if (selectedOwnerId is { } selected && ownersById.ContainsKey(selected))
            Enqueue(selected.OwnerKey, selected.WorldKey);

        var tracksByOwnerId = (ownerEventTracks ?? []).GroupBy(track => track.OwnerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        while (pendingOwnerIds.TryDequeue(out var pendingOwnerId))
        {
            var owner = ownersById[pendingOwnerId];
            foreach (var reference in owner.ObjectReferences)
                Enqueue(reference.TargetKey, owner.WorldKey);
            if (!tracksByOwnerId.TryGetValue(owner.Id, out var eventTracks)) continue;
            foreach (uint key in eventTracks.SelectMany(GetReferencedKeys))
                Enqueue(key, owner.WorldKey);
        }

        return allOwners.Where(owner => includedOwnerIds.Contains(owner.Id)).ToArray();

        void Enqueue(uint key, uint preferredWorldKey)
        {
            if (key is 0 or uint.MaxValue) return;
            var owner = CinematicScriptOwnerResolver.Resolve(key, preferredWorldKey, allOwners);
            if (owner != null) EnqueueOwner(owner);
        }

        void EnqueueOwner(CinematicScriptOwner owner)
        {
            includedOwnerIds.Add(owner.Id);
            if (visitedOwnerIds.Add(owner.Id)) pendingOwnerIds.Enqueue(owner.Id);
        }

        static IEnumerable<uint> GetReferencedKeys(CinematicScriptEventTrack track)
        {
            foreach (var (_, call) in track.Calls)
                yield return call.Target;
            foreach (var (_, message) in track.Messages)
            {
                yield return message.ExecutionObjectKey;
                yield return message.Gao;
                yield return message.Gao2;
            }
        }
    }
}
