namespace JadeView;

public sealed class SceneExportOptions
{
    public bool Textures = true, Actors = true, Collisions, UnknownObjects, Animations, AllClips, GameHidden;
    public int Placement; // 0 current, 1 original, 2 origin
    public bool MorphAnimations = true;
}

