namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private HashSet<uint> DetachRemovedObjects()
    {
        var removals = FindRemovedObjects();
        var ownedKeys = FindRemainingOwners(removals);
        var removedKeys = removals.Select(removal => removal.Key).Where(key => !ownedKeys.Contains(key)).ToHashSet();
        // Detached cinematic/prefab links can retain dependencies across scene visits.
        foreach (var record in replay.Records.Where(record => record.GaoReferenceOffsets.Count > 0))
            DetachObjectReferences(record, removedKeys);
        return removedKeys;
    }

    private HashSet<(uint World, uint Key)> FindRemovedObjects()
    {
        var removals = new HashSet<(uint World, uint Key)>();
        foreach (var change in changes.Where(change => change.Kind == "remove"))
        {
            var sceneObject = WorldObjects(change.WorldKey).FirstOrDefault(candidate => candidate.Id == change.ObjectId) ?? throw new InvalidDataException($"Deleted object '{change.Name}' no longer matches the source scene.");
            removals.Add((change.WorldKey, sceneObject.GaoKey));
        }

        return removals;
    }

    private HashSet<uint> FindRemainingOwners(HashSet<(uint World, uint Key)> removals)
    {
        var ownedKeys = changes.Where(change => change.Kind == "add").Select(change => change.GaoKey).ToHashSet();
        foreach (var world in replay.Worlds)
        {
            var primaryGroups = replay.Records.Where(record => record.Kind == 'n' && record.Key == world.Group && record.LoadWorldKey == world.Key);
            var remainingMembers = primaryGroups.SelectMany(group => group.References).Where(reference => !removals.Contains((world.Key, reference.Key)));
            ownedKeys.UnionWith(remainingMembers.Select(reference => reference.Key));
        }

        return ownedKeys;
    }

    private void DetachObjectReferences(PcSceneLoader.Record record, HashSet<uint> removedKeys)
    {
        byte[] sourceFrame = Frame(local, record);
        byte[] patched = PcSceneReferences.Remove(sourceFrame.ToArray(), record, removedKeys);
        if (sourceFrame.AsSpan().SequenceEqual(patched))
            return;
        bool hasConflictingPayload = detachedReferences.TryGetValue((record.Key, record.Kind), out var existing) && !existing.AsSpan().SequenceEqual(patched);
        if (hasConflictingPayload)
            throw new InvalidDataException($"Resource {record.Key:X8} has conflicting object references.");
        detachedReferences[(record.Key, record.Kind)] = patched;
    }
}
