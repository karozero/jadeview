namespace JadeView;

public sealed partial class SceneExporter
{
    private readonly List<ExportMorphTimeline> morphTimelines = [];

    private void CaptureMorphTimeline(SceneActor actor, SceneObject sceneObject, int meshNode)
    {
        int meshIndex = (int)nodes[meshNode]["mesh"];
        if (!meshes[meshIndex].ContainsKey("weights")) return;
        CaptureMouthCycle(actor, sceneObject, meshNode);
        if (options.MorphAnimations && options.CinematicMorphTimelines?.TryGetValue(sceneObject, out var cinematicKeys) == true
            && cinematicKeys.Length > 1)
            morphTimelines.Add(new(actor, meshNode, $"Cinematic facial events · {actor.Root.Name} · {sceneObject.Name}",
                cinematicKeys, "Source-time morph-channel events and supported mouth controls sampled at 60 Hz; dialogue audio and phoneme timing are not reconstructed", "LINEAR", actor.Clip));
        if (!options.MorphAnimations || sceneObject.MorphPreview?.Timeline is not { Keys.Count: > 0 } timeline) return;
        foreach (var issue in sceneObject.MorphPreview!.SourceIssues)
            if (timeline.Keys.Any(key => key.Weights[issue.Group] != 0))
                warnings.Add($"'{sceneObject.Name}' manual morph animation uses PC_Morph_{issue.Group:D2}: {issue.Message}");
        morphTimelines.Add(new(actor, meshNode, $"Manual morphs · {actor.Root.Name} · {sceneObject.Name}", timeline.CopyKeys()));
    }

    private void WriteMorphAnimations(SceneActor actor)
    {
        foreach (var timeline in morphTimelines.Where(timeline => timeline.Actor == actor))
        {
            var times = timeline.Keys.Select(key => key.Time).ToArray();
            int targetCount = timeline.Keys[0].Weights.Length;
            bool valid = targetCount > 0 && timeline.Keys.All(key => key.Weights.Length == targetCount);
            if (!valid) throw new InvalidDataException("Morph timeline target count mismatch.");
            int input = Floats(times, 1, "SCALAR", true);
            int output = Floats(timeline.Keys.SelectMany(key => key.Weights).ToArray(), 1, "SCALAR");
            AddExportAnimation(D("name", timeline.Name,
                "samplers", new[] { D("input", input, "output", output, "interpolation", timeline.Interpolation) },
                "channels", new[] { D("sampler", 0, "target", D("node", timeline.Node, "path", "weights")) },
                "extras", D("provenance", timeline.Provenance, "originalGameTimeline", false)), timeline.SourceClip);
        }
    }
}
