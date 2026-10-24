namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private PcMorphControllerBinding? boundController;
    private PcMorphControllerBinding? cachedControllerDataSource;
    private PcMorphMouthCycleDefinition? cachedControllerData;
    private string? cachedControllerDataFailure;
    private bool hasCachedControllerData;
    internal int ControllerDataBindingCount { get; private set; }
    public PcMorphMouthCycleDefinition? MouthCycleDefinition { get; private set; }
    public string? ControllerBindingDiagnostic { get; private set; }

    private void BindController(SceneObject member)
    {
        var actor = member.Actor;
        var binding = actor?.MouthController;
        bool moduleMatches = binding != null && actor!.FindModule(binding.ModuleSelector) == member;
        var geometryBinding = binding?.MatchMorphData == true ? ResolveControllerData(binding) : null;
        bool geometryMatches = geometryBinding != null;
        var controller = moduleMatches || geometryMatches ? binding : null;
        ControllerBindingDiagnostic = binding == null ? "No source controller binding was discovered."
            : moduleMatches ? $"Matched source module selector {binding.ModuleSelector}."
            : geometryMatches ? "Matched controller channel and form sequence against this morph mesh."
            : binding.MatchMorphData ? "Source controller sequence does not match this morph mesh."
            : $"Source controller expects module selector {binding.ModuleSelector}; this member differs.";
        if (!ReferenceEquals(controller, boundController))
        {
            StopMouthCycle();
            boundController = controller;
            MouthCycleDefinition = controller == null ? null : ResolveControllerData(controller);
        }
        if (controller != null && MouthCycleDefinition == null)
        {
            if (cachedControllerDataFailure != null)
                ControllerBindingDiagnostic += $" Mouth preview unavailable: {cachedControllerDataFailure}.";
        }
    }

    private PcMorphMouthCycleDefinition? ResolveControllerData(PcMorphControllerBinding binding)
    {
        if (hasCachedControllerData && ReferenceEquals(binding, cachedControllerDataSource))
            return cachedControllerData;

        cachedControllerDataSource = binding;
        cachedControllerData = binding.TryBind(Data, out cachedControllerDataFailure);
        hasCachedControllerData = true;
        ControllerDataBindingCount++;
        return cachedControllerData;
    }
}
