using ImGuiNET;

namespace JadeView;

internal static partial class EditorUi
{
    internal static void DrawPanelTitle(string title, string help)
    {
        if (EditorTheme.HasWindowTitleFont) ImGui.PushFont(EditorTheme.WindowTitleFont);
        ImGui.TextColored(EditorTheme.Green, title);
        if (EditorTheme.HasWindowTitleFont) ImGui.PopFont();
        DrawHelpMarker(help);
    }

    internal static void DrawHelpMarker(string help)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
        ImGui.TextUnformatted(help);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    internal static void DrawCategoryTitle(string title, string? help = null)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(title);
        if (help != null) DrawHelpMarker(help);
    }
}
