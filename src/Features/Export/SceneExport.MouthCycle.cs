namespace JadeView;

public sealed partial class SceneExporter
{
    private int experimentalMouthClips;
    private const string MouthCycleProvenance = "manually triggered recovered PC mouth cycle; simulated at 60 Hz from current preview state; no original dialogue timing";

    private static void ValidateMouthCycleOptions(SceneExportOptions options)
    {
        if (!options.ExperimentalMouthCycle) return;
        bool supportedDuration = float.IsFinite(options.MouthCycleDuration) && options.MouthCycleDuration > 0
            && options.MouthCycleDuration <= FaceMorphMouthCycleSampler.MaximumDuration;
        if (!options.Animations || !supportedDuration)
            throw new ArgumentException("Experimental mouth export requires animation/skinning and a duration of 0..10 seconds (exclusive zero).", nameof(options));
    }

    private void CaptureMouthCycle(SceneActor actor, SceneObject sceneObject, int meshNode)
    {
        if (!options.ExperimentalMouthCycle || sceneObject.MorphPreview is not { MouthCyclePlaying: true } preview) return;
        var definition = preview.MouthCycleDefinition;
        if (definition == null || actor.FindModule(definition.ModuleSelector) != sceneObject) return;
        var keys = preview.SampleMouthCycle(options.MouthCycleDuration);
        morphTimelines.Add(new(actor, meshNode, $"Experimental mouth cycle · {actor.Root.Name} · {sceneObject.Name}",
            keys, MouthCycleProvenance, "STEP"));
        experimentalMouthClips++;
        warnings.Add("Experimental mouth cycle is a sampled simulation, not an original facial/dialogue animation.");
    }
}
