namespace JadeView;

/// <summary>
/// Names for Jade's TEXPRO procedural texture callbacks.
/// Values are defined by Jaded's TEXture/TEXprocedural.h.
/// </summary>
public static class ProceduralTextureKind
{
    public static string Name(int subtype) => subtype switch
    {
        0 => "Unknown",
        1 => "Water",
        2 => "Fire",
        3 => "Video stream",
        4 => "Photo capture",
        5 => "Plasma",
        _ => "Unrecognized"
    };

    public static string PreviewStatus(int subtype) => subtype switch
    {
        1 => "wave simulation needs runtime disturbance impulses",
        2 => "fire simulation state is generated at runtime",
        3 => "cinematics use recovered MPEG-player capability triggers; runtime callback gates are not evaluated",
        4 => "requires a scripted camera capture",
        5 => "randomized noise and phase are initialized at runtime",
        _ => "runtime generation is not implemented"
    };
}
