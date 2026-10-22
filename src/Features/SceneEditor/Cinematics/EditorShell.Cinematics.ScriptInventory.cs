using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptInventoryWithoutPlayback()
    {
        if (Document == null || _cinematicWorld == null) return;

        var eventTracks = Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks).ToArray();
        var owners = CinematicScriptScope.ForWorld(Document.CinematicScriptOwners.Values,
                _cinematicWorld.Key,
                _cinematicWorld.Actors.Select(actor => actor.Root.GaoKey)
                    .Concat(_cinematicWorld.Objects.Select(sceneObject => sceneObject.GaoKey)),
                eventTracks)
            .OrderBy(owner => owner.WorldKey != _cinematicWorld.Key)
            .ThenBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DrawCategoryTitle($"Static script analysis ({owners.Length} owners)");
        if (owners.Length == 0)
        {
            ImGui.TextDisabled("No script owners were indexed for this cinematic world.");
            return;
        }

        if (_selectedCinematicScriptOwnerId is not { } selectedId
            || !owners.Any(owner => owner.Id == selectedId))
            _selectedCinematicScriptOwnerId = owners[0].Id;

        ImGui.BeginChild("CinematicStaticScriptOwners", new Vector2(0, 132), ImGuiChildFlags.Borders);
        foreach (var owner in owners)
        {
            string sourceWorld = Document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name
                ?? $"world {owner.WorldKey:X8}";
            string relation = owner.WorldKey == _cinematicWorld.Key
                ? "[world owner]"
                : $"[linked owner · {sourceWorld}]";
            if (ImGui.Selectable($"{relation} {owner.Name} · {owner.OwnerKey:X8}##static-script{owner.WorldKey:X8}{owner.OwnerKey:X8}",
                owner.Id == _selectedCinematicScriptOwnerId))
                _selectedCinematicScriptOwnerId = owner.Id;
            DrawCopyNameContextMenu($"StaticCinematicScriptName##{owner.WorldKey:X8}{owner.OwnerKey:X8}", owner.Name);
        }
        ImGui.EndChild();

        var selectedOwner = owners.FirstOrDefault(owner => owner.Id == _selectedCinematicScriptOwnerId);
        if (selectedOwner == null) return;

        ImGui.TextWrapped($"Owner {selectedOwner.OwnerKey:X8} · brain {selectedOwner.BrainKey:X8} · model {selectedOwner.ScriptModelKey:X8}");
        ImGui.TextWrapped($"Events: {FormatScriptKey(selectedOwner.EventsKey)} · Scene events: {FormatScriptKey(selectedOwner.SceneEventsKey)}");
        ImGui.TextWrapped("This view reports serialized owner data only. Without a linked playback clip, it cannot claim that these scripts execute during a particular moment.");

        string[] behavior = CinematicScriptBehaviorAnalyzer.Analyze(selectedOwner,
            Document.CinematicScriptOwners.Values);
        if (behavior.Length > 0)
        {
            DrawCategoryTitle("Static trigger analysis");
            foreach (string finding in behavior) ImGui.TextWrapped(finding);
            ImGui.TextDisabled("Findings come from serialized OVA/event data and the shared Iris implementation; live game state is not evaluated.");
        }

        if (selectedOwner.CallbackSlots is { } callbackSlots)
        {
            DrawCategoryTitle($"Script-model callback slots ({callbackSlots.Length})");
            var configuredRange = CinematicScriptCallbackCoverage.ResolveConfiguredRange(callbackSlots);
            ImGui.TextDisabled(configuredRange is { } range
                ? $"Iris scans configured callback span {range.FirstSlot}–{range.LastSlot} in ascending slot order; empty gaps are skipped."
                : "No callback slots are configured on this model.");
            foreach (var slot in callbackSlots)
            {
                string functionName = slot.FunctionKey == 0 ? "empty"
                    : slot.FunctionName is { } name
                        ? CinematicScriptReader.DescribeCallbackName(name)
                        : $"unmapped function {slot.FunctionKey:X8}";
                ImGui.BulletText($"Slot {slot.Index:D2}: {functionName}");
            }
            ImGui.TextDisabled("Configured callbacks may still be skipped at runtime by callback timers, AI table membership, distance, process flags, and other live state.");
        }
        else
        {
            ImGui.TextDisabled("Callback-slot data was not decoded for this script owner.");
        }

        DrawCinematicScriptSchedulerEvidence(selectedOwner);
        DrawCinematicScriptEventTracks(selectedOwner);
        DrawCinematicScriptEventTrackCalls(selectedOwner);
        DrawCinematicScriptActionKitMerges(selectedOwner);
        DrawCinematicScriptReferencedObjectCalls(selectedOwner);
        DrawCinematicScriptCallPaths(selectedOwner);
        DrawCinematicScriptVariables(selectedOwner);
    }
}
