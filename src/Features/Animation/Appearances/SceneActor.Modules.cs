namespace JadeView;

public sealed partial class SceneActor
{
    private ActorModuleMap? moduleMap;
    private bool invalidModuleMap;
    internal PcMorphControllerBinding? MouthController { get; set; }

    public SceneObject? FindModule(byte selector)
    {
        if (invalidModuleMap) return null;
        // Native B59CC0 uses component ordinal only if no module map exists.
        int component = moduleMap?.FindComponent(selector, Bones) ?? selector;
        return component >= 0 && component < Bones.Count ? Bones[component] : null;
    }

    private void UpdateModuleMap(byte[] shape)
    {
        // Keep viewing legacy/unqualified shapes possible; do not invent a map.
        try
        {
            moduleMap = ActorModuleMap.Read(shape, moduleMap);
            invalidModuleMap = false;
        }
        catch (InvalidDataException)
        {
            moduleMap = null;
            invalidModuleMap = true;
        }
    }
}
