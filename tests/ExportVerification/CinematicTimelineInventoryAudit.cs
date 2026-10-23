using System.Text.Json;
using JadeView;

internal static class CinematicTimelineInventoryAudit
{
    internal static void Write(string archivePath, string container, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        var cinematicWorlds = document.Worlds
            .Where(world => world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase))
            .Select(world => world.Key).ToHashSet();
        var owners = document.CinematicScriptOwners.Values
            .Where(owner => cinematicWorlds.Contains(owner.WorldKey)).ToArray();
        var linkedTracks = document.CinematicScriptEventTracks.Values
            .SelectMany(tracks => tracks)
            .Where(track => cinematicWorlds.Contains(track.WorldKey)).ToArray();
        var report = new
        {
            Container = entry.Id,
            RootSceneEvents = document.CinematicClips.Values.Select(clip => new
            {
                Key = clip.Key.ToString("X8"),
                World = clip.CinematicWorldKey.ToString("X8"),
                clip.Name,
                clip.Duration,
                TrackCount = clip.Tracks.Count,
                Owners = owners.Where(owner => owner.SceneEventsKey == clip.Key)
                    .Select(owner => new { Key = owner.OwnerKey.ToString("X8"), owner.Name }).ToArray()
            }).OrderBy(clip => clip.World).ThenBy(clip => clip.Key).ToArray(),
            OwnerLinkCounts = new
            {
                Owners = owners.Length,
                EventsLinks = owners.Count(owner => IsValidKey(owner.EventsKey)),
                SceneEventsLinks = owners.Count(owner => IsValidKey(owner.SceneEventsKey)),
                DecodedEvents = linkedTracks.Count(track => track.LinkName == "Events"),
                DecodedSceneEvents = linkedTracks.Count(track => track.LinkName == "SceneEvents")
            },
            OrdinaryEvents = linkedTracks.Where(track => track.LinkName == "Events")
                .Select(track => new
                {
                    Owner = owners.FirstOrDefault(owner => owner.OwnerKey == track.OwnerKey
                        && owner.WorldKey == track.WorldKey)?.Name,
                    Key = track.EventKey.ToString("X8"),
                    track.Name,
                    track.Duration
                }).OrderBy(track => track.Owner).ThenBy(track => track.Key).ToArray()
        };

        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, $"{entry.Id}-timeline-inventory.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"TIMELINE INVENTORY: {entry.Id}: {report.RootSceneEvents.Length} root SceneEvents, "
            + $"{report.OwnerLinkCounts.DecodedEvents} decoded object Events -> {reportPath}");
    }

    private static bool IsValidKey(uint key) => key is > 2 and < uint.MaxValue;
}
