using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Набор GPU-материалов для частей геометрии.
/// </summary>
public sealed class GpuMaterialSet : IDisposable
{
    public (uint GeoKey, uint MatKey) Key { get; init; }
    public List<Material> PartMaterials { get; } = [];
    public List<List<GpuMaterialPass>> Passes { get; } = [];

    public unsafe void Dispose()
    {
        foreach (var mat in Passes.SelectMany(p => p).Select(p => p.Material))
        {
            if (mat.Maps != null)
            {
                Raylib.MemFree(mat.Maps);
            }
        }
        PartMaterials.Clear();
        Passes.Clear();
    }
}

