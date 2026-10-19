using System.Text.Json;
using JadeView;

internal static class SceneAnimationEventAudit
{
    internal static void Write(string archivePath, string container, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        string folder = Path.Combine(output, entry.Id + "-scene-events");
        Directory.CreateDirectory(folder);
        var owners = new List<object>();
        foreach (var world in document.Worlds)
        foreach (var actor in world.Actors)
        {
            string relative = $"{world.Key:X8}-{actor.Root.GaoKey:X8}";
            string actorFolder = Path.Combine(folder, relative);
            Directory.CreateDirectory(actorFolder);
            AnimationEventAudit.Write(actor, actorFolder);
            owners.Add(new { World = world.Key.ToString("X8"), world.Name,
                GaoKey = actor.Root.GaoKey.ToString("X8"), Actor = actor.Root.Name,
                actor.Clips.Count, Directory = relative });
        }
        File.WriteAllText(Path.Combine(folder, "index.json"), JsonSerializer.Serialize(new
        {
            Container = entry.Id, Owners = owners,
            Limits = "Parsed actors/clips in one container; no archive-wide completeness or event execution claimed."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCENE EVENT AUDIT: {entry.Id}: {owners.Count} actor instances");
    }
}
