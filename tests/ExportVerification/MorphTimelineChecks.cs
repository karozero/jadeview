using JadeView;

internal static class MorphTimelineChecks
{
    internal static void Verify()
    {
        var timeline = new FaceMorphTimeline(2) { Loop = false };
        timeline.Record(.2f, [0, 1]);
        timeline.Record(0, [0, 0]);
        float[] pose = [1, .5f];
        timeline.Record(.1f, pose);
        pose[0] = 0;
        Check(timeline.Keys.Count == 3 && timeline.Keys[1].Weights[0] == 1, "Keys were not ordered or copied");
        var middle = timeline.Sample(.05f)!;
        Check(MathF.Abs(middle[0] - .5f) < .00001f && MathF.Abs(middle[1] - .25f) < .00001f, "Morph interpolation differs");
        timeline.Seek(.15f); timeline.Playing = true;
        var final = timeline.Advance(.1f)!;
        Check(timeline.Time == .2f && !timeline.Playing && final.SequenceEqual(new float[] { 0, 1 }), "Non-looping morph endpoint did not hold");
        timeline.Loop = true; timeline.Playing = true;
        timeline.Advance(.05f);
        Check(MathF.Abs(timeline.Time - .05f) < .00001f, "Morph loop time is incorrect");
        timeline.Record(.1f, [.8f, .2f]);
        Check(timeline.Keys.Count == 3 && timeline.Keys[1].Weights[0] == .8f, "Recording did not replace the existing key");
        timeline.Seek(.1f);
        Check(timeline.RemoveCurrentKey() && timeline.Keys.Count == 2, "Morph key removal failed");
        timeline.Clear();
        Check(timeline.Keys.Count == 0 && timeline.Time == 0 && !timeline.Playing, "Clearing morph animation failed");
        Reject(() => timeline.Record(float.NaN, [0, 0]));
        Reject(() => timeline.Record(0, [0]));
        Reject(() => timeline.Record(0, [float.PositiveInfinity, 0]));
        Console.WriteLine("PASS: authored morph key ordering, interpolation, endpoints, looping, editing and validation");
    }

    internal static void VerifySally(SceneActor actor, SceneObject head, FaceMorphPreview preview,
        WorldData world, ResourceResolver resolver, string output)
    {
        preview.Reset(); preview.Timeline.Record(0, preview.CopyWeights());
        preview.SetWeight(1, 1); preview.SetWeight(2, 1);
        preview.Timeline.Record(.1f, preview.CopyWeights());
        preview.Reset(); preview.Timeline.Record(.2f, preview.CopyWeights());
        preview.Seek(0); preview.Timeline.Playing = true;
        actor.Evaluate(.05f);
        Check(preview.GetWeight(1) == .5f && preview.GetWeight(2) == .5f, "Actor playback did not advance facial weights");
        string path = Path.Combine(output, "sally-authored-morph.glb");
        var result = SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
        Check(preview.Timeline.Time == .05f && preview.Timeline.Playing && preview.GetWeight(1) == .5f,
            "Export altered the current morph clock/weights");
        using var glb = new GlbReader(path);
        var animation = glb.Root.GetProperty("animations").EnumerateArray()
            .Single(animation => animation.GetProperty("name").GetString()!.StartsWith("Manual morphs"));
        Check(!animation.GetProperty("extras").GetProperty("originalGameTimeline").GetBoolean(), "Manual clip provenance was lost");
        var sampler = animation.GetProperty("samplers")[0];
        var times = glb.Accessor(sampler.GetProperty("input").GetInt32());
        var values = glb.Accessor(sampler.GetProperty("output").GetInt32());
        int targetCount = preview.Data!.Groups.Length;
        Check(times.Length == 3 && values.Length == 3 * targetCount, "glTF morph SCALAR packing is incorrect");
        Check(values[targetCount + 1][0] == 1 && values[targetCount + 2][0] == 1 && values[1][0] == 0,
            "Authored eyelid key values differ");
        var skeletalOnly = SceneExporter.Export(Path.Combine(output, "sally-without-morph-animation.glb"), world, resolver,
            new() { Animations = true, Textures = false, MorphAnimations = false }, actor.Root);
        Check(skeletalOnly.Animations == 1 && preview.Timeline.Keys.Count == 3,
            "Disabling morph clip export lost keys or still exported the facial clip");
        preview = MorphProjectChecks.RoundTripSally(actor, head, world, resolver, output);
        preview.Timeline.Clear(); preview.Reset(); actor.Evaluate(0);
        Console.WriteLine($"PASS: Sally authored facial clip exports {times.Length} keys, {targetCount} targets; {result.Animations} total animations");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid morph timeline input was accepted");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
