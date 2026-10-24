namespace JadeView;

public sealed record CinematicEnvironmentController(uint OwnerKey, uint WorldKey, uint[] ObjectKeys)
{
    internal uint EnvironmentKey { get; init; }
    internal uint SkyKey { get; init; }
    internal uint FogKey { get; init; }
    internal uint SunKey { get; init; }
    internal uint MoonKey { get; init; }
    internal uint MoonLightKey { get; init; }
    internal uint MoonRayKey { get; init; }
    internal uint DeactivationObjectKey { get; init; }
    internal uint StandardControllerKey { get; init; }
    internal float NightHeight { get; init; }
    internal float DawnHeight { get; init; }
    internal float SunriseHeight { get; init; }
    internal float DayHeight { get; init; }
    internal float NightRliFactor { get; init; }
    internal float DawnRliFactor { get; init; }
    internal float SunsetRliFactor { get; init; }
    internal float DayRliFactor { get; init; }
    internal float FogNear { get; init; }
    internal float FogFar { get; init; }
    internal float AttackFogNear { get; init; }
    internal float AttackFogFar { get; init; }
    internal float AttackTransitionSpeed { get; init; }
    internal float AttackReturnSpeed { get; init; }
    internal uint[][] NormalPalettes { get; init; } = [];
    internal uint[][] AttackPalettes { get; init; } = [];
}
