using System.Numerics;

namespace JadeView;

public sealed record ActorBoneLink(uint Key, int Index, Matrix4x4 Matrix);
