namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private PcMorphControllerBinding? boundController;
    public PcMorphMouthCycleDefinition? MouthCycleDefinition { get; private set; }

    private void BindController(SceneObject member)
    {
        var controller = member.Actor?.FindModule(0) == member ? member.Actor.MouthController : null;
        if (ReferenceEquals(controller, boundController)) return;
        StopMouthCycle();
        boundController = controller;
        MouthCycleDefinition = controller?.Bind(Data);
    }
}
