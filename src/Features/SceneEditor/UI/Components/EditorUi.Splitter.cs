using System.Numerics;
using ImGuiNET;

namespace JadeView;

internal static partial class EditorUi
{
    internal static float DrawPanelSplitter(string id, Vector2 size, bool vertical)
    {
        ImGui.InvisibleButton(id, size);
        bool active = ImGui.IsItemActive();
        bool hovered = ImGui.IsItemHovered();
        uint handleColor = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive
            : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
        Vector2 center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
        Vector2 halfLine = vertical ? new Vector2(0, size.Y * .5f) : new Vector2(size.X * .5f, 0);
        ImGui.GetWindowDrawList().AddLine(center - halfLine, center + halfLine, handleColor);
        if (active || hovered)
        {
            ImGui.SetMouseCursor(vertical ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS);
            uint color = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive : ImGuiCol.SeparatorHovered);
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), color);
        }
        if (hovered) ImGui.SetTooltip("Drag to resize. Double-click to reset.");
        if (!active) return 0;
        return vertical ? ImGui.GetIO().MouseDelta.X : ImGui.GetIO().MouseDelta.Y;
    }

}
