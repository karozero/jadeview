namespace JadeView;

internal static class CinematicEmbeddedEventTrackResolver
{
    internal static string? Describe(CinematicLiteralCall call, SceneDocument document, uint worldKey)
    {
        string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        if (!IsEventTrackFunction(function)) return null;

        if (!TryResolveLists(call.Target, worldKey, document, out var lists, out string? scopeNote))
        {
            int sourceWorldCount = document.CinematicEmbeddedEventTracks.Keys
                .Where(key => key.OwnerKey == call.Target)
                .Where(key => document.CinematicEmbeddedEventTracks[key].Any(IsRuntimeSceneEventList))
                .Select(key => key.WorldKey)
                .Distinct()
                .Count();
            return sourceWorldCount > 1
                ? "target EVE lists exist in multiple source worlds; no unique world-scoped owner can be selected"
                : "target embedded EVE track list is not present in the loaded archive scope";
        }

        if (function.EndsWith("All", StringComparison.Ordinal) || function == "TRASetTime")
        {
            int count = lists.Sum(list => list.Tracks.Length);
            string links = string.Join(", ", lists.Select(list =>
                $"{list.LinkName} {list.EventKey:X8} ({list.Tracks.Length} tracks)"));
            return $"target EVE lists contain {count} track(s){scopeNote}: {links}";
        }

        if (call.Arguments.Length == 0)
            return "event-track index is missing from this literal call";
        int index = unchecked((int)call.Arguments[0]);
        foreach (var list in lists)
        {
            var track = list.Tracks.FirstOrDefault(candidate => candidate.Index == index);
            if (track == null) continue;
            string target = track.TargetKey switch
            {
                0 => "owner/default target",
                uint.MaxValue => "invalid target key",
                _ => DescribeTarget(track.TargetKey, list.WorldKey, document)
            };
            return $"{list.LinkName} {list.EventKey:X8} · EVE track {index}{scopeNote} · {target} · "
                + $"bone {track.Bone} · {track.KeyCount} keys · {track.Duration:F2}s";
        }

        int totalTracks = lists.Sum(list => list.Tracks.Length);
        return $"EVE track index {index} is outside the {totalTracks} loaded track(s) on this target";
    }

    private static bool TryResolveLists(uint ownerKey, uint worldKey, SceneDocument document,
        out CinematicEmbeddedEventTrackList[] lists, out string scopeNote)
    {
        if (document.CinematicEmbeddedEventTracks.TryGetValue((worldKey, ownerKey), out var worldLists))
        {
            lists = worldLists.Where(IsRuntimeSceneEventList).ToArray();
            scopeNote = "";
            return lists.Length > 0;
        }

        var fallbackLists = document.CinematicEmbeddedEventTracks
            .Where(entry => entry.Key.OwnerKey == ownerKey)
            .SelectMany(entry => entry.Value)
            .Where(IsRuntimeSceneEventList)
            .ToArray();
        if (fallbackLists.Length > 0 && fallbackLists.Select(list => list.WorldKey).Distinct().Count() == 1)
        {
            lists = fallbackLists;
            scopeNote = $" (source world {fallbackLists[0].WorldKey:X8})";
            return true;
        }

        lists = [];
        scopeNote = "";
        return false;
    }

    private static bool IsRuntimeSceneEventList(CinematicEmbeddedEventTrackList list) =>
        list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase);

    private static string DescribeTarget(uint targetKey, uint worldKey, SceneDocument document)
    {
        var references = CinematicScriptNamedReferenceCatalog.ForDocument(document);
        var resolved = CinematicScriptReferenceNameResolver.Resolve(targetKey, worldKey, references);
        return resolved == null
            ? $"target {targetKey:X8}"
            : $"target {resolved.Name} ({targetKey:X8})";
    }

    private static bool IsEventTrackFunction(string function) => function is
        "TRAPlay" or "TRAStop" or "TRAPause" or "TRAUnPause" or "TRAReinit" or "TRAIsStop"
        or "TRAPlayAll" or "TRAStopAll" or "TRAPauseAll" or "TRAUnPauseAll"
        or "TRAReinitAll" or "TRAIsStopAll" or "TRASetTime";
}
