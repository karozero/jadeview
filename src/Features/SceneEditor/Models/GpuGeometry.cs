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
    internal GpuDiffuseLightingSamples DiffuseLighting { get; } = new();
    public List<BoundingBox> PartLocalBounds { get; } = [];
    internal Vector3[] NormalBuffer { get; private set; } = [];
    internal int[] NormalCountsBuffer { get; private set; } = [];
    internal Vector3[] DeformedPositionBuffer { get; private set; } = [];
    public BoundingBox LocalBounds { get; set; }
    public Vector3 LocalCenter { get; set; }
    public float BoundingRadius { get; set; }

    internal void EnsureNormalBuffers(int vertexCount)
    {
        if (NormalBuffer.Length == vertexCount && NormalCountsBuffer.Length == vertexCount) return;
        NormalBuffer = new Vector3[vertexCount];
        NormalCountsBuffer = new int[vertexCount];
    }

    internal void EnsureDeformedPositionBuffer(int vertexCount)
    {
        if (DeformedPositionBuffer.Length == vertexCount) return;
        DeformedPositionBuffer = new Vector3[vertexCount];
    }

    public void Dispose()
    {
        foreach (var m in PartMeshes)
        {
            Raylib.UnloadMesh(m);
        }
        PartMeshes.Clear();
        DiffuseLighting.Clear();
        PartLocalBounds.Clear();
        NormalBuffer = [];
        NormalCountsBuffer = [];
        DeformedPositionBuffer = [];
    }
}

