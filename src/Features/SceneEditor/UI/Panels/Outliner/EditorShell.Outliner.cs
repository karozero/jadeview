using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;
public sealed partial class EditorShell
{
    private void DrawOutliner()
    {
        DrawPanelTitle("OUTLINER", "Objects placed in the current world, grouped by actor. Search and select objects here; right-click for scene actions. Double-click to frame an object.");
        DrawOutlinerSearch();
        DrawOutlinerSceneSelection();
        DrawOutlinerDisplay();
        DrawCategoryTitle("Scene objects", "Objects in the selected world, grouped by actor. Filters below affect this list only.");
        ImGui.TextWrapped(ActiveWorld != null ? $"{ActiveWorld.Name} ({ActiveWorld.Objects.Count} objects)" : "No world");
        bool canAddObjects = ActiveWorld != null && Resolver.Geometries.Count > 0;
        ImGui.BeginDisabled(!canAddObjects);
        if (ImGui.Button("Add object"))
            ImGui.OpenPopup("AddSceneObjectFromAsset");
        ImGui.EndDisabled();
        DrawAddSceneObjectPopup();
        DrawOutlinerFilters();
        if (ActiveWorld == null || ActiveWorld.Objects.Count == 0)
        {
            ImGui.TextDisabled("No objects in the active world.");
            return;
        }

        var filtered = ActiveWorld.Objects.Where(MatchesOutlinerFilter).ToList();
        var shown = filtered.ToHashSet();
        ImGui.BeginChild("OutlinerList", Vector2.Zero, ImGuiChildFlags.None);
        foreach (var actor in ActiveWorld.Actors.Where(actor => actor.Members.Any(shown.Contains)))
            DrawOutlinerActor(actor);
        foreach (var sceneObject in filtered.Where(sceneObject => sceneObject.Actor == null))
            DrawOutlinerObject(sceneObject);
        ImGui.EndChild();
    }

    private void DrawOutlinerSearch()
    {
        DrawCategoryTitle("Search", "Find objects by name or GAO key in the selected world.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchOutliner", "Search name or GAO key...", ref _searchOutliner, 128);
    }

    private void DrawOutlinerFilters()
    {
        ImGui.RadioButton("All", ref _outlinerFilterType, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Movable", ref _outlinerFilterType, 1);
        ImGui.SameLine();
        ImGui.RadioButton("Restricted", ref _outlinerFilterType, 2);
    }

    private bool MatchesOutlinerFilter(SceneObject sceneObject)
    {
        bool matchesSearch = string.IsNullOrEmpty(_searchOutliner) || sceneObject.Name.Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase) || sceneObject.GaoKey.ToString("X8").Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase);
        if (!matchesSearch)
            return false;
        return _outlinerFilterType switch
        {
            1 => sceneObject.Capabilities.CanTranslate,
            2 => !sceneObject.Capabilities.CanTranslate,
            _ => true
        };
    }

    private void DrawOutlinerActor(SceneActor actor)
    {
        ImGui.PushID(actor.Root.Id);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (SelectedObject == actor.Root)
            flags |= ImGuiTreeNodeFlags.Selected;
        bool revealChild = _revealOutliner?.Actor == actor && _revealOutliner != actor.Root;
        if (revealChild)
            ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"[Actor] {actor.Root.Name} ({actor.Members.Count - 1})", flags);
        DrawObjectContextMenu(actor.Root);
        RevealOutlinerObject(actor.Root);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
            SelectedObject = actor.Root;
        FrameOutlinerObjectOnDoubleClick(actor.Root);
        if (open)
        {
            foreach (var component in actor.Members.Where(component => component != actor.Root))
            {
                if (ImGui.Selectable($"{component.Name}##{component.Id}", SelectedObject == component))
                    SelectedObject = component;
                DrawObjectContextMenu(component);
                RevealOutlinerObject(component);
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void DrawOutlinerObject(SceneObject sceneObject)
    {
        ImGui.PushID(sceneObject.Id);
        string prefix = Renderer.IsHidden(sceneObject) ? "[hidden] " : "";
        string label = $"{prefix}{sceneObject.Name} ({sceneObject.GaoKey:X8})";
        if (ImGui.Selectable(label, SelectedObject == sceneObject))
            SelectedObject = sceneObject;
        DrawObjectContextMenu(sceneObject);
        RevealOutlinerObject(sceneObject);
        FrameOutlinerObjectOnDoubleClick(sceneObject);
        ImGui.PopID();
    }

    private void RevealOutlinerObject(SceneObject sceneObject)
    {
        if (_revealOutliner != sceneObject)
            return;
        ImGui.SetScrollHereY(.5f);
        _revealOutliner = null;
    }

    private void FrameOutlinerObjectOnDoubleClick(SceneObject sceneObject)
    {
        if (!ImGui.IsItemHovered() || !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            return;
        SelectedObject = sceneObject;
        Renderer.FrameSelected();
    }

    private void DrawObjectContextMenu(SceneObject obj)
    {
        if (!ImGui.BeginPopupContextItem("ObjectActions##" + obj.Id))
            return;
        SelectedObject = obj;
        bool canEditStructure = ActiveWorld != null && obj.Actor == null;
        if (ImGui.MenuItem("Duplicate", null, false, canEditStructure && CanDuplicateSceneObject(obj)))
            DuplicateSceneObject(obj);
        if (ImGui.MenuItem("Delete from scene", "Delete", false, canEditStructure))
            DeleteSceneObject(obj);
        if (!canEditStructure && obj.Actor != null)
            ImGui.TextDisabled("Actor hierarchies cannot be copied or removed as individual objects yet.");
        ImGui.Separator();
        if (ImGui.MenuItem("Export..."))
        {
            _exportTarget = obj;
            _openExport = true;
        }

        ImGui.EndPopup();
    }
}
