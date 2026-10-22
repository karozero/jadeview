namespace JadeView;

internal sealed record CinematicScriptOwnerReferenceCandidate(
    string Container, uint WorldKey, uint OwnerKey, string OwnerName);

internal sealed record CinematicScriptOwnerReferenceResolution(
    CinematicScriptOwnerReferenceCandidate? Owner, string Status,
    CinematicScriptOwnerReferenceCandidate[] Candidates)
{
    internal bool IsResolved => Owner != null;
}

internal static class CinematicScriptOwnerReferenceResolver
{
    internal static CinematicScriptOwnerReferenceResolution Resolve(string sourceContainer,
        uint sourceWorldKey, uint targetOwnerKey,
        IEnumerable<CinematicScriptOwnerReferenceCandidate> ownerCandidates)
    {
        var matchingOwners = ownerCandidates
            .Where(candidate => candidate.OwnerKey == targetOwnerKey)
            .DistinctBy(candidate => (candidate.Container, candidate.WorldKey,
                candidate.OwnerKey, candidate.OwnerName))
            .ToArray();
        if (matchingOwners.Length == 0)
            return new(null, "no matching script owner", []);

        var localOwners = matchingOwners.Where(candidate =>
            string.Equals(candidate.Container, sourceContainer, StringComparison.OrdinalIgnoreCase)
            && candidate.WorldKey == sourceWorldKey).ToArray();
        if (TryResolve(localOwners, "same container and world", out var localResolution))
            return localResolution;
        if (localOwners.Length > 1)
            return new(null, "ambiguous in the same container and world", localOwners);

        var worldOwners = matchingOwners.Where(candidate => candidate.WorldKey == sourceWorldKey)
            .DistinctBy(candidate => (candidate.WorldKey, candidate.OwnerKey, candidate.OwnerName))
            .ToArray();
        if (TryResolve(worldOwners, "unique in the source world", out var worldResolution))
            return worldResolution;
        if (worldOwners.Length > 1)
            return new(null, "ambiguous in the source world", worldOwners);

        var globallyUniqueOwners = matchingOwners
            .DistinctBy(candidate => (candidate.WorldKey, candidate.OwnerKey, candidate.OwnerName))
            .ToArray();
        if (TryResolve(globallyUniqueOwners, "unique across loaded worlds", out var globalResolution))
            return globalResolution;
        return new(null, "ambiguous across loaded worlds", globallyUniqueOwners);
    }

    private static bool TryResolve(CinematicScriptOwnerReferenceCandidate[] candidates,
        string status, out CinematicScriptOwnerReferenceResolution resolution)
    {
        if (candidates.Length == 1)
        {
            resolution = new(candidates[0], status, candidates);
            return true;
        }
        resolution = new(null, status, candidates);
        return false;
    }
}
