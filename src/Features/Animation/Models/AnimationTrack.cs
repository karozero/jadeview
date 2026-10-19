using System.Numerics;

namespace JadeView;

public sealed class AnimationTrack
{
    public uint Target;
    public int Bone;
    public int ChannelFlags;
    public uint HeaderWord;
    public List<AnimationKey> Keys = [];
}
