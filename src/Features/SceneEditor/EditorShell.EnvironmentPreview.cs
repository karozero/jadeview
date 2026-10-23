namespace JadeView;

public sealed partial class EditorShell
{
    private WorldData? _environmentPreviewWorld;
    private CinematicMaterialState? _environmentPreviewMaterials;

    private CinematicMaterialState? ResolveEnvironmentPreview(WorldData? world)
    {
        if (world == null || Document == null)
        {
            _environmentPreviewWorld = null;
            _environmentPreviewMaterials = null;
            return null;
        }

        if (!ReferenceEquals(world, _environmentPreviewWorld))
        {
            var controllers = Document.CinematicEnvironmentControllers.Values
                .Where(controller => controller.WorldKey == world.Key)
                .ToArray();
            _environmentPreviewWorld = world;
            _environmentPreviewMaterials = controllers.Length == 0
                ? null
                : new CinematicMaterialState(new AnimationClip(), world, Resolver.Materials, controllers);
        }

        _environmentPreviewMaterials?.Evaluate((float)Raylib_cs.Raylib.GetTime());
        return _environmentPreviewMaterials;
    }
}
