using System.Numerics;

namespace JadeView;

// Editor-only direct target weights. This does not interpret native channel state or timing.
public sealed class FaceMorphPreview
{
    public Geometry Geometry { get; }
    public SceneModifier Modifier { get; }
    public FaceMorphData? Data { get; }
    public string? Error { get; }
    public FaceMorphSourceIssue[] SourceIssues { get; }
    public FaceMorphTimeline Timeline { get; }
    internal bool HasUnmatchedProjectState { get; set; }
    private readonly float[] weights;

    private FaceMorphPreview(Geometry geometry, SceneModifier modifier)
    {
        Geometry = geometry;
        Modifier = modifier;
        try { Data = FaceMorphReader.Read(modifier.Data, geometry.Positions.Length); }
        catch (InvalidDataException error) { Error = error.Message; }
        weights = new float[Data?.Groups.Length ?? 0];
        Timeline = new(weights.Length);
        SourceIssues = Data != null ? FaceMorphDiagnostics.SourceIssues(geometry, Data) : [];
    }

    public static FaceMorphPreview? Get(SceneObject sceneObject, Geometry geometry)
    {
        SceneModifier? source = null;
        foreach (var modifier in sceneObject.Modifiers)
        {
            if (modifier.Type != 4) continue;
            if (source != null) return sceneObject.MorphPreview = null;
            source = modifier;
        }
        if (source == null) return sceneObject.MorphPreview = null;
        var cached = sceneObject.MorphPreview;
        if (cached?.Geometry == geometry && cached.Modifier == source) return cached;
        var preview = new FaceMorphPreview(geometry, source);
        sceneObject.MorphPreview = preview;
        sceneObject.RestoreMorphPreview?.Invoke(preview);
        return preview;
    }

    public float GetWeight(int group) => weights[group];
    public float[] CopyWeights() => (float[])weights.Clone();
    public void SetWeight(int group, float weight)
    {
        if (!float.IsFinite(weight)) throw new ArgumentException("Morph weight must be finite.", nameof(weight));
        Timeline.Playing = false;
        weights[group] = weight;
    }
    public void Reset() { Array.Clear(weights); Timeline.Playing = false; }
    public void Seek(float time)
    {
        Timeline.Playing = false;
        ApplySample(Timeline.Seek(time));
    }
    public void Advance(float delta) => ApplySample(Timeline.Advance(delta));
    private void ApplySample(float[]? sample)
    {
        if (sample != null) sample.CopyTo(weights, 0);
    }

    public Vector3[] Positions()
    {
        if (Data == null || !weights.Any(weight => weight != 0)) return Geometry.Positions;
        var positions = (Vector3[])Geometry.Positions.Clone();
        foreach (var group in Data.Groups)
        {
            float weight = weights[group.Index];
            if (weight == 0) continue;
            for (int entry = 0; entry < group.VertexIndices.Length; entry++)
                positions[group.VertexIndices[entry]] += group.Deltas[entry] * weight;
        }
        return positions;
    }
}
