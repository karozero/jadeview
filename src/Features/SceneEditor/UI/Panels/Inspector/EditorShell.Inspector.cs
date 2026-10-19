using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawAssetInspector()
    {
        DrawPanelTitle("ASSET INSPECTOR", "Settings for the object selected in the Outliner or viewport. Properties contains transforms and resources; Animation and Morphs control actor previews. Frame focuses the camera; Export asset exports the selection.");
        if (SelectedObject == null)
        {
            ImGui.TextWrapped("Select an object in the Outliner or viewport to edit its properties, animations and morphs.");
            return;
        }
        DrawCategoryTitle("Selected asset");
        ImGui.TextWrapped(SelectedObject.Name);
        if (ImGui.Button("Frame")) Renderer.FrameSelected();
        ImGui.SameLine();
        if (ImGui.Button("Export asset..."))
        {
            _exportTarget = SelectedObject;
            _openExport = true;
        }
        ImGui.Separator();
        if (!ImGui.BeginTabBar("AssetInspectorTabs")) return;
        if (ImGui.BeginTabItem("Properties"))
        {
            DrawDetails();
            ImGui.EndTabItem();
        }
        if (SelectedObject.Actor != null && ImGui.BeginTabItem("Animation"))
        {
            DrawAnimationControls();
            ImGui.EndTabItem();
        }
        if (SelectedObject.Actor != null && ImGui.BeginTabItem("Morphs"))
        {
            DrawMorphControls();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
}
