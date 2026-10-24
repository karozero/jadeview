namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private Result Run()
    {
        try
        {
            var roots = ReadWorldRoots();
            result.OrderedWorldRootKeys = roots.ToArray();
            var mergedWorldKeys = new HashSet<uint>();
            foreach (uint root in roots)
            {
                if (!ReadWorld(root, mergedWorldKeys))
                    continue;
                uint firstRoot = roots.First(key => key is not (0 or uint.MaxValue));
                if (root != firstRoot)
                    cache.Remove(root);
            }

            if (cursor != dataLength)
                throw new InvalidDataException($"FF0 traversal left {dataLength - cursor} bytes at {cursor:X8}.");
        }
        catch (Exception error)when (error is InvalidDataException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            result.Error = $"{error.Message} Last: {currentRecord?.Kind} {currentRecord?.Key:X8}, cursor {cursor:X8}.";
        }

        result.End = cursor;
        return result;
    }

    private List<uint> ReadWorldRoots()
    {
        bool isWorldList = PcWorldListHeaderReader.TryRead(dataBytes.AsSpan(0, dataLength), null,
            out GameWorldListDescriptor? descriptor, out int consumedBytes, out string error);
        if (!isWorldList || descriptor == null)
            throw new InvalidDataException(error);
        List<uint> roots = descriptor.OrderedWorldRootKeys.ToList();

        // The native loader bypasses SPE for the built-in FIX container.
        fixedResources = roots.Count == 1 && roots[0] == 0x5E0084DE;
        cursor = consumedBytes;
        return roots;
    }

    private bool ReadWorld(uint root, HashSet<uint> mergedWorldKeys)
    {
        loadingWorldKey = root;
        EnqueueRequest(root, 'z');
        DrainRequests();
        if (!result.Resources.TryGetValue(root, out var world))
            return false;
        if (!result.Resources.TryGetValue(world.Group, out var primaryList))
            return false;
        var expansion = new WorldExpansion(CachedObjects(primaryList));
        foreach (var member in expansion.Members.ToArray())
        {
            bool canExpandActions = (member.Flags & 3) != 1;
            if (canExpandActions)
                ExpandActions(member, expansion);
        }

        world.WorldMembers.AddRange(expansion.Members);
        foreach (var member in expansion.Members)
            ResolveLightComponent(member);
        if (expansion.Members.Any(member => (member.Flags & 2) != 0))
            world.ActorResources = new(cache);
        mergedWorldKeys.UnionWith(expansion.MemberKeys);
        EvictExternalReferences(mergedWorldKeys);
        return true;
    }

    private List<Record> CachedObjects(Record list) => list.References.Select(reference => cache.GetValueOrDefault(reference.Key)).Where(record => record?.Kind == 'l').Cast<Record>().ToList();

    private void ResolveLightComponent(Record owner)
    {
        if (owner.LightKey is 0 or uint.MaxValue) return;
        if (!cache.TryGetValue(owner.LightKey, out var resource) || resource.Kind != 'm')
        {
            owner.LightError = $"Light resource {owner.LightKey:X8} is absent from the loaded world scope.";
            return;
        }
        if (PcLightReader.TryReadGraphic(dataBytes.AsSpan(resource.Offset, resource.Size), out var light, out string error))
        {
            owner.Light = light;
            owner.LightError = null;
        }
        else owner.LightError = $"Light resource {owner.LightKey:X8}: {error}";
    }
    private List<Record> GroupMembers(uint groupKey)
    {
        if (!cache.TryGetValue(groupKey, out var group))
            return[];
        var listReference = group.References.FirstOrDefault();
        if (listReference == null || !cache.TryGetValue(listReference.Key, out var list))
            return[];
        return CachedObjects(list);
    }

    private void ExpandActions(Record sceneObject, WorldExpansion expansion)
    {
        bool hasNewActions = (sceneObject.Flags & 2) != 0 && expansion.Processing.Add(sceneObject.Key);
        if (!hasNewActions)
            return;
        if (expansion.Processing.Count > 256)
            throw new InvalidDataException("FF0 action-group nesting exceeds limit.");
        EnqueueRequest(sceneObject.Shape, 'c');
        EnqueueRequest(sceneObject.Actions, 'k');
        DrainRequests();
        foreach (var child in GroupMembers(sceneObject.Actions))
            ExpandActionMember(child, expansion);
        if (sceneObject.Skeleton is 0 or uint.MaxValue)
        {
            EnqueueRequest(sceneObject.Events, 'i');
            DrainRequests();
        }

        expansion.Processing.Remove(sceneObject.Key);
    }

    private void ExpandActionMember(Record child, WorldExpansion expansion)
    {
        if (expansion.MemberKeys.Contains(child.Key))
            return;
        ExpandActions(child, expansion);
        expansion.Add(child);
        if ((child.Flags & 0x800000) == 0)
            return;
        foreach (var nested in GroupMembers(child.Prefab))
        {
            ExpandActions(nested, expansion);
            expansion.Add(nested);
        }
    }

    private void EvictExternalReferences(HashSet<uint> mergedWorldKeys)
    {
        // Native y.b(z) runs on the merged world and detaches evicted parents.
        foreach (uint objectKey in mergedWorldKeys)
        {
            if (!cache.TryGetValue(objectKey, out var sceneObject))
                continue;
            bool hasExternalParent = (sceneObject.Flags & 0x400000) != 0 && sceneObject.Parent != uint.MaxValue;
            if (hasExternalParent)
                EvictExternal(sceneObject.Parent, mergedWorldKeys);
            bool hasPrefab = (sceneObject.Flags & 0x800000) != 0 && sceneObject.Prefab is not (0 or uint.MaxValue);
            if (!hasPrefab)
                continue;
            foreach (var member in PrefabReferences(sceneObject.Prefab))
                EvictExternal(member.Key, mergedWorldKeys);
        }
    }

    private IEnumerable<Request> PrefabReferences(uint prefabKey)
    {
        if (!cache.TryGetValue(prefabKey, out var prefab))
            return[];
        var groupReference = prefab.References.FirstOrDefault();
        if (groupReference == null || !cache.TryGetValue(groupReference.Key, out var members))
            return[];
        return members.References;
    }

    private void EvictExternal(uint key, HashSet<uint> mergedWorldKeys)
    {
        bool wasEvicted = !mergedWorldKeys.Contains(key) && cache.Remove(key);
        if (!wasEvicted)
            return;
        foreach (uint memberKey in mergedWorldKeys)
        {
            if (cache.TryGetValue(memberKey, out var child) && child.Parent == key)
                child.Parent = uint.MaxValue;
        }
    }
}
