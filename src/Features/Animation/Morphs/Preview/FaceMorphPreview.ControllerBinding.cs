namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private PcMorphControllerBinding? boundController;
    public PcMorphMouthCycleDefinition? MouthCycleDefinition { get; private set; }
    public string? ControllerBindingDiagnostic { get; private set; }

    private void BindController(SceneObject member)
    {
        var actor = member.Actor;
        var binding = actor?.MouthController;
        bool moduleMatches = binding != null && actor!.FindModule(binding.ModuleSelector) == member;
        var geometryBinding = binding?.MatchMorphData == true ? binding.Bind(Data) : null;
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
            MouthCycleDefinition = controller?.Bind(Data);
        }
        if (controller != null && MouthCycleDefinition == null)
        {
            controller.TryBind(Data, out var bindFailure);
            if (bindFailure != null) ControllerBindingDiagnostic += $" Mouth preview unavailable: {bindFailure}.";
        }
    }
}
