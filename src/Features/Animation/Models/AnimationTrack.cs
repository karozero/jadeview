using System.Numerics;

namespace JadeView;

public sealed class AnimationTrack
{
    public uint Target;
    public int Bone;
    public List<AnimationKey> Keys = [];
}
