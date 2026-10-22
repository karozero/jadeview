using System.Text.Json;
using JadeView;

internal static class ActionKitResourceAudit
{
    internal static void Write(string archivePath, string containerId, uint ownerKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate => candidate.Id.Equals(containerId,
            StringComparison.OrdinalIgnoreCase));
        byte[] containerBytes = archive.Read(entry);
        var replay = PcSceneLoader.Read(containerBytes, archivePath);
        if (!replay.Complete)
            throw new InvalidDataException(replay.Error);
        var document = SceneLoader.Load(containerBytes, entry.Name, archivePath);
        var fixedEntry = archive.Entries.FirstOrDefault(candidate => candidate.Id == "FF0084DF");
        PcSceneLoader.Result? fixedReplay = fixedEntry == null
            ? null : PcSceneLoader.Read(archive.Read(fixedEntry), archivePath);
        var indexedRecords = replay.Records.Concat(fixedReplay?.Records ?? []).ToArray();
        var recordBytes = replay.Records.ToDictionary(record => record, _ => containerBytes);
        if (fixedEntry != null && fixedReplay != null)
        {
            byte[] fixedBytes = archive.Read(fixedEntry);
            foreach (var record in fixedReplay.Records)
                recordBytes[record] = fixedBytes;
        }

        var candidates = replay.Records.Where(record => record.Key == ownerKey)
            .OrderBy(record => record.LoadWorldKey).ThenBy(record => record.Kind).ToArray();
        var report = new
        {
            Container = entry.Id,
            Owner = ownerKey.ToString("X8"),
            DecodedActionKitTables = document.CinematicActionKitsByOwner
                .Where(pair => pair.Key.OwnerKey == ownerKey)
                .Select(pair => new
                {
                    World = pair.Key.WorldKey.ToString("X8"),
                    SlotCount = pair.Value.Count,
                    Slots = pair.Value.Keys.Order().ToArray()
                }),
            OwnerRecords = candidates.Select(record => new
            {
                Key = record.Key.ToString("X8"),
                Kind = record.Kind.ToString(),
                World = record.LoadWorldKey.ToString("X8"),
                record.Name,
                Flags = record.Flags.ToString("X8"),
                ActionKitKey = record.Skeleton.ToString("X8"),
                ActionTable = replay.Records.Where(candidate => candidate.Key == record.Skeleton)
                    .Select(table => new
                    {
                        Key = table.Key.ToString("X8"),
                        Kind = table.Kind.ToString(),
                        World = table.LoadWorldKey.ToString("X8"),
                        table.Offset,
                        table.Size,
                        Entries = table.References.Select(reference => new
                        {
                            Key = reference.Key.ToString("X8"),
                            Kind = reference.Kind.ToString(),
                            LoadedRecords = indexedRecords.Where(candidate => candidate.Key == reference.Key)
                                .Select(candidate => new
                                {
                                    Kind = candidate.Kind.ToString(),
                                    World = candidate.LoadWorldKey.ToString("X8"),
                                    candidate.Offset,
                                    candidate.Size,
                                    ContentHash = candidate.Offset >= 0 && candidate.Size >= 0
                                        && candidate.Offset <= recordBytes[candidate].Length - candidate.Size
                                        ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                                            recordBytes[candidate].AsSpan(candidate.Offset, candidate.Size)))
                                        : "outside source buffer"
                                })
                        })
                    })
            }),
            Limits = "Shows serialized object-to-action-table references and records loaded from this container. It does not inspect runtime mutations or resources absent from the loaded scope."
        };
        string path = Path.Combine(output, $"{containerId}-{ownerKey:X8}-action-kit.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ACTION KIT RESOURCE AUDIT: {candidates.Length} owner records -> {path}");
    }
}
