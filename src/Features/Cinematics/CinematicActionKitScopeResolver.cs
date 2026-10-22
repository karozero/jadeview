namespace JadeView;

internal static class CinematicActionKitScopeResolver
{
    internal static CinematicActionKitResolution Resolve(uint ownerKey, uint preferredWorldKey,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> actionKits)
    {
        if (actionKits.TryGetValue((preferredWorldKey, ownerKey), out var localActions))
            return new(localActions, preferredWorldKey, "");

        if (preferredWorldKey != 0 && actionKits.TryGetValue((0, ownerKey), out var sharedActions))
            return new(sharedActions, 0, "");

        var candidates = actionKits.Where(entry => entry.Key.OwnerKey == ownerKey).ToArray();
        if (candidates.Length == 1)
            return new(candidates[0].Value, candidates[0].Key.WorldKey, "");

        if (candidates.Length > 1)
            return new(null, null,
                $"The target has action tables in multiple source worlds ({string.Join(", ", candidates.Select(candidate => candidate.Key.WorldKey.ToString("X8")))}); the target world has no local or shared table, so the link is ambiguous.");

        return new(null, null, "The target has no decoded action table in the loaded archive scope.");
    }
}

internal sealed record CinematicActionKitResolution(Dictionary<int, CinematicAction>? Actions,
    uint? SourceWorldKey, string Failure)
{
    internal bool IsResolved => Actions != null;
}
