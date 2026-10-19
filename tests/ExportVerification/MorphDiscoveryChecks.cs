using JadeView;

internal static class MorphDiscoveryChecks
{
    internal static void Verify()
    {
        var data = new FaceMorphData([], [new(0, [], []),
            new(1, [0, 0], [new(1, 0, 0), new(2, 0, 0)]),
            new(2, [0, 0], [new(1, 0, 0), new(-1, 0, 0)])],
            [new(0, 0, [1, 1, uint.MaxValue])]);
        var poses = FaceMorphExpressionCatalog.Discover(data);
        Check(poses.Length == 1 && poses[0].Group == 1 && poses[0].Channels.SequenceEqual(new[] { 0 }),
            "Discovery included empty/cancelled deltas or duplicated references");
        Check(data.Groups[1].AccumulatedDeltas()[0].X == 3, "Repeated sparse indices were not accumulated");
        Console.WriteLine("PASS: automatic morph discovery, channel provenance, empty/cancelled targets and repeated sparse indices");
    }

    internal static void VerifyPeyj(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id == "FF00791B");
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        var actor = document.Worlds.SelectMany(world => world.Actors)
            .First(actor => actor.Root.Name == "_PJ_Peyj_le_petit_cochon.gao");
        var world = document.Worlds.First(world => world.Actors.Contains(actor));
        var head = actor.Members.First(member => member.Name == "B_peyj_Tete.gao");
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive); resolver.LoadSceneContainer(archive, entry);
        var models = actor.SourceAppearances(head);
        int highDetail = Enumerable.Range(0, models.Count).Single(index => models[index].RenderGeometryKey == 0x8E001060);
        int CountPoses(SceneObject member) => resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var sourceGeometry)
            ? FaceMorphPreview.Get(member, sourceGeometry)?.Expressions.Count ?? 0 : 0;
        Check(actor.ResolveMorphAppearance(head, CountPoses), "Empty Peyj head was not automatically replaced");
        Check(head.RenderGeometryKey == models[highDetail].RenderGeometryKey, "Automatic selection did not choose Peyj HD head");
        var geometry = resolver.Geometries[head.RenderGeometryKey];
        var preview = FaceMorphPreview.Get(head, geometry)!;
        Check(preview.Expressions.Count == 13 && preview.Data!.Groups.Length == 22 && preview.ChannelCount == 7,
            "Peyj HD morph discovery does not match the source archive");
        Check(!preview.SupportsSallyBlink, "Sally native bindings leaked into Peyj");
        SharedHeadBlinkChecks.Verify(actor, head, world, resolver, output, "peyj");
        var expression = preview.Expressions[0];
        byte[] original = (byte[])preview.Modifier.Data.Clone();
        preview.PreviewExpression(expression.Group);
        actor.SelectClip(actor.Clips.FirstOrDefault());
        Check(head.RenderGeometryKey == geometry.Key, "Clip selection replaced the explicitly selected morph appearance");
        string path = Path.Combine(output, "peyj-discovered-expression.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
        using (var glb = new GlbReader(path))
        {
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray().First(mesh =>
                mesh.TryGetProperty("weights", out var weights) && weights.GetArrayLength() == 22);
            Check(mesh.GetProperty("weights")[expression.Group].GetSingle() == 1, "Discovered Peyj expression was lost on export");
            SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [0]);
        }
        Check(preview.Modifier.Data.SequenceEqual(original), "Peyj expression changed native source bytes");
        var project = new ModProject(); project.CaptureMorphPreviews(document);
        string projectPath = Path.Combine(output, "peyj-morph-project.bgemod");
        project.Save(projectPath);
        actor.RestoreClipAppearance(head);
        uint clipGeometry = head.RenderGeometryKey;
        Check(!actor.ResolveMorphAppearance(head, CountPoses) && head.RenderGeometryKey == clipGeometry,
            "Automatic discovery overrode the user's return to clip appearance");
        var loaded = ModProject.Load(projectPath);
        Check(loaded.BindMorphPreviews(document, resolver).Count == 0, "Peyj appearance profile did not bind");
        actor.PreviewAppearance(head, highDetail);
        preview = FaceMorphPreview.Get(head, geometry)!;
        Check(preview.GetWeight(expression.Group) == 1, "Peyj morph pose did not restore after returning to HD appearance");
        Console.WriteLine("PASS: real Peyj HD automatic discovery (13/22 targets), appearance override, project restore and GLB skin/pose export");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
