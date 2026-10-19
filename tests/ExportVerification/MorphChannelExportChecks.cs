using JadeView;

internal static class MorphChannelExportChecks
{
    internal static FaceMorphPreview VerifySally(SceneActor actor, SceneObject head,
        WorldData world, ResourceResolver resolver, string output)
    {
        var preview = head.MorphPreview!;
        preview.SetChannel(0, .75f, 1);
        preview.SetChannel(1, .5f, 1);
        Check(preview.GetWeight(1) == .75f && preview.GetWeight(2) == .5f,
            "Sally channel reference slots did not resolve to eyelid targets");
        var document = new SceneDocument { ContainerName = "FF0057FD" };
        document.Worlds.Add(world);
        var project = new ModProject();
        project.CaptureMorphPreviews(document);
        string projectPath = Path.Combine(output, "sally-channel-project.bgemod");
        project.Save(projectPath);
        var loaded = ModProject.Load(projectPath);
        Check(loaded.BindMorphPreviews(document, resolver).Count == 0, "Sally channel project failed to bind");
        preview = head.MorphPreview!;
        Check(preview.ChannelsActive && preview.ChannelCount == 6 && preview.GetWeight(1) == .75f
            && preview.GetWeight(2) == .5f, "Sally channel controls/pose failed to restore");
        string path = Path.Combine(output, "sally-channel-pose.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
        using (var glb = new GlbReader(path))
        {
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray()
                .First(mesh => mesh.TryGetProperty("weights", out var weights) && weights.GetArrayLength() == 55);
            Check(mesh.GetProperty("weights")[1].GetSingle() == .75f && mesh.GetProperty("weights")[2].GetSingle() == .5f,
                "Sally channel pose was lost on GLB export");
            var metadata = mesh.GetProperty("extras").GetProperty("pcMorph");
            Check(metadata.GetProperty("previewChannels").GetArrayLength() == 6, "Sally channel metadata was lost");
            SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [0, actor.Clip!.Duration / 2]);
        }
        preview.SetChannel(0, .75f, 0); preview.SetChannel(1, .5f, 0);
        preview.Timeline.Record(0, preview.CopyWeights());
        preview.SetChannel(0, .75f, 1); preview.SetChannel(1, .5f, 1);
        preview.Timeline.Record(.1f, preview.CopyWeights());
        preview.SetChannel(0, .75f, 0); preview.SetChannel(1, .5f, 0);
        preview.Timeline.Record(.2f, preview.CopyWeights());
        SceneExporter.Export(Path.Combine(output, "sally-channel-animation.glb"), world, resolver,
            new() { Animations = true, Textures = true }, actor.Root);
        Check(preview.ChannelsActive, "Animation export changed the channel control mode");
        preview.Reset(); preview.Timeline.Clear();
        actor.Evaluate(0);
        Console.WriteLine("PASS: real Sally channel pose, project reload, GLB skin/metadata and channel-authored keys");
        return preview;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
