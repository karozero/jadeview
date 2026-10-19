namespace JadeView;

public sealed partial class SceneExporter
{
    private JadeMaterialLayer? ExportColorLayer(SceneObject sceneObject, List<JadeMaterialLayer> layers)
    {
        var selection = MaterialLayerRules.SelectColor(layers);
        if (selection.OmitsReflection)
            warnings.Add($"'{sceneObject.Name}' exports its detail color texture without the view-dependent reflection pass; reflections remain approximate.");
        return selection.Layer;
    }
}
