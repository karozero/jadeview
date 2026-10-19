using JadeView;

internal static class MorphBlinkChecks
{
    internal static void Verify()
    {
        float[] baseline = [.3f, .2f, .4f, -.1f];
        var keys = FaceMorphBlinkCurve.Bake(1, baseline, 1, 2);
        Check(keys[0].Time == 1 && keys[^1].Time == 1 + FaceMorphBlinkCurve.Duration,
            "Blink key interval does not match the PC phase speed/limit");
        Check(keys[0].Weights.SequenceEqual(baseline) && keys[^1].Weights.SequenceEqual(baseline),
            "Blink does not start/end at the captured baseline");
        Check(keys.Any(key => key.Weights[1] == 1 && key.Weights[2] == 1), "Blink bake omitted the exact peak");
        Check(keys.All(key => key.Weights[0] == .3f && key.Weights[3] == -.1f), "Blink changed unrelated morphs");
        keys[0].Weights[1] = 99;
        Check(baseline[1] == .2f && keys[^1].Weights[1] == .2f, "Blink keys alias source/other keys");
        foreach (float time in new[] { -1f, float.NaN, float.PositiveInfinity, 1e20f })
        {
            try { FaceMorphBlinkCurve.Bake(time, baseline, 1, 2); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid/unrepresentable blink time was accepted");
        }
        Console.WriteLine("PASS: PC blink envelope bake, endpoint cleanup, peak, independent keys and unrelated morph preservation");
    }

    internal static FaceMorphPreview VerifySally(SceneActor actor, SceneObject head,
        WorldData world, ResourceResolver resolver, string output)
    {
        var preview = head.MorphPreview!;
        Check(preview.SupportsSallyBlink, "Real Sally head did not qualify for the PC blink envelope");
        preview.SetWeight(1, .2f); preview.SetWeight(2, .4f); preview.SetWeight(10, .3f);
        preview.StartBlink(); actor.Evaluate(.05f);
        Check(preview.BlinkPlaying && MathF.Abs(preview.GetWeight(1) - (.2f + .8f * MathF.Sin(.75f))) < .000001f
            && preview.GetWeight(10) == .3f, "Blink clock/envelope failed while the actor was evaluated");
        actor.Evaluate(1);
        Check(!preview.BlinkPlaying && preview.GetWeight(1) == .2f && preview.GetWeight(2) == .4f,
            "Completed blink did not restore its baseline");
        preview.SetChannel(0, .25f, 1);
        preview.StartBlink(); preview.Advance(.03f); preview.StopBlink();
        Check(preview.ChannelsActive && preview.GetChannel(0).Weight == .25f && preview.GetWeight(1) == .25f,
            "Stopping blink did not restore channel authoring state");
        preview.StartBlink(); preview.SetWeight(1, .2f);
        Check(!preview.BlinkPlaying && !preview.ChannelsActive, "Manual editing did not stop blink playback");
        preview.SetWeight(2, .4f); preview.SetWeight(10, .3f);
        preview.BakeBlink();
        int keyCount = preview.Timeline.Keys.Count;
        try { preview.BakeBlink(); throw new Exception("Blink overwrite was accepted"); }
        catch (InvalidOperationException) { }
        Check(preview.Timeline.Keys.Count == keyCount, "Rejected blink bake changed existing keys");
        preview.StartBlink(); preview.Timeline.Playing = true; preview.Advance(.02f);
        Check(!preview.BlinkPlaying, "Timeline playback did not take over from the transient blink");
        preview.Seek(0);
        var document = new SceneDocument { ContainerName = "FF0057FD" };
        document.Worlds.Add(world);
        var project = new ModProject(); project.CaptureMorphPreviews(document);
        string projectPath = Path.Combine(output, "sally-pc-blink.bgemod");
        project.Save(projectPath);
        Check(ModProject.Load(projectPath).BindMorphPreviews(document, resolver).Count == 0,
            "Baked blink failed to reload from a project");
        preview = head.MorphPreview!;
        Check(!preview.BlinkPlaying && preview.Timeline.Keys.Count == keyCount && !preview.Timeline.Playing,
            "Reloaded blink lost its keys or resumed transient playback");
        SceneExporter.Export(Path.Combine(output, "sally-pc-blink.glb"), world, resolver,
            new() { Animations = true, Textures = true }, actor.Root);
        preview.StartBlink(); preview.Reset();
        Check(!preview.BlinkPlaying && preview.CopyWeights().All(weight => weight == 0), "Reset did not stop blink playback");
        preview.Timeline.Clear(); actor.Evaluate(0);
        Console.WriteLine("PASS: real Sally blink playback, mode takeover, overwrite rejection, project reload and GLB export");
        return preview;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
