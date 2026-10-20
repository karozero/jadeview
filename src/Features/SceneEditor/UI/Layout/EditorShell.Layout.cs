using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private readonly EditorPanelLayout panelLayout = new();

    private void DrawWorkspace()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        float splitterSize = 7 * _scale;
        float bodyHeight = Math.Max(1, available.Y - 30 * _scale);
        float usableHeight = Math.Max(1, bodyHeight - splitterSize);
        float browserHeight = panelLayout.GetBrowserHeight(usableHeight, _scale);
        float sceneHeight = Math.Max(1, usableHeight - browserHeight);

        ImGui.BeginChild("SceneWorkspace", new Vector2(available.X, sceneHeight));
        float columnWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - splitterSize * 2);
        Vector3 widths = panelLayout.GetWidths(columnWidth, _scale);
        ImGui.BeginChild("CentralViewportContainer", new Vector2(widths.X, 0), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawPanelTitle("VIEWPORT", "Explore the scene in 3D. Hold RMB and use WASD/QE to fly; Shift moves faster, and the wheel changes flight speed. Click to select; F frames the selection. Drag panel dividers to resize them.");
        Renderer.DrawViewport("MainViewport", ImGui.GetContentRegionAvail());
        ImGui.EndChild();
        DrawColumnSplitter("ViewportInspectorSplitter", columnWidth, splitterSize, true);
        ImGui.BeginChild("AssetInspectorPanel", new Vector2(widths.Y, 0), ImGuiChildFlags.Borders);
        if (_cutsceneMode) DrawCinematicInspector();
        else DrawAssetInspector();
        ImGui.EndChild();
        DrawColumnSplitter("InspectorOutlinerSplitter", columnWidth, splitterSize, false);
        ImGui.BeginChild("OutlinerPanel", Vector2.Zero, ImGuiChildFlags.Borders);
        DrawOutliner();
        ImGui.EndChild();
        ImGui.EndChild();

        // Place the horizontal handle without automatic item spacing around it.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        float verticalDelta = DrawPanelSplitter("BrowserSplitter", new Vector2(available.X, splitterSize), false);
        if (verticalDelta != 0) panelLayout.ResizeBrowser(usableHeight, _scale, verticalDelta);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) panelLayout.ResetBrowser();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        ImGui.BeginChild("ContentBrowserPanel", new Vector2(available.X, browserHeight), ImGuiChildFlags.Borders);
        if (_cutsceneMode) DrawCinematicBrowser();
        else DrawContentBrowser();
        ImGui.EndChild();
    }

    private void DrawColumnSplitter(string id, float width, float thickness, bool firstBoundary)
    {
        ImGui.SameLine(0, 0);
        float delta = DrawPanelSplitter(id, new Vector2(thickness, ImGui.GetContentRegionAvail().Y), true);
        if (delta != 0) panelLayout.ResizeColumns(width, _scale, delta, firstBoundary);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) panelLayout.ResetColumns();
        ImGui.SameLine(0, 0);
    }

}
