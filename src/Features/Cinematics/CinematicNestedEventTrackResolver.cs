namespace JadeView;

internal static class CinematicNestedEventTrackResolver
{
    private const int MaximumDepth = 8;
    private const int MaximumRoutes = 512;

    internal static bool HasNestedCalls(CinematicEmbeddedEventTrack track) =>
        track.Calls.Any(item => IsEventTrackCommand(item.Call));

    internal static CinematicNestedEventTrackGraph Analyze(CinematicEmbeddedEventTrackList sourceList,
        CinematicEmbeddedEventTrack sourceTrack, IEnumerable<CinematicEmbeddedEventTrackList> eventLists)
    {
        var listsByOwner = eventLists
            .Where(IsSceneEvents)
            .GroupBy(list => list.OwnerKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var routes = new List<CinematicNestedEventTrackRoute>();
        bool depthLimitReached = false;
        var root = Identity(sourceList, sourceTrack);
        Visit(sourceList, sourceTrack, 0, [$"{DescribeTrack(sourceList, sourceTrack)}"],
            new HashSet<CinematicNestedEventTrackIdentity> { root });
        return new(routes.ToArray(), depthLimitReached || routes.Count >= MaximumRoutes);

        void Visit(CinematicEmbeddedEventTrackList currentList, CinematicEmbeddedEventTrack currentTrack,
            int depth, string[] path, HashSet<CinematicNestedEventTrackIdentity> ancestry)
        {
            foreach (var (time, call) in currentTrack.Calls)
            {
                if (!IsEventTrackCommand(call)) continue;
                if (routes.Count >= MaximumRoutes) return;

                string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                string callDescription = $"{time:F3}s {function}({FormatArguments(call.Arguments)})";
                string[] nextPath = [.. path, callDescription];
                if (!listsByOwner.TryGetValue(call.Target, out var candidates))
                {
                    routes.Add(new(depth + 1, string.Join(" → ", nextPath), call.Target,
                        null, null, null, false, false,
                        "No SceneEvents list for the literal command target is loaded."));
                    continue;
                }

                var localCandidates = candidates.Where(candidate => candidate.WorldKey == currentList.WorldKey).ToArray();
                var resolvedCandidates = localCandidates.Length > 0
                    ? localCandidates
                    : candidates.Select(candidate => candidate.WorldKey).Distinct().Count() == 1
                        ? candidates
                        : [];
                if (resolvedCandidates.Length != 1)
                {
                    routes.Add(new(depth + 1, string.Join(" → ", nextPath), call.Target,
                        null, null, null, false, false,
                        resolvedCandidates.Length == 0
                            ? "Matching SceneEvents lists exist in multiple source worlds; the target world is ambiguous."
                            : "More than one SceneEvents list matches this target in the selected world."));
                    continue;
                }

                CinematicEmbeddedEventTrackList targetList = resolvedCandidates[0];
                CinematicEmbeddedEventTrack[] targetTracks = SelectTracks(function, call.Arguments, targetList);
                if (targetTracks.Length == 0)
                {
                    string reason = IsAllTracksCommand(function)
                        ? "The resolved SceneEvents list has no decoded channels."
                        : call.Arguments.Length == 0
                            ? "The event-track index argument is missing."
                            : $"Event-track index {unchecked((int)call.Arguments[0])} is not present in the resolved SceneEvents list.";
                    routes.Add(new(depth + 1, string.Join(" → ", nextPath), call.Target,
                        targetList, null, null, false, false, reason));
                    continue;
                }

                foreach (CinematicEmbeddedEventTrack targetTrack in targetTracks)
                {
                    if (routes.Count >= MaximumRoutes) return;
                    var targetIdentity = Identity(targetList, targetTrack);
                    string[] routePath = [.. nextPath, DescribeTrack(targetList, targetTrack)];
                    bool cycle = ancestry.Contains(targetIdentity);
                    routes.Add(new(depth + 1, string.Join(" → ", routePath), call.Target,
                        targetList, targetTrack, targetTrack.Index, true, cycle,
                        cycle ? "Cycle detected; traversal stopped at this repeated channel." : null));
                    if (cycle) continue;
                    if (depth + 1 >= MaximumDepth)
                    {
                        depthLimitReached |= HasNestedCalls(targetTrack);
                        continue;
                    }

                    var nextAncestry = new HashSet<CinematicNestedEventTrackIdentity>(ancestry)
                    {
                        targetIdentity
                    };
                    Visit(targetList, targetTrack, depth + 1, routePath, nextAncestry);
                }
            }
        }
    }

    private static CinematicEmbeddedEventTrack[] SelectTracks(string function, uint[] arguments,
        CinematicEmbeddedEventTrackList list)
    {
        if (IsAllTracksCommand(function)) return list.Tracks;
        if (arguments.Length == 0) return [];
        int requestedIndex = unchecked((int)arguments[0]);
        return list.Tracks.Where(track => track.Index == requestedIndex).ToArray();
    }

    private static bool IsSceneEvents(CinematicEmbeddedEventTrackList list) =>
        list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase);

    private static bool IsEventTrackCommand(CinematicLiteralCall call) =>
        IsEventTrackFunction(CinematicEngineFunctionCatalog.NameFor(call.Selector));

    private static bool IsEventTrackFunction(string function) => function is
        "TRAPlay" or "TRAStop" or "TRAPause" or "TRAUnPause" or "TRAReinit" or "TRAIsStop"
        or "TRAPlayAll" or "TRAStopAll" or "TRAPauseAll" or "TRAUnPauseAll"
        or "TRAReinitAll" or "TRAIsStopAll";

    private static bool IsAllTracksCommand(string function) => function.EndsWith("All", StringComparison.Ordinal);

    private static CinematicNestedEventTrackIdentity Identity(CinematicEmbeddedEventTrackList list,
        CinematicEmbeddedEventTrack track) => new(list.WorldKey, list.OwnerKey, list.EventKey, track.Index);

    private static string DescribeTrack(CinematicEmbeddedEventTrackList list, CinematicEmbeddedEventTrack track)
    {
        string target = track.TargetKey switch
        {
            0 => "owner/default target",
            uint.MaxValue => "invalid target",
            _ => $"target {track.TargetKey:X8}"
        };
        string[] components = track.SourceTrack?.Keys is { } keys
            ? new[]
            {
                keys.Any(key => key.Position != null) ? "translation" : null,
                keys.Any(key => key.Rotation != null) ? "rotation" : null,
                keys.Any(key => key.Scale != null) ? "scale" : null
            }.OfType<string>().ToArray()
            : [];
        string transformSummary = components.Length > 0
            ? $" · transform keys: {string.Join(", ", components)}"
            : "";
        string channelData = DescribeChannelData(track);
        return $"{list.OwnerName} / {list.LinkName} {list.EventKey:X8} / EVE {track.Index} · "
            + $"{target} · bone {track.Bone} · {track.KeyCount} keys · {track.Duration:F3}s"
            + $"{transformSummary}{channelData}";
    }

    private static string DescribeChannelData(CinematicEmbeddedEventTrack track)
    {
        if (track.SourceTrack is not { } sourceTrack)
            return " · channel data unavailable";

        string keyKinds = string.Join(", ", sourceTrack.Keys
            .SelectMany(DescribeKeyKinds)
            .GroupBy(kind => kind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}×{group.Count()}"));
        string keyFlags = string.Join(", ", sourceTrack.Keys
            .GroupBy(key => key.Flags)
            .OrderBy(group => group.Key)
            .Select(group => $"0x{group.Key:X4}×{group.Count()}"));
        string payloadSummary = keyKinds.Length == 0 ? "no decoded key payload" : keyKinds;
        string flagSummary = keyFlags.Length == 0 ? "none" : keyFlags;
        return $" · channel 0x{sourceTrack.ChannelFlags:X4} · key data: {payloadSummary}"
            + $" · key flags: {flagSummary} · decoded TRA calls: {track.Calls.Length}"
            + $" · decoded messages: {track.Messages.Length}";
    }

    private static IEnumerable<string> DescribeKeyKinds(AnimationKey key)
    {
        bool hasTransform = key.Position != null || key.Rotation != null || key.Scale != null;
        if (hasTransform)
        {
            if (key.Position != null) yield return "translation";
            if (key.Rotation != null) yield return "rotation";
            if (key.Scale != null) yield return "scale";
        }

        foreach (string payloadKind in key.OpaquePayloads.Select(payload => payload.Kind)
            .Distinct(StringComparer.Ordinal))
            yield return payloadKind;

        if (!hasTransform && key.OpaquePayloads.Count == 0)
            yield return "unclassified key";
        if (key.TimeCurve != null)
            yield return "time curve";
    }

    private static string FormatArguments(uint[] arguments) =>
        string.Join(", ", arguments.Select(argument => $"0x{argument:X8}"));
}

internal sealed record CinematicNestedEventTrackGraph(CinematicNestedEventTrackRoute[] Routes,
    bool WasTruncated);

internal sealed record CinematicNestedEventTrackRoute(int Depth, string Path, uint TargetOwnerKey,
    CinematicEmbeddedEventTrackList? TargetList, CinematicEmbeddedEventTrack? TargetTrack,
    int? TargetTrackIndex, bool IsResolved, bool IsCycle, string? Limitation);

internal readonly record struct CinematicNestedEventTrackIdentity(uint WorldKey, uint OwnerKey,
    uint EventKey, int TrackIndex);
