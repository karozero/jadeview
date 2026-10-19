using System.Text.Json;
using JadeView;

internal static partial class ActorScriptAudit
{
    internal static void WriteModules(string archivePath, string container, uint actorKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] sceneBytes = archive.Read(entry);
        var document = SceneLoader.Load(sceneBytes, entry.Name, archive.PathName);
        var actor = document.Worlds.SelectMany(world => world.Actors).Single(actor => actor.Root.GaoKey == actorKey);
        var records = ReadRecords(sceneBytes, archive.PathName, container);
        var fixedEntry = archive.Entries.Single(entry => entry.Id == "FF0084DF");
        if (entry.Id != fixedEntry.Id)
            records.InsertRange(0, ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id));
        var root = records.Last(record => record.Key == actorKey && record.Kind == 'l');
        var shapeRecords = records.Where(record => record.Key == root.Shape).ToArray();
        var report = new
        {
            Container = container, ActorKey = actorKey.ToString("X8"), actor.Root.Name,
            ShapeKey = root.Shape.ToString("X8"),
            SelectorZeroComponent = actor.FindModule(0)?.GaoKey.ToString("X8"),
            Shapes = shapeRecords.Select(record => new { record.Container, record.World, record.Offset,
                Bytes = Convert.ToHexString(record.Bytes) }),
            Components = actor.Bones.Select((component, index) => new
            {
                Index = index, Key = component?.GaoKey.ToString("X8"), component?.Name,
                Geometry = component?.RenderGeometryKey.ToString("X8"),
                Sources = actor.SourceMeshes.GetValueOrDefault(index)?.Select(source => new
                    { Key = source.GaoKey.ToString("X8"), source.Name, Geometry = source.GeometryKey.ToString("X8") })
            }),
            Limits = "Parsed source shape bytes and component order only; shape column semantics and runtime module resolution are not inferred."
        };
        string filename = actorKey.ToString("X8") + "-modules.json";
        File.WriteAllText(Path.Combine(output, filename), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MODULE AUDIT: {actor.Bones.Count} components, {shapeRecords.Length} source shape occurrences");
    }
}
