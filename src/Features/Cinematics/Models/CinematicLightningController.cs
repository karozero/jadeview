namespace JadeView;

internal sealed record CinematicLightningController(uint OwnerKey, uint CapabilityActorKey,
    uint DestinationKey, uint InitialCapabilities, int Mode, int CapabilityMask,
    float OnDelayMin, float OnDelayMax, float OffDelayMin, float OffDelayMax,
    float FadeIn, float FadeOut, float Width, float Radius, int SegmentCount,
    int LightningMode, uint OffColor, uint OnColorFirst, uint OnColorSecond, float Frequency);

internal readonly record struct CinematicLightningState(System.Numerics.Vector3 Start,
    System.Numerics.Vector3 End, uint Color, float Opacity, float Width, float Radius,
    int SegmentCount, uint Seed, float Time);
