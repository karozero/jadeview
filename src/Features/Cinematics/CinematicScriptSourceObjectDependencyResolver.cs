namespace JadeView;

internal static class CinematicScriptSourceObjectDependencyResolver
{
    internal static CinematicScriptNamedReference[] ResolveUniqueObjects(
        IEnumerable<CinematicScriptReachableSourceReference> sourceReferences,
        IEnumerable<CinematicScriptNamedReference> availableReferences)
    {
        var availableObjects = availableReferences
            .Where(reference => reference.IsObject && CinematicObjectKey.IsArchiveObject(reference.Key))
            .DistinctBy(reference => (reference.WorldKey, reference.Key))
            .GroupBy(reference => reference.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return sourceReferences
            .Where(reference => reference.Kind == "Object" && CinematicObjectKey.IsArchiveObject(reference.Key))
            .Select(reference => availableObjects.GetValueOrDefault(reference.Key) is
                { Length: 1 } candidates ? candidates[0] : null)
            .OfType<CinematicScriptNamedReference>()
            .DistinctBy(reference => (reference.WorldKey, reference.Key))
            .OrderBy(reference => reference.WorldKey)
            .ThenBy(reference => reference.Key)
            .ToArray();
    }
}
