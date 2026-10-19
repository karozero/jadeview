using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private const int OutlinerPickerVisibleRows = 16;

    private void DrawOutlinerSceneSelection()
    {
        DrawCategoryTitle("Scene selection");
        if (!ImGui.BeginTable("OutlinerSceneSelection", 2, ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
        ImGui.TableSetupColumn("Selection", ImGuiTableColumnFlags.WidthStretch);
        DrawOutlinerArchivePicker();
        DrawOutlinerWorldPicker();
        ImGui.EndTable();
    }

    private static void DrawScenePickerLabel(string title, string help)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        DrawHelpMarker(help);
        ImGui.TableNextColumn();
    }

    private void DrawOutlinerArchivePicker()
    {
        if (Archive == null || _archiveScenes.Count == 0)
            return;
        DrawScenePickerLabel("Scene", "A scene container in the BF archive. Loading it opens its worlds, objects and related resources. Choose a subscene below to inspect one world inside this container.");
        ImGui.SetNextItemWidth(-1);
        ImGui.BeginDisabled(_activeJob != null);
        var activeScene = _archiveScenes.FirstOrDefault(scene => string.Equals(Document?.ContainerName, scene.Entry.Name, StringComparison.OrdinalIgnoreCase));
        string preview = activeScene != null ? $"{activeScene.Label} · {activeScene.Entry.Id}" : "Archive scenes";
        SetOutlinerPickerHeight();
        if (ImGui.BeginCombo("##ArchiveScenePicker", preview))
        {
            foreach (var scene in _archiveScenes)
                DrawArchiveSceneOption(scene);
            ImGui.EndCombo();
        }

        ImGui.EndDisabled();
    }

    private void DrawArchiveSceneOption(ArchiveSceneItem scene)
    {
        string label = $"{scene.Label} · {scene.Entry.Id}";
        bool selected = Document?.ContainerName.Equals(scene.Entry.Name, StringComparison.OrdinalIgnoreCase) == true;
        if (ImGui.Selectable(label, selected))
            OpenSceneContainer(scene.Entry);
        if (selected)
            ImGui.SetItemDefaultFocus();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(label + (scene.Diagnostic != null ? "\n" + scene.Diagnostic : ""));
    }

    private void DrawOutlinerWorldPicker()
    {
        if (Document == null || Document.Worlds.Count == 0)
            return;
        DrawScenePickerLabel("Subscene", "A world inside the loaded scene container. It may contain a location, an actor setup or cinematic objects. Selecting it changes the hierarchy and viewport without opening another archive container.");
        ImGui.SetNextItemWidth(-1);
        SetOutlinerPickerHeight();
        if (!ImGui.BeginCombo("##WorldPicker", ActiveWorld?.Name ?? "Select world"))
            return;
        foreach (var world in Document.Worlds)
        {
            bool selected = world == ActiveWorld;
            if (ImGui.Selectable($"{world.Name} ({world.Objects.Count} objects)", selected))
            {
                ActiveWorld = world;
                Renderer.ActiveWorld = world;
                SelectedObject = world.Objects.FirstOrDefault();
                Renderer.FrameAll();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void SetOutlinerPickerHeight()
    {
        var style = ImGui.GetStyle();
        float height = ImGui.GetTextLineHeightWithSpacing() * OutlinerPickerVisibleRows
            - style.ItemSpacing.Y + style.WindowPadding.Y * 2;
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, height));
    }

}
