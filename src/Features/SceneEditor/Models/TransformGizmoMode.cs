using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Режим трансформации 3D Gizmo (W: перемещение, E: вращение, R: масштаб).
/// </summary>
public enum TransformGizmoMode
{
    Translate,
    Rotate,
    Scale
}

