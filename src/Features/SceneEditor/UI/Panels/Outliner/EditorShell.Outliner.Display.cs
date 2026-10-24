using static JadeView.EditorUi;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawOutlinerDisplay()
    {
        ImGui.Separator();
        bool open = ImGui.TreeNodeEx("Display", ImGuiTreeNodeFlags.NoTreePushOnOpen);
        DrawHelpMarker("Viewport display settings only; these do not change the scene or exported assets. Game view temporarily hides editor overlays and colliders regardless of these settings.");
        if (!open) return;

        ImGui.Indent();
        ImGui.Checkbox("Floor grid", ref Renderer.ShowGrid);
        ImGui.Checkbox("Scene wireframe", ref Renderer.Wireframe);
        ImGui.Checkbox("Selection wireframe", ref Renderer.ShowSelectionWireframe);
        ImGui.Checkbox("Colliders", ref Renderer.ShowColliders);
        ImGui.Checkbox("Transform gizmo", ref Renderer.ShowGizmo);
        ImGui.Checkbox("Skeleton badges", ref Renderer.ShowSkeletonBadges);
        ImGui.Checkbox("Dynamic lighting", ref Renderer.DynamicLighting);
        DrawHelpMarker("Preview the supported Omni, Direct and Spot diffuse lights. Specular, native sector culling and some script-driven light changes are still incomplete. This viewport switch does not change BF data or export settings.");
        ImGui.Checkbox("Game-hidden objects", ref Renderer.ShowGameHidden);
        ImGui.Unindent();
    }
}
