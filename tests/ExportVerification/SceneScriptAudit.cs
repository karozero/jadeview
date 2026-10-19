using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static partial class ActorScriptAudit
{
    internal static void WriteScene(string archivePath, string container, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var records = ReadRecords(archive.Read(entry), archive.PathName, entry.Id);
        var fixedEntry = archive.Entries.FirstOrDefault(entry => entry.Id == "FF0084DF");
        if (fixedEntry != null && fixedEntry.Id != entry.Id)
            records.InsertRange(0, ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id));
        var roots = records.Where(record => record.Brain.HasValue).Select(record => new
        {
            record.Container, Key = record.Key.ToString("X8"), record.Name,
            World = record.World.ToString("X8"), Brain = record.Brain!.Value.ToString("X8")
        }).ToArray();
        var resources = records.Where(record => record.Kind is 'q' or 's' or 't' or 'r').ToArray();
        var report = new
        {
            Archive = archive.PathName, Container = entry.Id, Roots = roots,
            Limits = "Parsed resources in one scene plus fixed container; no global key deduplication, controller execution or inferred defaults.",
            Records = resources.Select(record => new
            {
                record.Container, Key = record.Key.ToString("X8"), Kind = record.Kind.ToString(), record.Offset,
                Sha256 = Convert.ToHexString(SHA256.HashData(record.Bytes)),
                World = record.World.ToString("X8"), record.Name,
                References = record.References.Select(key => key.ToString("X8")).ToArray(),
                Bytes = Convert.ToHexString(record.Bytes)
            }).ToArray()
        };
        File.WriteAllText(Path.Combine(output, entry.Id + "-scene-scripts.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCENE SCRIPT AUDIT: {entry.Id}: {roots.Length} brain owners, {resources.Length} resources");
    }
}
