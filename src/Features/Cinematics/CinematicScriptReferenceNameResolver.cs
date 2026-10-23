namespace JadeView;

internal sealed record CinematicScriptNamedReference(uint WorldKey, uint Key, string Name, bool IsObject);

internal static class CinematicScriptReferenceNameResolver
{
    internal static CinematicScriptNamedReference? Resolve(uint key, uint preferredWorldKey,
        IEnumerable<CinematicScriptNamedReference> references)
    {
        if (!CinematicObjectKey.IsArchiveObject(key)) return null;
        CinematicScriptNamedReference[] candidates = references.Where(reference => reference.Key == key).ToArray();

        CinematicScriptNamedReference? localObject = candidates.FirstOrDefault(reference =>
            reference.WorldKey == preferredWorldKey && reference.IsObject);
        if (localObject != null) return localObject;

        CinematicScriptNamedReference? localOwner = candidates.FirstOrDefault(reference =>
            reference.WorldKey == preferredWorldKey && !reference.IsObject);
        if (localOwner != null) return localOwner;

        CinematicScriptNamedReference[] externalObjects = candidates
            .Where(reference => reference.WorldKey != preferredWorldKey && reference.IsObject)
            .DistinctBy(reference => (reference.WorldKey, reference.Key))
            .ToArray();
        if (externalObjects.Length == 1) return externalObjects[0];
        if (externalObjects.Length > 1) return null;

        CinematicScriptNamedReference[] externalOwners = candidates
            .Where(reference => reference.WorldKey != preferredWorldKey && !reference.IsObject)
            .DistinctBy(reference => (reference.WorldKey, reference.Key))
            .ToArray();
        return externalOwners.Length == 1 ? externalOwners[0] : null;
    }
}
