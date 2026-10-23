using System.Text.Json;
using JadeView;

internal static class CinematicActorClipInventoryAudit
{
    private sealed record ClipRow(string Key, string Name, float Duration, int MatchingTracks);
    private sealed record ActorRow(string Name, string Key, int BroadMatches, int SourceLinkedMatches,
        ClipRow[] UnlinkedMatches, ClipRow[] SourceLinkedMatchesDetails);

    internal static void Write(string archivePath, string container, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] sourceBytes = archive.Read(entry);
        var document = SceneLoader.Load(sourceBytes, entry.Name, archive.PathName);
        var replay = PcSceneLoader.Read(sourceBytes, archive.PathName);
        var cinematicWorldKeys = document.Worlds
            .Where(world => world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase))
            .Select(world => world.Key).ToHashSet();
        var clips = DecodeCinematicClips(replay.Records, sourceBytes, cinematicWorldKeys);

        var actorRows = document.Worlds.Where(world => cinematicWorldKeys.Contains(world.Key))
            .SelectMany(world => world.Actors.Select(actor => AnalyzeActor(actor, world.Key,
                replay.Records, cinematicWorldKeys, clips)))
            .OrderByDescending(actor => actor.UnlinkedMatches.Length)
            .ThenBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var report = new
        {
            Container = entry.Id,
            CinematicResources = clips.Count,
            BroadActorClipAssignments = actorRows.Sum(actor => actor.BroadMatches),
            SourceLinkedActorClipAssignments = actorRows.Sum(actor => actor.SourceLinkedMatches),
            UnlinkedActorClipAssignments = actorRows.Sum(actor => actor.UnlinkedMatches.Length),
            Actors = actorRows
        };

        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, $"{entry.Id}-actor-clip-inventory.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ACTOR CLIP INVENTORY: {entry.Id}: {report.BroadActorClipAssignments} broad, "
            + $"{report.SourceLinkedActorClipAssignments} source-linked, "
            + $"{report.UnlinkedActorClipAssignments} unlinked actor-clip assignments -> {reportPath}");
    }

    private static ActorRow AnalyzeActor(SceneActor actor, uint worldKey,
        IEnumerable<PcSceneLoader.Record> records, IReadOnlySet<uint> cinematicWorldKeys,
        IReadOnlyDictionary<PcSceneLoader.Record, AnimationClip> clips)
    {
        var memberKeys = actor.Members.Select(member => member.GaoKey).ToHashSet();
        var matchingBroadClips = clips.Where(pair => pair.Key.LoadWorldKey == worldKey
                && HasActorTrack(pair.Value, actor, memberKeys))
            .Select(pair => ToRow(pair.Key, pair.Value, actor, memberKeys))
            .OrderBy(clip => clip.Key, StringComparer.Ordinal).ToArray();
        var linkedRecords = CinematicActorClipDiscovery.FindActorClips(records,
            cinematicWorldKeys, memberKeys).ToHashSet();
        var matchingSourceLinkedClips = clips.Where(pair => pair.Key.LoadWorldKey == worldKey
                && linkedRecords.Contains(pair.Key)
                && HasActorTrack(pair.Value, actor, memberKeys))
            .Select(pair => ToRow(pair.Key, pair.Value, actor, memberKeys))
            .OrderBy(clip => clip.Key, StringComparer.Ordinal).ToArray();
        var linkedKeys = matchingSourceLinkedClips.Select(clip => clip.Key).ToHashSet(StringComparer.Ordinal);
        var unlinkedClips = matchingBroadClips.Where(clip => !linkedKeys.Contains(clip.Key)).ToArray();
        return new(actor.Root.Name, actor.Root.GaoKey.ToString("X8"), matchingBroadClips.Length,
            matchingSourceLinkedClips.Length, unlinkedClips, matchingSourceLinkedClips);
    }

    private static ClipRow ToRow(PcSceneLoader.Record record, AnimationClip clip, SceneActor actor,
        IReadOnlySet<uint> memberKeys) => new(record.Key.ToString("X8"), clip.Name, clip.Duration,
        clip.Tracks.Count(track => IsActorTrack(track, actor, memberKeys)));

    private static bool HasActorTrack(AnimationClip clip, SceneActor actor, IReadOnlySet<uint> memberKeys) =>
        clip.Tracks.Any(track => IsActorTrack(track, actor, memberKeys));

    private static bool IsActorTrack(AnimationTrack track, SceneActor actor, IReadOnlySet<uint> memberKeys) =>
        track.Bone >= 0 ? track.Target == actor.Root.GaoKey : memberKeys.Contains(track.Target);

    private static Dictionary<PcSceneLoader.Record, AnimationClip> DecodeCinematicClips(
        IEnumerable<PcSceneLoader.Record> records, byte[] sourceBytes, IReadOnlySet<uint> worldKeys)
    {
        var clips = new Dictionary<PcSceneLoader.Record, AnimationClip>();
        foreach (var record in records.Where(record => record.Kind == 'i'
            && worldKeys.Contains(record.LoadWorldKey)))
        {
            try
            {
                clips.Add(record, AnimationReader.Read(sourceBytes, record.Offset, record.Size));
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException
                or OverflowException or IndexOutOfRangeException)
            {
                // Unsupported/malformed records cannot be attached as decoded actor clips.
            }
        }
        return clips;
    }
}
