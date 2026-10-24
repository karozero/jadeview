using System.Text.Json;
using JadeView;

internal static class PcWorldListResourceAudit
{
    internal static void Write(string archivePath, string keyText, string output)
    {
        uint key = Convert.ToUInt32(keyText, 16);
        var archive = new Archive(archivePath);
        GameScriptWorldListResource resource = GameScriptWorldListArchiveReader.Read(archive, key);
        var report = new
        {
            SchemaVersion = 1,
            Archive = Path.GetFileName(archive.PathName),
            Key = key.ToString("X8"),
            resource.EntryName,
            resource.Status,
            resource.Diagnostic,
            OrderedRoots = resource.Descriptor?.OrderedWorldRootKeys
                .Select(root => root.ToString("X8"))
                .ToArray()
        };
        string path = Path.Combine(output, $"pc-world-list-{key:X8}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{resource.Status}: WOL {key:X8} · {resource.EntryName}");
        if (resource.Diagnostic is { Length: > 0 }) Console.WriteLine(resource.Diagnostic);
        if (resource.Descriptor is { } descriptor)
            Console.WriteLine($"Serialized roots: {string.Join(", ", descriptor.OrderedWorldRootKeys.Select(root => root.ToString("X8")))}");
        Console.WriteLine($"Audit: {path}");
    }
}
