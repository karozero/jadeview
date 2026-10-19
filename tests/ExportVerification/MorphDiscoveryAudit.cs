using System.Text;
using System.Text.Json;
using JadeView;

internal static class MorphDiscoveryAudit
{
    internal static void Run(string path, string output, string? container = null)
    {
        var archive = new Archive(path);
        var found = new HashSet<uint>();
        foreach (var entry in archive.Entries.Where(entry => entry.Id.StartsWith("FF0") && entry.Id != "FF0084DF"
            && (container == null || entry.Id == container)))
        {
            byte[] bytes = archive.Read(entry);
            if (!Encoding.Latin1.GetString(bytes).Contains("peyj", StringComparison.OrdinalIgnoreCase)) continue;
            var document = SceneLoader.Load(bytes, entry.Name, archive.PathName);
            var actors = document.Worlds.SelectMany(world => world.Actors)
                .Where(actor => actor.Root.Name.Contains("peyj", StringComparison.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine($"PEYJ SEARCH {entry.Id}: {actors.Length} matching actors");
            if (actors.Length == 0) continue;
            var resolver = new ResourceResolver();
            resolver.LoadFixContainer(archive); resolver.LoadSceneContainer(archive, entry);
            foreach (var actor in actors.Where(actor => found.Add(actor.Root.GaoKey)))
            {
                ActorScriptAudit.Write(archive, bytes, actor, output, entry.Id);
                foreach (var member in actor.Members.Concat(actor.SourceMeshes.Values.SelectMany(models => models)))
                {
                    if (!resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var geometry)) continue;
                    foreach (var modifier in member.Modifiers.Where(modifier => modifier.Type == 4))
                    {
                        FaceMorphData data;
                        try { data = FaceMorphReader.Read(modifier.Data, geometry.Positions.Length); }
                        catch (InvalidDataException error)
                        {
                            File.WriteAllBytes(Path.Combine(output, $"peyj-raw-{member.GaoKey:X8}-{geometry.Key:X8}-{geometry.Positions.Length}.bin"), modifier.Data);
                            Console.WriteLine($"PEYJ MORPH FAILED {member.Name}: {error.Message}");
                            continue;
                        }
                        string filename = $"peyj-morph-{member.GaoKey:X8}-{geometry.Key:X8}.json";
                        File.WriteAllText(Path.Combine(output, filename), JsonSerializer.Serialize(new
                        {
                            Container = entry.Id, Actor = actor.Root.Name, ActorKey = actor.Root.GaoKey.ToString("X8"),
                            member.Name, GaoKey = member.GaoKey.ToString("X8"), Geometry = geometry.Key.ToString("X8"),
                            Vertices = geometry.Positions.Length, Data = data
                        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                        Console.WriteLine($"PEYJ MORPH: {actor.Root.Name} / {member.Name}: {data.Groups.Length} groups, {data.Channels.Length} channels ({filename})");
                    }
                }
            }
            if (actors.Any(actor => actor.Root.Name.Equals("_PJ_Peyj.gao", StringComparison.OrdinalIgnoreCase))) break;
        }
        Console.WriteLine($"Peyj actor variants found: {found.Count}. Scope: {(container ?? "FF0 containers containing Peyj text")}; name-filtered discovery, not proof of archive-wide morph completeness.");
    }
}
