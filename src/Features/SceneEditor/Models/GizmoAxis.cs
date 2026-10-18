using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Ось трансформации 3D Gizmo.
/// </summary>
public enum GizmoAxis
{
    None = 0,
    X,
    Y,
    Z
}

