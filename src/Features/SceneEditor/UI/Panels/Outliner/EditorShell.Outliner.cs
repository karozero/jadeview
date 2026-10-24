using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private readonly List<OutlinerRow> _outlinerRows = new(512);
    private readonly HashSet<SceneObject> _outlinerMatches = [];
    private readonly HashSet<SceneObject> _expandedOutlinerActors = [];

    private void DrawOutliner()
    {
        DrawPanelTitle("OUTLINER", "Objects placed in the current world, grouped by actor. Search and select objects here; right-click for scene actions. Double-click to frame an object.");
        DrawOutlinerSearch();
        if (!_cutsceneMode)
        {
            DrawOutlinerSceneSelection();
            DrawOutlinerDisplay();
        }
        DrawCategoryTitle("Scene objects", "Objects in the selected world, grouped by actor. Filters below affect this list only.");
        DrawCurrentSceneName(showObjectCount: true);
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

        DrawOutlinerContentTabs(ActiveWorld);
        BuildOutlinerRows(ActiveWorld);
        ImGui.BeginChild("OutlinerList", Vector2.Zero, ImGuiChildFlags.None);
        int revealIndex = FindRevealedOutlinerRow();
        int revealActorIndex = FindRevealedActorHeader();
        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_outlinerRows.Count);
            if (revealActorIndex >= 0) clipper.IncludeItemByIndex(revealActorIndex);
            if (revealIndex >= 0) clipper.IncludeItemByIndex(revealIndex);
            while (clipper.Step())
            {
                for (int rowIndex = clipper.DisplayStart; rowIndex < clipper.DisplayEnd; rowIndex++)
                    DrawOutlinerRow(_outlinerRows[rowIndex]);
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
    }

    private void BuildOutlinerRows(WorldData world)
    {
        OutlinerRowBuilder.Build(world, MatchesOutlinerFilter, _expandedOutlinerActors,
            _revealOutliner, _outlinerRows, _outlinerMatches, _outlinerContentKind);
    }

    private void DrawOutlinerContentTabs(WorldData world)
    {
        int actorCount = world.Actors.Count(actor => actor.Members.Any(MatchesOutlinerFilter));
        int objectCount = world.Objects.Count(sceneObject => sceneObject.Actor == null && MatchesOutlinerFilter(sceneObject));
        if (!ImGui.BeginTabBar("OutlinerContentTabs")) return;
        DrawOutlinerTab(OutlinerContentKind.All, $"All objects ({world.Objects.Count(MatchesOutlinerFilter)})");
        DrawOutlinerTab(OutlinerContentKind.Actors, $"Actors ({actorCount})");
        DrawOutlinerTab(OutlinerContentKind.StandaloneObjects, $"Objects ({objectCount})");
        ImGui.EndTabBar();
    }

    private void DrawOutlinerTab(OutlinerContentKind kind, string label)
    {
        ImGuiTabItemFlags flags = _outlinerContentKind == kind ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!BeginTabItemWithFlags(label, flags)) return;
        _outlinerContentKind = kind;
        ImGui.EndTabItem();
    }

    private int FindRevealedOutlinerRow()
    {
        return FindOutlinerRow(_revealOutliner);
    }

    private int FindRevealedActorHeader()
    {
        SceneObject? actorRoot = _revealOutliner?.Actor?.Root;
        return actorRoot == null ? -1 : FindOutlinerRow(actorRoot);
    }

    private int FindOutlinerRow(SceneObject? sceneObject)
    {
        if (sceneObject == null) return -1;
        for (int rowIndex = 0; rowIndex < _outlinerRows.Count; rowIndex++)
            if (_outlinerRows[rowIndex].Object == sceneObject)
                return rowIndex;
        return -1;
    }

    private void DrawOutlinerRow(OutlinerRow row)
    {
        if (row.Kind == OutlinerRowKind.Actor && row.Actor is { } actor)
            DrawOutlinerActor(actor);
        else if (row.Kind == OutlinerRowKind.ActorComponent)
            DrawOutlinerActorComponent(row.Object);
        else
            DrawOutlinerObject(row.Object);
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
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (SelectedObject == actor.Root)
            flags |= ImGuiTreeNodeFlags.Selected;
        bool revealChild = _revealOutliner?.Actor == actor && _revealOutliner != actor.Root;
        if (revealChild)
            ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"[Actor] {actor.Root.Name} ({actor.Members.Count - 1})", flags);
        if (open) _expandedOutlinerActors.Add(actor.Root);
        else _expandedOutlinerActors.Remove(actor.Root);
        DrawObjectContextMenu(actor.Root);
        RevealOutlinerObject(actor.Root);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
            SelectedObject = actor.Root;
        FrameOutlinerObjectOnDoubleClick(actor.Root);
        ImGui.PopID();
    }

    private void DrawOutlinerActorComponent(SceneObject component)
    {
        ImGui.Indent();
        ImGui.PushID(component.Id);
        if (ImGui.Selectable($"{component.Name}##{component.Id}", SelectedObject == component))
            SelectedObject = component;
        DrawObjectContextMenu(component);
        RevealOutlinerObject(component);
        FrameOutlinerObjectOnDoubleClick(component);
        ImGui.PopID();
        ImGui.Unindent();
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
        if (ImGui.MenuItem("Copy name"))
            ImGui.SetClipboardText(obj.Name);
        ImGui.Separator();
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
