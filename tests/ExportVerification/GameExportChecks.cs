using JadeView;
using static SkinExportChecks;

internal static class GameExportChecks
{
    internal static void VerifyGame(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.First(entry => entry.Id == "FF0057FD");
        byte[] sceneBytes = archive.Read(entry);
        var document = SceneLoader.Load(sceneBytes, entry.Name, archive.PathName);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive); resolver.LoadSceneContainer(archive, entry);
        var world = document.Worlds.First(world => world.Objects.Any(sceneObject => sceneObject.Name == "OBJ_Gyro.gao"));
        var result = SceneExporter.Export(Path.Combine(output, "hangar.glb"), world, resolver, new() { Textures = true, Actors = false });
        Check(result.Meshes > 0 && result.Textures > 0, "Hangar export has no meshes or textures");
        Console.WriteLine($"PASS: game hangar export: {result.Meshes} meshes, {result.Textures} textures, {result.Warnings.Length} warnings");
        var actors = document.Worlds.SelectMany(world => world.Actors).Where(actor => actor.Members.Any(member =>
            resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var geometry) && geometry.Skin.Length > 0))
            .GroupBy(actor => actor.Root.GaoKey).Select(group => group.First())
            .OrderBy(actor => actor.Root.Name.Contains("Sally", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(2).ToArray();
        Check(actors.Length > 0, "No real skinned actor was found in the hangar container");
        foreach (var actor in actors)
        {
            ActorScriptAudit.Write(archive, sceneBytes, actor, output);
            VerifyActor(actor, document.Worlds.First(world => world.Actors.Contains(actor)), resolver, output);
        }
    }

    private static void VerifyActor(SceneActor actor, WorldData world, ResourceResolver resolver, string output)
    {
        AnimationEventAudit.Write(actor, output);
        foreach (var member in actor.Members.Concat(actor.SourceMeshes.Values.SelectMany(models => models)))
        foreach (var modifier in member.Modifiers.Where(modifier => modifier.Type == 4))
        {
            if (!resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var geometry)) continue;
            var morph = FaceMorphReader.Read(modifier.Data, geometry.Positions.Length);
            if (morph.Groups.Length == 55)
                File.WriteAllText(Path.Combine(output, $"morph-geometry-{geometry.Key:X8}.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { geometry.Positions, geometry.Parts },
                        new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
            Console.WriteLine($"MORPH: {member.Name} GAO {member.GaoKey:X8}, geometry {geometry.Key:X8}: {geometry.Positions.Length} vertices, {morph.Groups.Length} groups, {morph.Channels.Length} channels, {morph.Groups.Sum(group => group.VertexIndices.Length)} sparse entries");
            File.WriteAllText(Path.Combine(output, $"morph-{member.GaoKey:X8}-{geometry.Key:X8}.json"),
                System.Text.Json.JsonSerializer.Serialize(morph, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        var materialAudit = actor.Members.Where(member => resolver.Geometries.ContainsKey(member.RenderGeometryKey))
            .Select(member => new { member.Name, Geometry = member.RenderGeometryKey.ToString("X8"),
                Material = member.MaterialKey.ToString("X8"), Materials = resolver.Materials.TryGetValue(member.MaterialKey, out var material)
                    ? new[] { material }.Concat(material.SubMaterialKeys.Select(key => resolver.Materials.GetValueOrDefault(key)).OfType<ResolvedMaterial>())
                        .Select(source => new { source.Id, Layers = JadeMaterialLayer.Read(source.SourceBytes) }).ToArray() : [] });
        File.WriteAllText(Path.Combine(output, SceneExporter.SafeFileName(actor.Root.Name) + "-materials.json"),
            System.Text.Json.JsonSerializer.Serialize(materialAudit, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var clips = actor.Clips.Where(IsShortDecodedClip).Take(3).ToArray();
        Check(clips.Length > 0, "No short decoded animation found");
        foreach (var clip in clips)
            VerifyClip(actor, clip, world, resolver, output);
        if (actor.Root.Name == "_PJ_Sally.gao")
            MorphPreviewChecks.VerifySally(actor, world, resolver, output);
        if (actor.Root.Name == "_PJ_Militaire.gao")
            MilitaryAppearanceChecks.Verify(actor, world, resolver, output);
    }

    private static bool IsShortDecodedClip(AnimationClip clip)
    {
        clip.EnsureLoaded();
        return clip.Error == null && clip.Duration > 0 && clip.Duration < 10;
    }

    private static void VerifyClip(SceneActor actor, AnimationClip clip, WorldData world,
        ResourceResolver resolver, string output)
    {
        actor.RootMotion = false;
        actor.SelectClip(clip);
        string actorPath = Path.Combine(output, "actor-" + SceneExporter.SafeFileName(actor.Root.Name) + "-" + clip.Name + ".glb");
        var result = SceneExporter.Export(actorPath, world, resolver,
            new() { Textures = true, Animations = true }, actor.Root);
        Check(result.Meshes > 0 && result.Animations == 1, "Real actor export is missing animation or geometry");
        using (var glb = new GlbReader(actorPath))
        {
            MorphExportChecks.VerifyActor(glb, actor, resolver);
            if (actor.Root.Name == "_PJ_Sally.gao") MaterialExportChecks.VerifySallyMaterials(glb);
            var sampler = glb.Root.GetProperty("animations")[0].GetProperty("samplers")[0];
            var times = glb.Accessor(sampler.GetProperty("input").GetInt32()).Select(value => value[0]).ToArray();
            actor.RootMotion |= clip.SceneMotion;
            VerifyActorMeshes(glb, actor, resolver, [times[0], times[times.Length / 2], times[^1]]);
        }
        Console.WriteLine($"PASS: game actor '{actor.Root.Name}', clip '{clip.Name}': {result.Meshes} meshes, {result.Animations} animation");
    }

    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
