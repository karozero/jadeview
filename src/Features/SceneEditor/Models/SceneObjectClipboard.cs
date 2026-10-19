namespace JadeView;

internal sealed class SceneObjectClipboard(SceneDocument document, WorldData world, SceneStructureChange snapshot)
{
    internal SceneDocument Document { get; } = document;
    internal WorldData World { get; } = world;
    internal SceneStructureChange Snapshot { get; } = snapshot;
    internal int PasteCount { get; set; }
}
