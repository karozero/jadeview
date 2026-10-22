using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicSelectedObjectEventCommands()
    {
        if (Document == null || _cinematicWorld == null || SelectedObject == null) return;

        var eventLists = Document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists);
        var commands = CinematicEventTargetCommandResolver.Resolve(eventLists,
            _cinematicWorld.Key, SelectedObject.GaoKey);
        if (!ImGui.CollapsingHeader($"SceneEvents targeting selected object ({commands.Length})")) return;

        ImGui.TextWrapped($"Static SceneEvents commands targeting {SelectedObject.Name} ({SelectedObject.GaoKey:X8}) in this cinematic world. This is a target index, not proof that an event track played.");
        if (commands.Length == 0)
        {
            ImGui.TextDisabled("No decoded literal SceneEvents calls target this object in the selected world.");
            return;
        }

        foreach (var command in commands.Take(64))
        {
            string function = CinematicEngineFunctionCatalog.NameFor(command.Call.Selector);
            string arguments = string.Join(", ", command.Call.Arguments.Select(argument => $"0x{argument:X8}"));
            string label = $"{command.Time:F2}s · {command.EventList.OwnerName} · EVE {command.Track.Index} · {function}({arguments})";
            var eventOwner = _cinematicWorld.Objects.FirstOrDefault(sceneObject =>
                sceneObject.GaoKey == command.EventList.OwnerKey);
            if (eventOwner != null && ImGui.Selectable($"{label}##targetcommand{command.EventList.OwnerKey:X8}{command.Track.Index}{command.Time:F3}"))
            {
                SelectedObject = eventOwner;
                var scriptOwner = Document.CinematicScriptOwners.Values.FirstOrDefault(owner =>
                    owner.WorldKey == command.EventList.WorldKey
                    && owner.OwnerKey == command.EventList.OwnerKey);
                if (scriptOwner != null) _selectedCinematicScriptOwnerId = scriptOwner.Id;
                Renderer.FrameSelected();
            }
            else if (eventOwner == null)
            {
                ImGui.BulletText(label);
            }

            string description = CinematicEngineFunctionCatalog.Describe(command.Call);
            if (!string.IsNullOrWhiteSpace(description)) ImGui.TextWrapped($"  {description}");
        }

        if (commands.Length > 64)
            ImGui.TextDisabled($"Showing 64 of {commands.Length} matching commands.");
    }
}
