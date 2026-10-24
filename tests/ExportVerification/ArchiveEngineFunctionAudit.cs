using System.Text.Json;
using JadeView;

internal static class ArchiveEngineFunctionAudit
{
    private sealed record Occurrence(string Container, string ContainerName, string World, string? WorldName,
        string Clip, string ClipName, float Time, string Target, string? TargetName,
        string SerializedContextKey, string[] TrackTargets, string[] Arguments, string Description,
        object[] DirectOwners);

    internal static void Write(string archivePath, string functionName, string output)
    {
        var archive = new Archive(archivePath);
        var occurrences = new List<Occurrence>();
        var failures = new List<object>();
        Entry[] containers = archive.Entries
            .Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .ToArray();

        foreach (Entry container in containers)
        {
            byte[] source = archive.Read(container);
            PcSceneLoader.Result replay = PcSceneLoader.Read(source, archive.PathName);
            if (!replay.Complete)
            {
                failures.Add(new { Container = container.Id, replay.Error });
                continue;
            }

            var worldsByKey = replay.Worlds.GroupBy(world => world.Key)
                .ToDictionary(group => group.Key, group => group.First().Name);
            var recordsByWorldAndKey = replay.Records
                .GroupBy(record => (record.LoadWorldKey, record.Key))
                .ToDictionary(group => group.Key, group => group.First());

            foreach (PcSceneLoader.Record clipRecord in replay.Records.Where(record => record.Kind == 'i'))
            {
                byte[] payload = source.AsSpan(clipRecord.Offset, clipRecord.Size).ToArray();
                AnimationClip clip;
                try
                {
                    clip = AnimationReader.Read(payload, 0, payload.Length);
                    if (clip.Error != null)
                    {
                        failures.Add(new { Container = container.Id, Clip = clipRecord.Key.ToString("X8"), clip.Error });
                        continue;
                    }
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException
                    or OverflowException or IndexOutOfRangeException)
                {
                    failures.Add(new { Container = container.Id, Clip = clipRecord.Key.ToString("X8"), Error = error.Message });
                    continue;
                }

                PcSceneLoader.Record[] eventOwners = replay.Records
                    .Where(record => record.LoadWorldKey == clipRecord.LoadWorldKey
                        && record.Kind == 'l'
                        && (record.Events == clipRecord.Key || record.SceneEvents == clipRecord.Key))
                    .ToArray();
                clip.UsesEventTrackExecutionContext = eventOwners.Length > 0;
                clip.CinematicOwnerKeys = eventOwners.Select(record => record.Key).Distinct().ToArray();
                object[] directOwners = replay.Records
                    .Where(record => record.LoadWorldKey == clipRecord.LoadWorldKey
                        && (record.Events == clipRecord.Key || record.SceneEvents == clipRecord.Key
                            || record.References.Any(reference => reference.Key == clipRecord.Key)))
                    .Select(record => (object)new
                    {
                        Key = record.Key.ToString("X8"),
                        record.Name,
                        Kind = record.Kind.ToString(),
                        Events = record.Events.ToString("X8"),
                        SceneEvents = record.SceneEvents.ToString("X8")
                    }).ToArray();

                foreach ((float time, CinematicLiteralCall call) in CinematicLiteralCall.ReadTimeline(clip))
                {
                    string resolvedName = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                    if (!resolvedName.Equals(functionName, StringComparison.OrdinalIgnoreCase)) continue;

                    string[] trackTargets = clip.Tracks.Where(track => track.Bone < 0)
                        .Select(track => track.Target).Distinct()
                        .Select(key => key.ToString("X8")).ToArray();
                    string? targetName = recordsByWorldAndKey.TryGetValue((clipRecord.LoadWorldKey, call.Target),
                        out PcSceneLoader.Record? targetRecord) ? targetRecord.Name : null;

                    occurrences.Add(new(container.Id, container.Name, clipRecord.LoadWorldKey.ToString("X8"),
                        worldsByKey.GetValueOrDefault(clipRecord.LoadWorldKey), clipRecord.Key.ToString("X8"),
                        clipRecord.Name, time, call.Target.ToString("X8"), targetName,
                        call.SerializedContextKey.ToString("X8"), trackTargets,
                        call.Arguments.Select(argument => $"0x{argument:X8}").ToArray(),
                        CinematicEngineFunctionCatalog.Describe(call), directOwners));
                }
            }
        }

        Directory.CreateDirectory(output);
        string safeFunctionName = new(functionName.Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        string reportPath = Path.Combine(output, $"archive-engine-function-{safeFunctionName}.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            Archive = archive.PathName,
            Function = functionName,
            ContainersScanned = containers.Length,
            OccurrenceCount = occurrences.Count,
            Occurrences = occurrences.OrderBy(item => item.Container, StringComparer.Ordinal)
                .ThenBy(item => item.Clip, StringComparer.Ordinal).ThenBy(item => item.Time).ToArray(),
            DecodeFailures = failures,
            Limits = "Structural archive call inventory only. Direct owner references do not prove EVE activation, callback execution, branch success, or runtime call delivery."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ARCHIVE ENGINE FUNCTION AUDIT: {functionName}: {occurrences.Count} calls in {containers.Length} containers");
        Console.WriteLine($"REPORT: {reportPath}");
    }
}
