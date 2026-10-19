namespace JadeView;

// Editor authoring state: direct weights or explicit PC channel controls, without inferred timing.
public sealed partial class FaceMorphPreview
{
    public Geometry Geometry { get; }
    public SceneModifier Modifier { get; }
    public FaceMorphData? Data { get; }
    public string? Error { get; }
    public FaceMorphSourceIssue[] SourceIssues { get; }
    public IReadOnlyList<MorphExpressionPose> Expressions { get; }
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
        ResetChannelState();
        SourceIssues = Data != null ? FaceMorphDiagnostics.SourceIssues(geometry, Data) : [];
        Expressions = Data != null ? FaceMorphExpressionCatalog.Discover(Data) : [];
        sourceProfile = PcMorphProfiles.Match(geometry.Key, Data);
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
        if (cached?.Geometry == geometry && cached.Modifier == source)
        {
            cached.BindController(sceneObject);
            return cached;
        }
        var preview = new FaceMorphPreview(geometry, source);
        sceneObject.MorphPreview = preview;
        preview.BindController(sceneObject);
        sceneObject.RestoreMorphPreview?.Invoke(preview);
        return preview;
    }

    public float GetWeight(int group) => weights[group];
    public float[] CopyWeights() => (float[])weights.Clone();
    public void SetWeight(int group, float weight)
    {
        if (!float.IsFinite(weight)) throw new ArgumentException("Morph weight must be finite.", nameof(weight));
        StopMouthCycle();
        StopBlink();
        Timeline.Playing = false;
        ChannelsActive = false;
        weights[group] = weight;
    }
    public void Reset()
    {
        StopMouthCycle();
        StopBlink();
        Array.Clear(weights);
        Timeline.Playing = false;
        ResetChannelState();
    }
    public void Seek(float time)
    {
        StopMouthCycle();
        StopBlink();
        Timeline.Playing = false;
        ApplySample(Timeline.Seek(time));
    }
    public void Advance(float delta)
    {
        if (Timeline.Playing) { StopMouthCycle(); StopBlink(); }
        if (MouthCyclePlaying) AdvanceMouthCycle(delta);
        else if (BlinkPlaying) AdvanceBlink(delta);
        else ApplySample(Timeline.Advance(delta));
    }
    private void ApplySample(float[]? sample)
    {
        if (sample == null) return;
        ChannelsActive = false;
        sample.CopyTo(weights, 0);
    }

}
