using System.Numerics;
using JadeView;

internal static class MorphPreviewChecks
{
    internal static void VerifySally(SceneActor actor, WorldData world, ResourceResolver resolver, string output)
    {
        var head = actor.FindModule(0) ?? throw new InvalidOperationException("Source module selector 0 did not resolve the head");
        var geometry = resolver.Geometries[head.RenderGeometryKey];
        var preview = FaceMorphPreview.Get(head, geometry)!;
        Check(preview.Data?.Groups.Length == 55 && preview.Data.Channels.Length == 6,
            "Source-selected head does not have the expected morph layout");
        preview.SetWeight(1, 1); preview.SetWeight(2, 1);
        actor.Evaluate(0);
        string path = Path.Combine(output, "sally-manual-blink.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
        Check(ReferenceEquals(head.MorphPreview, preview) && preview.GetWeight(1) == 1 && preview.GetWeight(2) == 1,
            "Sally export changed active manual morph weights");
        using (var glb = new GlbReader(path))
        {
            var sampler = glb.Root.GetProperty("animations")[0].GetProperty("samplers")[0];
            var times = glb.Accessor(sampler.GetProperty("input").GetInt32()).Select(value => value[0]).ToArray();
            SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [times[0], times[times.Length / 2], times[^1]]);
        }
        preview.Reset();
        actor.Evaluate(0);
        Console.WriteLine("PASS: Sally manual eyelid weights match GLB deformation throughout skeletal motion");
        Check(preview.SourceIssues.Any(issue => issue.Group == 37), "Known raw Sally target was not identified");
        preview.SetWeight(37, 1);
        actor.Evaluate(0);
        var result = SceneExporter.Export(Path.Combine(output, "sally-raw-morph37.glb"), world, resolver,
            new() { Animations = true, Textures = true }, actor.Root);
        Check(result.Warnings.Any(warning => warning.Contains("PC_Morph_37") && warning.Contains("Known eyelid artifacts")),
            "Export silently accepted a known problematic raw target");
        Check(ReferenceEquals(head.MorphPreview, preview) && preview.GetWeight(37) == 1,
            "Diagnostic changed the source target or its selected weight");
        preview.Reset();
        actor.Evaluate(0);
        Console.WriteLine("PASS: known raw morph 37 is diagnosed without deleting, clamping or replacing source data");
        preview = MorphChannelExportChecks.VerifySally(actor, head, world, resolver, output);
        preview = MorphBlinkChecks.VerifySally(actor, head, world, resolver, output);
        SallyExpressionChecks.VerifySally(actor, head, world, resolver, output);
        MorphMouthPreviewChecks.Verify(actor, head, world, resolver, output);
        MorphTimelineChecks.VerifySally(actor, head, preview, world, resolver, output);
    }

    internal static void Verify(SceneActor actor, SceneObject source, Geometry geometry,
        WorldData world, ResourceResolver resolver, string output)
    {
        var original = source.Modifiers;
        source.Modifiers = [new SceneModifier(4, Payload(geometry.Positions.Length))];
        var preview = FaceMorphPreview.Get(source, geometry)!;
        Check(ReferenceEquals(preview.Positions(), geometry.Positions), "Neutral preview changed source positions");
        Check(!preview.SupportsSallyBlink, "Unqualified geometry was treated as a Sally blink rig");
        preview.SetWeight(1, .5f);
        var deformed = preview.Positions();
        Check(Vector3.Distance(deformed[0], geometry.Positions[0] + new Vector3(.1f, -.05f, .15f)) < .00001f,
            "Manual weight did not scale the sparse delta");
        actor.Evaluate(0);
        string path = Path.Combine(output, "manual-morph-weights.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = false }, actor.Root);
        Check(ReferenceEquals(source.MorphPreview, preview) && preview.GetWeight(1) == .5f,
            "Export did not restore manual preview state");
        using (var glb = new GlbReader(path))
            SkinExportChecks.VerifySkin(glb, actor, source, geometry, [0, .09f]);
        VerifyChannels(actor, source, geometry, world, resolver, preview, output);
        var replacement = new SceneModifier(4, Payload(geometry.Positions.Length));
        source.Modifiers = [replacement];
        var changed = FaceMorphPreview.Get(source, geometry)!;
        Check(!ReferenceEquals(preview, changed) && changed.GetWeight(1) == 0, "Weights leaked between source models");
        preview.Reset();
        Check(ReferenceEquals(preview.Positions(), geometry.Positions), "Reset did not restore neutral geometry");
        source.Modifiers = original;
        source.MorphPreview = null;
        actor.Evaluate(0);
        Console.WriteLine("PASS: manual morph preview, deformation before skinning, export-state restoration and model isolation");
    }

    internal static byte[] Payload(int vertexCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0u); writer.Write(0u); writer.Write(vertexCount);
        writer.Write(2); writer.Write(1);
        writer.Write(0);
        writer.Write(2); writer.Write(0); writer.Write(0);
        writer.Write(.1f); writer.Write(-.05f); writer.Write(.15f);
        writer.Write(.1f); writer.Write(-.05f); writer.Write(.15f);
        writer.Write(2); writer.Write(1f); writer.Write(0f); writer.Write(1u); writer.Write(1u);
        writer.Flush();
        return stream.ToArray();
    }

    private static void VerifyChannels(SceneActor actor, SceneObject source, Geometry geometry,
        WorldData world, ResourceResolver resolver, FaceMorphPreview preview, string output)
    {
        byte[] originalBytes = (byte[])preview.Modifier.Data.Clone();
        preview.SetChannel(0, .75f, .5f);
        Check(preview.ChannelsActive && preview.GetWeight(1) == .375f, "Channel controls did not evaluate before skinning");
        var saved = preview.CopyChannelState()!;
        saved[0] = new(0, 0);
        Check(preview.GetChannel(0).Weight == .75f, "Channel snapshots alias live state");
        string path = Path.Combine(output, "channel-morph-weights.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = false }, actor.Root);
        Check(preview.ChannelsActive && preview.GetWeight(1) == .375f, "Export changed active channel controls");
        using (var glb = new GlbReader(path))
        {
            SkinExportChecks.VerifySkin(glb, actor, source, geometry, [0, .09f]);
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray().First(mesh => mesh.TryGetProperty("weights", out _));
            Check(mesh.GetProperty("weights")[1].GetSingle() == .375f, "Channel pose was lost in GLB weights");
            var channel = mesh.GetProperty("extras").GetProperty("pcMorph").GetProperty("previewChannels")[0];
            Check(channel.GetProperty("weight").GetSingle() == .75f && channel.GetProperty("progression").GetSingle() == .5f,
                "GLB lost channel authoring metadata");
        }
        preview.Timeline.Record(0, preview.CopyWeights());
        preview.Timeline.Record(.1f, new float[2]);
        preview.Timeline.Playing = true;
        preview.Advance(.05f);
        Check(!preview.ChannelsActive && preview.GetWeight(1) == .1875f, "Timeline and channel controls competed for the pose");
        preview.Timeline.Clear();
        preview.SetChannel(0, 1, 1);
        preview.SetWeight(1, .2f);
        Check(!preview.ChannelsActive && preview.CopyChannelState() == null, "Direct target edit left channels active");
        try { preview.SetChannel(0, float.NaN, 1); throw new Exception("Invalid channel state accepted"); }
        catch (ArgumentException) { }
        Check(preview.GetWeight(1) == .2f && !preview.ChannelsActive, "Invalid channel edit changed the current pose");
        Check(preview.Modifier.Data.SequenceEqual(originalBytes), "Channel preview mutated the native BF payload");
        preview.Reset();
        Check(!preview.ChannelsActive && preview.GetChannel(0).Progression == 0 && preview.GetWeight(1) == 0,
            "Reset did not clear channel controls and target weights");
        Console.WriteLine("PASS: channel preview, GLB pose/metadata, source isolation, manual/timeline takeover and reset");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
