using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Структура закешированной GPU-геометрии (Mesh и локальные границы).
/// </summary>
public sealed class GpuGeometry : IDisposable
{
    public uint Key { get; init; }
    public Geometry? Source { get; init; }
    public SceneObject? AlphaOwner { get; set; }
    public int AlphaRevision { get; set; }
    public SceneObject? DeformationOwner { get; set; }
    public int DeformationRevision { get; set; }
    public int ProceduralFrame=-1;
    public Vector3 ProceduralCameraPosition,ProceduralCameraTarget;
    public Matrix4x4 ProceduralMatrix;
    public List<Mesh> PartMeshes { get; } = [];
    public BoundingBox LocalBounds { get; set; }
    public Vector3 LocalCenter { get; set; }
    public float BoundingRadius { get; set; }

    public void Dispose()
    {
        foreach (var m in PartMeshes)
        {
            Raylib.UnloadMesh(m);
        }
        PartMeshes.Clear();
    }
}

