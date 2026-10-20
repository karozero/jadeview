using System.Numerics;
using Raylib_cs;

namespace JadeView;

public sealed record GpuMaterialPass(Material Material, JadeMaterialLayer Layer, Vector4 Constant,
    uint MaterialKey, int LayerIndex, int SubMaterialIndex);
