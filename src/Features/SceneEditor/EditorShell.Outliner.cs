using System.Numerics;
using ImGuiNET;

namespace JadeView;
public sealed partial class EditorShell
{
    private void DrawOutliner()
    {
        ImGui.TextColored(Green, "OUTLINER");
        ImGui.SameLine();
        ImGui.TextDisabled(ActiveWorld != null ? $"{ActiveWorld.Name} ({ActiveWorld.Objects.Count})" : "No world");
        ImGui.SameLine();
        bool canAddObjects = ActiveWorld != null && Resolver.Geometries.Count > 0;
        ImGui.BeginDisabled(!canAddObjects);
        if (ImGui.Button("Add object"))
            ImGui.OpenPopup("AddSceneObjectFromAsset");
        ImGui.EndDisabled();
        DrawAddSceneObjectPopup();
        DrawOutlinerArchivePicker();
        DrawOutlinerWorldPicker();
        DrawOutlinerFilters();
        if (ActiveWorld == null || ActiveWorld.Objects.Count == 0)
        {
            ImGui.TextDisabled("No objects in the active world.");
            return;
        }

        var filtered = ActiveWorld.Objects.Where(MatchesOutlinerFilter).ToList();
        var shown = filtered.ToHashSet();
        ImGui.BeginChild("OutlinerList", Vector2.Zero, ImGuiChildFlags.None);
        DrawAnimationControls();
        DrawMorphControls();
        foreach (var actor in ActiveWorld.Actors.Where(actor => actor.Members.Any(shown.Contains)))
            DrawOutlinerActor(actor);
        foreach (var sceneObject in filtered.Where(sceneObject => sceneObject.Actor == null))
            DrawOutlinerObject(sceneObject);
        ImGui.EndChild();
    }

    private void DrawOutlinerArchivePicker()
    {
        if (Archive == null || _archiveScenes.Count == 0)
            return;
        ImGui.SetNextItemWidth(-1);
        ImGui.BeginDisabled(_activeJob != null);
        var activeScene = _archiveScenes.FirstOrDefault(scene => string.Equals(Document?.ContainerName, scene.Entry.Name, StringComparison.OrdinalIgnoreCase));
        string preview = activeScene != null ? $"{activeScene.Label} · {activeScene.Entry.Id}" : "Archive scenes";
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
        if (Document == null || Document.Worlds.Count <= 1)
            return;
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

    private void DrawOutlinerFilters()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchOutliner", "Search name or GAO key...", ref _searchOutliner, 128);
        ImGui.RadioButton("All", ref _outlinerFilterType, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Static", ref _outlinerFilterType, 1);
        ImGui.SameLine();
        ImGui.RadioButton("Restricted", ref _outlinerFilterType, 2);
        bool showSelectionWireframe = Renderer.ShowSelectionWireframe;
        if (ImGui.Checkbox("Selection wireframe", ref showSelectionWireframe))
            Renderer.ShowSelectionWireframe = showSelectionWireframe;
        ImGui.Separator();
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
