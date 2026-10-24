using System.Numerics;

namespace JadeView;

public sealed class AnimationClip
{
    public uint Key;
    public string Name = "";
    public float Duration;
    public List<AnimationTrack> Tracks = [];
    public string? Error;
    public int Unsupported;
    internal Func<AnimationClip>? Read;
    public bool Loaded;
    public bool SceneMotion;
    public bool Cinematic;
    public uint CinematicOwnerKey;
    internal uint[] CinematicOwnerKeys = [];
    internal bool UsesEventTrackExecutionContext;
    public uint CinematicWorldKey;
    public CinematicBodyTimeline? CinematicBody;
    public void EnsureLoaded()
    {
        if (Loaded) return;
        Loaded = true;
        try { var value = Read!(); Tracks = value.Tracks; Duration = value.Duration; Unsupported = value.Unsupported; }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException)
        { Error = e.Message; }
        Read = null;
    }
}

