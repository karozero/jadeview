using System.Text.Json;

namespace JadeView;
internal sealed partial class PcSceneLoader
{
    internal static void Audit(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var rows = archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000).Select(entry =>
        {
            try
            {
                var record = Read(archive.Read(entry), archivePath);
                return new
                {
                    entry.Id,
                    record.Complete,
                    record.Error,
                    record.End,
                    Records = record.Records.Count
                };
            }
            catch (Exception error)when (error is InvalidDataException or IOException)
            {
                return new
                {
                    entry.Id,
                    Complete = false,
                    Error = (string? )error.Message,
                    End = 0,
                    Records = 0
                };
            }
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void Verify(string archivePath, string sceneId, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.First(entry => entry.Id.Equals(sceneId, StringComparison.OrdinalIgnoreCase));
        var replay = Read(archive.Read(entry), archivePath, worldListKey: entry.Key);
        var worldList = new
        {
            OuterListKey = entry.Key.ToString("X8"),
            RootKeys = replay.OrderedWorldRootKeys.Select(key => key.ToString("X8")).ToArray(),
            Meaning = "Raw WOL roots in serialized order. Placeholder keys are retained; this is not the runtime universe slot table."
        };
        var summary = new
        {
            replay.Complete, replay.Error, replay.End, replay.SpeSkipped, replay.CachedSkipped,
            WorldList = worldList,
            Worlds = replay.Worlds.Select(world => new
            {
                Key = world.Key.ToString("X8"), world.Name,
                Members = world.WorldMembers.Select(record => new
                {
                    Key = record.Key.ToString("X8"), record.Offset, record.Name
                })
            }),
            Records = replay.Records.Select(record => new
            {
                Key = record.Key.ToString("X8"), Kind = record.Kind.ToString(),
                record.Offset, record.Size, record.Name,
                Geometry = record.Geometry.ToString("X8"), Material = record.Material.ToString("X8"),
                References = record.References.Select(reference => new
                {
                    Key = reference.Key.ToString("X8"), Kind = reference.Kind.ToString()
                })
            })
        };
        File.WriteAllText(output, JsonSerializer.Serialize(summary,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"FF0 {sceneId}: {replay.Complete}, {replay.Records.Count} records, {replay.End}; {replay.Error}");
    }
}
