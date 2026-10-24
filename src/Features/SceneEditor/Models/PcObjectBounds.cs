using System.Numerics;

namespace JadeView;

internal enum PcObjectBoundsKind
{
    AxisAlignedBox,
    OrientedBox,
    Sphere
}

/// <summary>
/// PC GAO bounds as stored by the retail loader. For boxes, Minimum/Maximum are the first
/// serialized vectors; for a sphere, Minimum is its center and SphereRadius is Maximum.X.
/// OBBox records additionally preserve their local-corner vectors.
/// </summary>
internal sealed record PcObjectBounds(
    PcObjectBoundsKind Kind,
    Vector3 Minimum,
    Vector3 Maximum,
    Vector3? LocalMinimum,
    Vector3? LocalMaximum,
    float? SphereRadius)
{
    internal const uint OrientedBoundsIdentityFlag = 0x00080000;

    internal bool HasMeaningfulExtent => Kind == PcObjectBoundsKind.Sphere
        ? SphereRadius is > 0
        : Minimum.X != Maximum.X || Minimum.Y != Maximum.Y || Minimum.Z != Maximum.Z;
}
