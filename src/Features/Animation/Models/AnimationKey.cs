using System.Numerics;

namespace JadeView;

public sealed class AnimationKey
{
    public float Time, Duration;
    public Vector3? Position, Scale, Control1, Control2;
    public Quaternion? Rotation;
    public int Flags, Format;
    public List<AnimationOpaquePayload> OpaquePayloads = [];
}
