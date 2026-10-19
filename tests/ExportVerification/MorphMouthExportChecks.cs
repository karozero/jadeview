using JadeView;

internal static class MorphMouthExportChecks
{
    internal static void Verify(SceneActor actor, SceneObject head, WorldData world, ResourceResolver resolver, string output)
    {
        var preview = head.MorphPreview!;
        var pose = preview.CopyWeights();
        int keyCount = preview.Timeline.Keys.Count;
        var samples = preview.SampleMouthCycle(.217f);
        Check(samples[0].Time == 0 && samples[^1].Time == .217f, "Off-grid sample endpoint was lost");
        Check(samples.Zip(samples.Skip(1)).All(pair => pair.First.Time < pair.Second.Time), "Sample times are not strictly increasing");
        Check(samples.Any(sample => !sample.Weights.SequenceEqual(pose)), "Sampled mouth clip has no motion");
        Check(preview.CopyWeights().SequenceEqual(pose) && preview.Timeline.Keys.Count == keyCount,
            "Sampling modified the preview or authored keys");
        var oneFrame = preview.SampleMouthCycle(1f / 60);
        Check(oneFrame.Length == 2 && oneFrame[1].Time == 1f / 60, "Float grid endpoint created duplicate keys");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, 11f })
        {
            try { preview.SampleMouthCycle(invalid); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Unbounded mouth sampling was accepted");
        }

        string path = Path.Combine(output, "sally-experimental-mouth-cycle.glb");
        SceneExporter.Export(path, world, resolver,
            new() { Animations = true, Textures = true, MorphAnimations = false,
                ExperimentalMouthCycle = true, MouthCycleDuration = .217f }, actor.Root);
        using var glb = new GlbReader(path);
        var animation = glb.Root.GetProperty("animations").EnumerateArray().Single(animation =>
            animation.GetProperty("name").GetString()!.StartsWith("Experimental mouth cycle"));
        var sampler = animation.GetProperty("samplers")[0];
        Check(sampler.GetProperty("interpolation").GetString() == "STEP", "Discrete simulation was exported as a continuous approximation");
        Check(!animation.GetProperty("extras").GetProperty("originalGameTimeline").GetBoolean(), "Simulated clip was labeled as original game animation");
        var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
        var weights = glb.Accessor(sampler.GetProperty("output").GetInt32());
        Check(times.Length == samples.Length && weights.Length == samples.Length * pose.Length, "Morph sampler dimensions do not match the sampled state");
        for (int frame = 0; frame < samples.Length; frame++)
        {
            Check(times[frame][0] == samples[frame].Time, "GLB mouth key time changed");
            for (int group = 0; group < pose.Length; group++)
                Check(weights[frame * pose.Length + group][0] == samples[frame].Weights[group], "GLB mouth weight changed");
        }
        Check(preview.MouthCyclePlaying && preview.CopyWeights().SequenceEqual(pose)
            && preview.Timeline.Keys.Count == keyCount, "Export changed live mouth state or recorded authoring keys");
        preview.Advance(1f / 60);
        Check(preview.CopyWeights().SequenceEqual(samples[1].Weights), "Export advanced the hidden controller state");
        Console.WriteLine("PASS: sampled experimental GLB mouth clip, STEP keys, off-grid endpoint, provenance and unchanged live controller");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
