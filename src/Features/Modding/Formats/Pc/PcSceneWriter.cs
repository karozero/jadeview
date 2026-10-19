namespace JadeView;
/// <summary>Rebuilds a PC FF0 stream through the loader's dependency queue.</summary>
public static class PcSceneWriter
{
    public static byte[] Rebuild(byte[] original, SceneDocument document, ModProject project, ResourceResolver? resolver = null)
    {
        var rebuild = new PcSceneRebuild(original, document, project, resolver);
        return rebuild.Build();
    }
}
