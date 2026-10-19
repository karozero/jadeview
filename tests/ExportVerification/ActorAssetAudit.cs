using System.Text;
using JadeView;

internal static class ActorAssetAudit
{
    internal static void Run(string archivePath, string actorName, string output)
    {
        var archive = new Archive(archivePath);
        foreach (var entry in archive.Entries.Where(entry => entry.Id.StartsWith("FF0") && entry.Id != "FF0084DF"))
        {
            byte[] bytes = archive.Read(entry);
            if (!Encoding.Latin1.GetString(bytes).Contains(actorName, StringComparison.OrdinalIgnoreCase)) continue;
            var document = SceneLoader.Load(bytes, entry.Name, archive.PathName);
            foreach (var world in document.Worlds)
            foreach (var actor in world.Actors.Where(actor => actor.Root.Name.Equals(actorName, StringComparison.OrdinalIgnoreCase)))
            {
                var resolver = new ResourceResolver();
                resolver.LoadFixContainer(archive);
                resolver.LoadSceneContainer(archive, entry);
                string path = Path.Combine(output, SceneExporter.SafeFileName(actorName) + "-audit.glb");
                actor.SelectClip(actor.Clips.FirstOrDefault());
                var result = SceneExporter.Export(path, world, resolver,
                    new() { Animations = true, Textures = true }, actor.Root);
                Console.WriteLine($"ACTOR {entry.Id} {world.Name} {actor.Root.GaoKey:X8}: {result.Meshes} meshes, {result.Textures} textures, {result.Animations} animations");
                foreach (var warning in result.Warnings) Console.WriteLine($"WARNING {warning}");
                foreach (var member in actor.Members.Where(member => !member.PreviewHidden))
                    Console.WriteLine($"MEMBER {member.Name} geometry={member.RenderGeometryKey:X8} material={member.MaterialKey:X8}");
                using var glb = new GlbReader(path);
                SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [0, .1f]);
                int inverseMasks = InverseAlphaChecks.Verify(glb, resolver);
                if (actor.Root.GaoKey == 0x9E006A5E && inverseMasks != 3)
                    throw new Exception("The known Spectre instance must export three corrected inverse alpha masks");
                Console.WriteLine("PASS: exported actor skin matches the editor at two animation times");
                return;
            }
        }
        throw new InvalidOperationException($"Actor {actorName} was not found in parsed FF0 scenes");
    }
}
