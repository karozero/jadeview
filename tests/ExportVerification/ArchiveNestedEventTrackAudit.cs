using System.Text.Json;
using JadeView;

internal static class ArchiveNestedEventTrackAudit
{
    internal static void Write(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var entries = archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000).ToArray();
        int containersParsed = 0;
        int containersFailed = 0;
        int sourceTracks = 0;
        int routes = 0;
        int resolvedRoutes = 0;
        int ambiguousOrMissingRoutes = 0;
        int cycleStops = 0;
        int truncatedRoots = 0;
        var samples = new List<object>();
        int ownerWideAutoLoopRoots = 0;
        int ownerWideRunningInitPeers = 0;
        var ownerLoopSamples = new List<object>();

        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            var entry = entries[entryIndex];
            SceneDocument document;
            try
            {
                document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
                containersParsed++;
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                or OverflowException or IndexOutOfRangeException)
            {
                containersFailed++;
                continue;
            }

            var lists = document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists).ToArray();
            foreach (var list in lists)
            foreach (var loopRoot in list.Tracks.Where(track => track.Bone == 255
                         && (track.ChannelFlags & CinematicEventTrackFlags.AutoLoop) != 0))
            {
                var runningInitPeers = list.Tracks.Where(track => track.Index != loopRoot.Index
                        && (track.ChannelFlags & CinematicEventTrackFlags.RunningInit) != 0)
                    .Select(track => track.Index).Order().ToArray();
                ownerWideAutoLoopRoots++;
                ownerWideRunningInitPeers += runningInitPeers.Length;
                if (ownerLoopSamples.Count < 1000)
                    ownerLoopSamples.Add(new
                    {
                        Container = entry.Name,
                        World = list.WorldKey.ToString("X8"),
                        Owner = list.OwnerKey.ToString("X8"),
                        list.OwnerName,
                        Event = list.EventKey.ToString("X8"),
                        RootChannel = loopRoot.Index,
                        loopRoot.Duration,
                        RunningInitPeers = runningInitPeers
                    });
            }

            foreach (var list in lists)
            foreach (var track in list.Tracks.Where(CinematicNestedEventTrackResolver.HasNestedCalls))
            {
                sourceTracks++;
                var graph = CinematicNestedEventTrackResolver.Analyze(list, track, lists);
                routes += graph.Routes.Length;
                resolvedRoutes += graph.Routes.Count(route => route.IsResolved);
                ambiguousOrMissingRoutes += graph.Routes.Count(route => !route.IsResolved);
                cycleStops += graph.Routes.Count(route => route.IsCycle);
                if (graph.WasTruncated) truncatedRoots++;

                if (samples.Count < 1000 && graph.Routes.Length > 0)
                    samples.Add(new
                    {
                        Container = entry.Name,
                        World = list.WorldKey.ToString("X8"),
                        Owner = list.OwnerKey.ToString("X8"),
                        list.OwnerName,
                        Event = list.EventKey.ToString("X8"),
                        SourceTrack = track.Index,
                        Routes = graph.Routes.Select(route => new
                        {
                            route.Depth,
                            route.Path,
                            TargetOwner = route.TargetOwnerKey.ToString("X8"),
                            TargetEvent = route.TargetList?.EventKey.ToString("X8"),
                            route.TargetTrackIndex,
                            route.IsResolved,
                            route.IsCycle,
                            route.Limitation
                        })
                    });
            }

            if ((entryIndex + 1) % 20 == 0 || entryIndex + 1 == entries.Length)
                Console.WriteLine($"NESTED EVE AUDIT: {entryIndex + 1}/{entries.Length} containers");
        }

        Directory.CreateDirectory(output);
        string path = Path.Combine(output, "archive-nested-event-track-audit.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Archive = archive.PathName,
            ContainersDiscovered = entries.Length,
            ContainersParsed = containersParsed,
            ContainersFailed = containersFailed,
            SourceTracksWithNestedCalls = sourceTracks,
            RouteCount = routes,
            ResolvedRouteCount = resolvedRoutes,
            AmbiguousOrMissingRouteCount = ambiguousOrMissingRoutes,
            CycleStopCount = cycleStops,
            TruncatedRootCount = truncatedRoots,
            OwnerWideAutoLoopRootCount = ownerWideAutoLoopRoots,
            OwnerWideRunningInitPeerCount = ownerWideRunningInitPeers,
            OwnerWideAutoLoopSamples = ownerLoopSamples,
            Limits = "This is a static traversal of decoded SceneEvents TRA* links within each container. It does not execute callbacks, evaluate gates or prove that any channel was started. Cross-container references are unresolved unless their owner data is present in the same parsed container.",
            Samples = samples
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ARCHIVE NESTED EVE AUDIT: {sourceTracks} source tracks, {resolvedRoutes} resolved routes, {ambiguousOrMissingRoutes} unresolved routes; wrote {path}");
    }
}
