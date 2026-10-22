namespace JadeView;

internal static class CinematicScriptOwnerResolver
{
    internal static CinematicScriptOwner? Resolve(uint ownerKey, uint preferredWorldKey,
        IEnumerable<CinematicScriptOwner> owners)
    {
        CinematicScriptOwner[] candidates = owners.Where(owner => owner.OwnerKey == ownerKey).ToArray();
        CinematicScriptOwner[] localCandidates = candidates
            .Where(owner => owner.WorldKey == preferredWorldKey).ToArray();
        if (localCandidates.Length == 1) return localCandidates[0];
        if (localCandidates.Length > 1) return null;
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
