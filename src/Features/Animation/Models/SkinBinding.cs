using System.Numerics;

namespace JadeView;

public sealed record SkinBinding(int Bone, Matrix4x4 InverseBind, uint[] PackedWeights);
