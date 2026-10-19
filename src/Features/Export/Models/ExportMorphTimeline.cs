namespace JadeView;

internal sealed record ExportMorphTimeline(SceneActor Actor, int Node, string Name, FaceMorphKeyframe[] Keys,
    string Provenance = "user-authored morph preview", string Interpolation = "LINEAR");
