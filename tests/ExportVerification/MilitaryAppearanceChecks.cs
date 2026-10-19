using JadeView;

internal static class MilitaryAppearanceChecks
{
    internal static void Verify(SceneActor actor, WorldData world, ResourceResolver resolver, string output)
    {
        var head = actor.Members.Single(member => member.GaoKey == 0x20000EE6);
        var hair = actor.Members.Single(member => member.GaoKey == 0xA900532C);
        actor.SelectClip(null);
        Check(hair.PreviewHidden, "Ordinary military shape should hide Double H's separate hair");
        int CountPoses(SceneObject member) => resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var geometry)
            ? FaceMorphPreview.Get(member, geometry)?.Expressions.Count ?? 0 : 0;
        Check(actor.ResolveMorphAppearance(head, CountPoses), "Military HD morph head was not discovered");
        CheckHair(head, hair);
        MilitaryMaterialChecks.WriteSource(head, hair, resolver, output);
        SharedHeadBlinkChecks.Verify(actor, head, world, resolver, output, "military");
        foreach (var clip in actor.Clips.Take(3))
        {
            actor.SelectClip(clip);
            CheckHair(head, hair);
        }
        string path = Path.Combine(output, "military-hd-hair.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = true, Animations = true }, actor.Root);
        CheckHair(head, hair);
        using (var glb = new GlbReader(path))
        {
            Check(glb.Root.GetProperty("nodes").EnumerateArray().Any(node =>
                node.TryGetProperty("name", out var name) && name.GetString() == hair.Name && node.TryGetProperty("mesh", out _)),
                "HD hair mesh is missing from the GLB");
            SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [0]);
            MilitaryMaterialChecks.Verify(glb, actor, head, resolver, output);
        }
        var models = actor.SourceAppearances(head);
        int ordinary = Enumerable.Range(0, models.Count).Single(index => models[index].RenderGeometryKey == 0xA9003416);
        actor.PreviewAppearance(head, ordinary);
        Check(hair.PreviewHidden, "Switching back to ordinary head retained HD hair");
        int highDetail = Enumerable.Range(0, models.Count).Single(index => models[index].RenderGeometryKey == 0x8F0066B2);
        actor.PreviewAppearance(head, highDetail);
        CheckHair(head, hair);
        actor.RestoreClipAppearance(head);
        Check(hair.PreviewHidden, "Returning to the clip appearance retained HD hair");
        Console.WriteLine("PASS: military HD morph head includes hair, survives clips/export and restores native accessory visibility");
    }

    private static void CheckHair(SceneObject head, SceneObject hair) => Check(
        head.RenderGeometryKey == 0x8F0066B2 && !hair.PreviewHidden && hair.RenderGeometryKey == 0xA90052F4,
        "HD head did not activate its separate hair/accessory source");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
