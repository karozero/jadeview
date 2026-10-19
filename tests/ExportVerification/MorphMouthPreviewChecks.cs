using JadeView;

internal static class MorphMouthPreviewChecks
{
    internal static void Verify(SceneActor actor, SceneObject head, WorldData world, ResourceResolver resolver, string output)
    {
        var preview = head.MorphPreview!;
        byte[] source = (byte[])preview.Modifier.Data.Clone();
        preview.Reset();
        preview.SetWeight(10, .23f);
        float[] baseline = preview.CopyWeights();
        preview.StartMouthCycle();
        for (int frame = 0; frame < 10; frame++) actor.Evaluate(1f / 60);
        float[] pose = preview.CopyWeights();
        Check(preview.MouthCyclePlaying && !pose.SequenceEqual(baseline), "Mouth preview did not animate");
        Check(preview.GetWeight(10) == .23f, "Mouth preview changed an unrelated group");
        Check(preview.Positions().All(position => float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z)),
            "Mouth preview produced invalid geometry");
        actor.Evaluate(0);
        Check(preview.CopyWeights().SequenceEqual(pose), "Zero-time pose evaluation advanced transient mouth state");

        var document = new SceneDocument { ContainerName = "FF0057FD" };
        document.Worlds.Add(world);
        var project = new ModProject();
        project.CaptureMorphPreview(document, actor, head, preview);
        Check(project.MorphPreviews.Single().Weights.SequenceEqual(baseline), "Project captured a transient mouth frame");
        SceneExporter.Export(Path.Combine(output, "sally-mouth-cycle-pose.glb"), world, resolver,
            new() { Animations = true, Textures = true }, actor.Root);
        using (var glb = new GlbReader(Path.Combine(output, "sally-mouth-cycle-pose.glb")))
        {
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray().First(mesh =>
                mesh.TryGetProperty("weights", out var weights) && weights.GetArrayLength() == 55);
            Check(mesh.GetProperty("weights").EnumerateArray().Select(value => value.GetSingle()).SequenceEqual(pose),
                "GLB did not retain the current mouth pose");
        }
        Check(ReferenceEquals(head.MorphPreview, preview) && preview.MouthCyclePlaying && preview.CopyWeights().SequenceEqual(pose),
            "GLB export disturbed mouth playback");
        MorphMouthExportChecks.Verify(actor, head, world, resolver, output);
        preview.StopMouthCycle();
        Check(preview.CopyWeights().SequenceEqual(baseline), "Stop did not restore the baseline");

        preview.SetChannel(0, .4f, 1);
        var channels = preview.CopyChannelState()!;
        float[] channelPose = preview.CopyWeights();
        preview.StartMouthCycle(); preview.Advance(.1f);
        project.CaptureMorphPreview(document, actor, head, preview);
        Check(project.MorphPreviews.Single().ChannelState!.SequenceEqual(channels), "Project lost baseline channel controls during mouth playback");
        preview.StopMouthCycle();
        Check(preview.ChannelsActive && preview.CopyChannelState()!.SequenceEqual(channels)
            && preview.CopyWeights().SequenceEqual(channelPose), "Stop did not restore channel mode");

        preview.StartMouthCycle(); preview.Advance(.1f); preview.SetWeight(10, .5f);
        Check(!preview.MouthCyclePlaying && preview.GetWeight(10) == .5f, "Manual edit did not take over mouth playback");
        preview.StartMouthCycle(); preview.StartBlink();
        Check(!preview.MouthCyclePlaying && preview.BlinkPlaying, "Blink did not take over mouth playback");
        preview.StartMouthCycle();
        Check(!preview.BlinkPlaying, "Mouth playback did not take over blink");
        preview.Reset();
        Check(!preview.MouthCyclePlaying && preview.CopyWeights().All(weight => weight == 0), "Reset did not clear transient mouth state");
        Check(preview.Modifier.Data.SequenceEqual(source), "Mouth preview mutated source BF bytes");
        actor.Evaluate(0);
        Console.WriteLine("PASS: real source-bound mouth preview, takeover/reset, project baseline and GLB pose/export-state isolation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
