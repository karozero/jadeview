using System.Numerics;

namespace JadeView;

internal sealed record BakedMaterialTexture(int Size, byte[] Rgba, Vector2[] CornerUvs);
