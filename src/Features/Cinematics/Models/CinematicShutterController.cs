using System.Numerics;

namespace JadeView;

public sealed record CinematicShutterController(float CloseTime, float HoldTime, float OpenTime,
    Vector2 Center, ushort InitialCapabilities = 0);

public readonly record struct CinematicShutterState(float Closure, Vector2 Center);
