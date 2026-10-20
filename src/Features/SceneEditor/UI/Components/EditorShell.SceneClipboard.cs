using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCurrentSceneName(bool showObjectCount = false)
    {
        string? name = Document?.Worlds.FirstOrDefault(world => world.Key == ActiveWorld?.Key)?.Name
            ?? ActiveWorld?.Name ?? Document?.ContainerName;
        string count = showObjectCount && ActiveWorld != null ? $" ({ActiveWorld.Objects.Count} objects)" : "";
        ImGui.TextWrapped(name == null ? "No scene" : name + count);
        DrawCopyNameContextMenu("CurrentSceneName", name, "Copy scene name");
    }
}
