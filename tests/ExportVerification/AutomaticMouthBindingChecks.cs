using JadeView;
using System.Reflection;

internal static class AutomaticMouthBindingChecks
{
    internal static void Verify(SceneActor actor, SceneObject head, FaceMorphPreview preview,
        WorldData world, ResourceResolver resolver, string output, string prefix)
    {
        var definition = preview.MouthCycleDefinition ?? throw new InvalidOperationException("Known shared controller was not automatically bound");
        Check(actor.FindModule(0) == head && definition.Completion == PcMorphTransitionCompletion.ProgressionThreshold,
            "Source controller bound the wrong module or completion rule");
        Check(definition.FormSequence[0] == (prefix == "military" ? 6 : 4), "Instance friend override selected the wrong initializer branch");
        var baseline = preview.CopyWeights();
        preview.StartMouthCycle();
        var samples = preview.SampleMouthCycle(.5f);
        Check(samples.Any(sample => !sample.Weights.SequenceEqual(baseline)), "Automatically bound mouth has no evaluated motion");
        string path = Path.Combine(output, prefix + "-automatic-mouth.glb");
        SceneExporter.Export(path, world, resolver, new() { Animations = true, ExperimentalMouthCycle = true,
            MouthCycleDuration = .5f, MorphAnimations = false, Textures = false }, actor.Root);
        using (var glb = new GlbReader(path))
            Check(glb.Root.GetProperty("animations").EnumerateArray().Any(animation =>
                animation.GetProperty("name").GetString()!.StartsWith("Experimental mouth cycle")), "Automatic mouth clip was not exported");
        preview.StopMouthCycle();
        Check(preview.CopyWeights().SequenceEqual(baseline), "Automatic mouth test changed the baseline");

        var genericHead = new SceneObject { Name = "Unrelated component", Modifiers = head.Modifiers };
        var controllerProperty = typeof(SceneActor).GetProperty("MouthController", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var genericActor = new SceneActor();
        controllerProperty.SetValue(genericActor, controllerProperty.GetValue(actor));
        genericActor.Bones.Add(genericHead);
        genericHead.Actor = genericActor;
        var genericGeometry = new Geometry { Key = 0xDEADBEEF, Positions = preview.Geometry.Positions };
        var genericPreview = FaceMorphPreview.Get(genericHead, genericGeometry)!;
        Check(genericPreview.MouthCycleDefinition != null, "Controller discovery still depends on a known geometry key or character name");
        controllerProperty.SetValue(genericActor, null);
        FaceMorphPreview.Get(genericHead, genericGeometry);
        Check(genericPreview.MouthCycleDefinition == null, "Unknown controller inherited a guessed mouth sequence");
        Console.WriteLine($"PASS: {prefix} automatic source-controller mouth binding, initializer branch, GLB export and name/key independence");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
