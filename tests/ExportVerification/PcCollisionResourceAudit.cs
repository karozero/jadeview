using System.Text.Json;
using JadeView;

internal static class PcCollisionResourceAudit
{
    internal static void Write(string archivePath, string container, string output,
        PcCollisionWireDialect dialect = PcCollisionWireDialect.JadedPc,
        PcCollisionReadMode mode = PcCollisionReadMode.BinaryCompact)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] bytes = archive.Read(entry);
        var replay = PcSceneLoader.Read(bytes, archivePath);
        if (!replay.Complete) throw new InvalidDataException(replay.Error);

        var decoded = replay.Records.Where(record => record.Kind is 'd' or 'e' or 'f')
            .Select(record => Decode(record, bytes, dialect, mode)).ToArray();
        var owners = replay.Records.Where(record => record.Kind == 'l'
                && (record.CollisionInstance != uint.MaxValue || record.CollisionMap != uint.MaxValue))
            .Select(record => new
            {
                Key = record.Key.ToString("X8"), World = record.LoadWorldKey.ToString("X8"),
                record.Name, IdentityFlags = record.Flags.ToString("X8"),
                InstanceKey = record.CollisionInstance.ToString("X8"),
                MapKey = record.CollisionMap.ToString("X8")
            }).ToArray();
        var coverage = decoded.GroupBy(item => (item.Kind, item.Status))
            .Select(group => new { group.Key.Kind, group.Key.Status, Count = group.Count() }).ToArray();
        var document = SceneLoader.Load(bytes, entry.Name, archivePath);
        if (document.CollisionResources == null)
            throw new InvalidDataException("The typed scene loader did not construct the source collision catalog.");
        var loadedObjects = new List<PcCollisionObjectResources>();
        foreach (var sceneObject in document.Worlds.SelectMany(world => world.Objects))
            if (document.CollisionResources.TryGet(sceneObject, out var resources))
                loadedObjects.Add(resources);
        string path = Path.Combine(output, $"{container}-{dialect}-{mode}-collision-resources.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Dialect = dialect.ToString(),
            ReadMode = mode.ToString(),
            Scope = "Decoded source resources only; no collision or grounding simulation.",
            Coverage = coverage, Owners = owners, Resources = decoded,
            LoadedObjectCatalog = loadedObjects
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        foreach (var group in coverage)
            Console.WriteLine($"COLLISION COVERAGE: {group.Kind} {group.Status}: {group.Count}");
        Console.WriteLine($"LOADED COLLISION CATALOG: {loadedObjects.Count} objects");
        Console.WriteLine($"COLLISION AUDIT: {path}");
    }

    private static AuditResource Decode(PcSceneLoader.Record record, byte[] bytes,
        PcCollisionWireDialect dialect, PcCollisionReadMode mode)
    {
        var payload = bytes.AsSpan(record.Offset, record.Size);
        var result = record.Kind switch
        {
            'd' => Describe(record, PcCollisionResourceReader.ReadInstance(payload, dialect, mode)),
            'e' => Describe(record, PcCollisionResourceReader.ReadSet(payload, dialect, mode)),
            'f' => Describe(record, PcCollisionResourceReader.ReadMap(payload, dialect, mode)),
            _ => throw new ArgumentException("Expected a collision resource.", nameof(record))
        };
        return result with { PayloadHex = Convert.ToHexString(payload) };
    }

    private static AuditResource Describe<T>(PcSceneLoader.Record record,
        PcCollisionDecodeResult<T> result) where T : class => new(
            record.Key.ToString("X8"), record.LoadWorldKey.ToString("X8"),
            record.Kind.ToString(), record.Offset, record.Size, result.Status.ToString(),
            result.Diagnostic, result.Warnings, result.Value);

    private sealed record AuditResource(string Key, string World, string Kind, int Offset, int Size,
        string Status, string Diagnostic, string[] Warnings, object? Value)
    {
        public string PayloadHex { get; init; } = "";
    }
}
