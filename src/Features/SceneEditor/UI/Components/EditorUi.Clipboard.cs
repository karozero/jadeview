using ImGuiNET;

namespace JadeView;

internal static partial class EditorUi
{
    internal static void DrawCopyNameContextMenu(string popupId, string? name, string label = "Copy name")
    {
        if (string.IsNullOrWhiteSpace(name) || !ImGui.BeginPopupContextItem(popupId)) return;
        if (ImGui.MenuItem(label)) ImGui.SetClipboardText(name);
        ImGui.EndPopup();
    }
}
