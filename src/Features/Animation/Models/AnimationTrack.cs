using System.Numerics;

namespace JadeView;

public sealed class AnimationTrack
{
    public uint Target;
    public int Bone;
    public int ChannelFlags;
    public uint HeaderWord;
    public bool UsesFlashReference;
    public List<AnimationKey> Keys = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal CinematicTrackPlaybackClock? CinematicPlaybackClock { get; set; }
}
