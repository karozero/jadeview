using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Пространство координат для Gizmo (World / Local).
/// </summary>
public enum TransformSpace
{
    World,
    Local
}

