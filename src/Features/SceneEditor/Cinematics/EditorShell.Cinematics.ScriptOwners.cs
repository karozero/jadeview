using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptOwnerList(CinematicScriptOwner[] owners, uint cinematicWorldKey,
        uint? clipKey)
    {
        if (clipKey == null && _cinematicScriptOwnerCategory == CinematicScriptOwnerCategory.ClipLinked)
            _cinematicScriptOwnerCategory = CinematicScriptOwnerCategory.All;
        ImGui.TextDisabled("Owner groups");
        if (!ImGui.BeginTabBar("CinematicScriptOwnerCategories")) return;
        if (clipKey is { } selectedClip)
            DrawCinematicScriptOwnerTab(owners, cinematicWorldKey, selectedClip,
                CinematicScriptOwnerCategory.ClipLinked, "Clip-linked");
        DrawCinematicScriptOwnerTab(owners, cinematicWorldKey, clipKey,
            CinematicScriptOwnerCategory.CinematicWorld, "Scene owners");
        DrawCinematicScriptOwnerTab(owners, cinematicWorldKey, clipKey,
            CinematicScriptOwnerCategory.ReferencedWorld, "Linked worlds");
        DrawCinematicScriptOwnerTab(owners, cinematicWorldKey, clipKey,
            CinematicScriptOwnerCategory.All, "All");
        ImGui.EndTabBar();
    }

    private void DrawCinematicScriptOwnerTab(CinematicScriptOwner[] owners, uint cinematicWorldKey,
        uint? clipKey, CinematicScriptOwnerCategory category, string label)
    {
        CinematicScriptOwner[] visibleOwners = owners
            .Where(owner => MatchesCinematicScriptOwnerCategory(owner, category, cinematicWorldKey, clipKey))
            .Where(MatchesCinematicOwnerSearch)
            .ToArray();
        ImGuiTabItemFlags flags = _cinematicScriptOwnerCategory == category
            ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!BeginTabItemWithFlags($"{label} ({visibleOwners.Length})", flags)) return;
        _cinematicScriptOwnerCategory = category;
        if (visibleOwners.Length == 0)
        {
            ImGui.TextDisabled("No script owners match this category and search.");
        }
        else
        {
            float availableHeight = ImGui.GetContentRegionAvail().Y;
            float ownerListHeight = Math.Clamp(availableHeight * .24f, 88 * _scale, 176 * _scale);
            ImGui.BeginChild($"CinematicScriptOwnerList{category}", new Vector2(0, ownerListHeight), ImGuiChildFlags.Borders);
            foreach (var owner in visibleOwners)
            {
                string sourceWorldName = Document?.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name
                    ?? $"world {owner.WorldKey:X8}";
                string relation = owner.WorldKey != cinematicWorldKey
                    ? $"[linked · {sourceWorldName}]"
                    : clipKey is { } key && IsLinkedToClip(owner, cinematicWorldKey, key)
                        ? "[clip-linked]" : "[scene owner]";
                if (ImGui.Selectable($"{relation} {owner.Name} · {owner.OwnerKey:X8}##script{owner.WorldKey:X8}{owner.OwnerKey:X8}",
                    owner.Id == _selectedCinematicScriptOwnerId))
                    _selectedCinematicScriptOwnerId = owner.Id;
                DrawCopyNameContextMenu($"CinematicScriptName##{owner.WorldKey:X8}{owner.OwnerKey:X8}", owner.Name);
            }
            ImGui.EndChild();
        }
        ImGui.EndTabItem();
    }

    private bool MatchesCinematicScriptOwnerCategory(CinematicScriptOwner owner,
        CinematicScriptOwnerCategory category, uint cinematicWorldKey, uint? clipKey)
    {
        bool belongsToCinematicWorld = owner.WorldKey == cinematicWorldKey;
        bool isClipLinked = clipKey is { } key && IsLinkedToClip(owner, cinematicWorldKey, key);
        return category switch
        {
            CinematicScriptOwnerCategory.ClipLinked => isClipLinked,
            CinematicScriptOwnerCategory.CinematicWorld => belongsToCinematicWorld && !isClipLinked,
            CinematicScriptOwnerCategory.ReferencedWorld => !belongsToCinematicWorld,
            _ => true
        };
    }

    private bool MatchesCinematicOwnerSearch(CinematicScriptOwner owner)
    {
        string filter = _cinematicEveSearch.Trim();
        if (filter.Length == 0) return true;
        return owner.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || owner.OwnerKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || owner.BrainKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || owner.ScriptModelKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || owner.EventsKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || owner.SceneEventsKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || MatchesOwnedEventTrackSearch(owner, filter);
    }

    private bool MatchesOwnedEventTrackSearch(CinematicScriptOwner owner, string filter)
    {
        if (Document == null) return false;
        return Document.CinematicEmbeddedEventTracks
            .Where(entry => entry.Key.WorldKey == owner.WorldKey && entry.Key.OwnerKey == owner.OwnerKey)
            .SelectMany(entry => entry.Value)
            .Any(list => MatchesCinematicEventSearch(list, filter, list.WorldKey));
    }

    private void DrawCinematicScriptInventoryWithoutPlayback()
    {
        if (Document == null || _cinematicWorld == null) return;

        DrawArchiveStartupScriptInventory();

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

        DrawCinematicScriptOwnerList(owners, _cinematicWorld.Key, null);

        var selectedOwner = owners.FirstOrDefault(owner => owner.Id == _selectedCinematicScriptOwnerId);
        if (selectedOwner == null) return;

        ImGui.TextWrapped($"Owner {selectedOwner.OwnerKey:X8} · brain {selectedOwner.BrainKey:X8} · model {selectedOwner.ScriptModelKey:X8}");
        ImGui.TextWrapped($"Events: {FormatScriptKey(selectedOwner.EventsKey)} · Scene events: {FormatScriptKey(selectedOwner.SceneEventsKey)}");
        ImGui.TextWrapped("This view reports serialized owner data only. Without a linked playback clip, it cannot claim that these scripts execute during a particular moment.");

        if (ImGui.CollapsingHeader("Trigger and activation evidence", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string[] behavior = CinematicScriptBehaviorAnalyzer.Analyze(selectedOwner,
                Document.CinematicScriptOwners.Values);
            if (behavior.Length > 0)
            {
                DrawCategoryTitle("Static trigger analysis");
                foreach (string finding in behavior) ImGui.TextWrapped(finding);
                ImGui.TextDisabled("Findings come from serialized OVA/event data and the shared Iris implementation; live game state is not evaluated.");
            }
        }

        if (ImGui.CollapsingHeader("AI update tracks"))
        {
            if (selectedOwner.CallbackSlots is { } callbackSlots)
            {
                DrawCategoryTitle($"AI update tracks · model defaults ({callbackSlots.Length})");
                ImGui.TextDisabled("Iris loads up to five callback functions as mutable AI tracks. The world AI pass advances due tracks; these are not clip timestamps or engine-event hooks.");
                var configuredRange = CinematicScriptCallbackCoverage.ResolveConfiguredRange(callbackSlots);
                ImGui.TextDisabled(configuredRange is { } range
                    ? $"Configured scheduler span: tracks {range.FirstSlot}–{range.LastSlot}, visited in ascending order; empty gaps are skipped."
                    : "No AI update tracks are configured on this model.");
                var functionResources = selectedOwner.FunctionResources.ToDictionary(resource => resource.FunctionKey);
                foreach (var slot in callbackSlots)
                {
                    string functionName = slot.FunctionKey == 0 ? "empty"
                        : slot.FunctionName is { } name
                            ? CinematicScriptReader.DescribeCallbackName(name)
                            : $"unmapped function {slot.FunctionKey:X8}";
                    string resourceState = slot.FunctionKey == 0 ? ""
                        : functionResources.TryGetValue(slot.FunctionKey, out var functionResource)
                            ? $" · .ofc {functionResource.Status}"
                            : " · no matching .ofc reference in this model";
                    ImGui.BulletText($"Track {slot.Index:D2}: {functionName}{resourceState}");
                }
                DrawCinematicScriptAiTrackMutationCandidates(selectedOwner);

            }
            else
            {
                ImGui.TextDisabled("Callback-slot data was not decoded for this script owner.");
            }
        }

        DrawCinematicScriptFunctionResources(selectedOwner);

        if (ImGui.CollapsingHeader("EVE tracks and action links", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawCinematicScriptEventTracks(selectedOwner);
            DrawCinematicScriptEventTrackCalls(selectedOwner);
            DrawCinematicScriptActionKitMerges(selectedOwner);
        }

        if (ImGui.CollapsingHeader("Scheduler, calls and variables"))
        {
            DrawCinematicScriptSchedulerEvidence(selectedOwner);
            DrawCinematicEventSchedulerEvidence(selectedOwner);
            DrawCinematicScriptReferencedObjectCalls(selectedOwner);
            DrawCinematicScriptCallPaths(selectedOwner);
            DrawCinematicScriptVariables(selectedOwner);
        }
    }
}
