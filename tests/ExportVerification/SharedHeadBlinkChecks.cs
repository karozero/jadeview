using JadeView;

internal static class SharedHeadBlinkChecks
{
    internal static void Verify(SceneActor actor, SceneObject head, WorldData world,
        ResourceResolver resolver, string output, string prefix)
    {
        var preview = FaceMorphPreview.Get(head, resolver.Geometries[head.RenderGeometryKey])!;
        AutomaticMouthBindingChecks.Verify(actor, head, preview, world, resolver, output, prefix);
        Check(preview.SupportsPcBlink && !preview.SupportsSallyBlink && preview.BlinkGroups.SequenceEqual(new[] { 10 }),
            "Shared head blink binding is not qualified independently of Sally");
        var unknown = FaceMorphPreview.Get(new SceneObject { Modifiers = head.Modifiers },
            new Geometry { Key = 0xDEADBEEF, Positions = preview.Geometry.Positions })!;
        Check(!unknown.SupportsPcBlink, "Blink qualification leaked to an unrelated geometry key");
        try { unknown.StartBlink(); throw new Exception("Unknown actor blink was accepted"); }
        catch (InvalidOperationException) { }
        var original = preview.CopyWeights();
        byte[] source = (byte[])preview.Modifier.Data.Clone();
        preview.SetWeight(10, .2f);
        preview.SetWeight(3, .3f);
        preview.StartBlink();
        actor.Evaluate(MathF.PI / (2 * FaceMorphBlinkCurve.PhaseSpeed));
        Check(MathF.Abs(preview.GetWeight(10) - 1) < .00001f && preview.GetWeight(3) == .3f,
            "Blink peak changed unrelated targets or did not close its target");
        string path = Path.Combine(output, prefix + "-pc-blink-peak.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
        Check(preview.BlinkPlaying, "Export stopped the transient blink");
        using (var glb = new GlbReader(path))
        {
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray().Single(mesh =>
                mesh.TryGetProperty("weights", out var weights) && weights.GetArrayLength() == preview.Data!.Groups.Length);
            Check(MathF.Abs(mesh.GetProperty("weights")[10].GetSingle() - 1) < .00001f,
                "Export lost the current blink pose");
        }
        preview.StopBlink();
        Check(preview.GetWeight(10) == .2f && preview.GetWeight(3) == .3f, "Stop did not restore the baseline");
        preview.SetChannel(0, .25f, 2);
        var channelBaseline = preview.CopyWeights();
        preview.StartBlink(); preview.Advance(.03f); preview.StopBlink();
        Check(preview.ChannelsActive && preview.CopyWeights().SequenceEqual(channelBaseline),
            "Stopping blink did not restore the explicit channel pose");
        preview.StartBlink(); preview.Advance(1);
        Check(!preview.BlinkPlaying && preview.ChannelsActive, "Blink completion did not restore the channel mode");
        preview.StartBlink(); preview.SetWeight(10, .4f);
        Check(!preview.BlinkPlaying && !preview.ChannelsActive, "Direct editing did not take over from blink");
        preview.StartBlink(); preview.Reset();
        Check(!preview.BlinkPlaying && preview.CopyWeights().All(weight => weight == 0), "Reset did not clear blink");
        Check(preview.Modifier.Data.SequenceEqual(source), "Blink changed native source bytes");
        for (int group = 0; group < original.Length; group++) preview.SetWeight(group, original[group]);
        actor.Evaluate(0);
        Console.WriteLine($"PASS: {prefix} native-envelope blink, peak GLB pose, baseline/channel restore and source isolation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
