namespace JadeView;

internal static class CinematicOwnerActionContributionResolver
{
    private const string MissingActionPrefix = "Missing body action ";

    internal static string[] FindResolvedRequests(CinematicPlayback playback,
        SceneDocument document, WorldData world, CinematicScriptOwnerId ownerId,
        IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners = null,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners = null,
        IReadOnlyDictionary<CinematicScriptOwnerId, float>? previewedSceneEventStartTimes = null)
    {
        var selectedOwnerIds = previewedSceneEventOwners == null
            ? new HashSet<CinematicScriptOwnerId>()
            : new HashSet<CinematicScriptOwnerId>(previewedSceneEventOwners);
        selectedOwnerIds.Add(ownerId);
        CinematicNestedEventTimeline candidateTimeline = CinematicNestedEventTransformTracks.ReadTimeline(
            playback.SourceClip, world, document, disabledCallbackOwners, selectedOwnerIds,
            previewedSceneEventStartTimes);

        var contributions = new List<string>();
        foreach (var (actor, baselineBody) in playback.BodyTimelines)
        {
            CinematicBodyTimeline candidateBody = CinematicBodyTimeline.Create(playback.SourceClip,
                actor, world.Key, document.CinematicActionKitsByOwner, candidateTimeline.Commands);
            contributions.AddRange(FindResolvedRequests(actor, baselineBody, candidateBody));
        }

        return contributions.ToArray();
    }

    internal static string[] FindResolvedRequests(SceneActor actor,
        CinematicBodyTimeline baseline, CinematicBodyTimeline candidate)
    {
        HashSet<string> unresolvedRequests = MissingRequests(baseline);
        unresolvedRequests.ExceptWith(MissingRequests(candidate));
        return unresolvedRequests.OrderBy(value => value, StringComparer.Ordinal)
            .Select(request => $"{actor.Root.Name}: {DescribeAction(request)}")
            .ToArray();
    }

    private static HashSet<string> MissingRequests(CinematicBodyTimeline timeline) =>
        timeline.Diagnostics.Where(message => message.StartsWith(MissingActionPrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static string DescribeAction(string diagnostic)
    {
        string action = diagnostic[MissingActionPrefix.Length..].TrimEnd('.');
        int actorSeparator = action.LastIndexOf(" for ", StringComparison.Ordinal);
        if (actorSeparator >= 0) action = action[..actorSeparator];
        return $"action {action}";
    }
}
