using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptInspector()
    {
        if (_cinematicPlayback == null || Document == null) return;
        DrawArchiveStartupScriptInventory();
        var sceneScripts = CinematicScriptScope.ForClip(Document.CinematicScriptOwners.Values,
                _cinematicPlayback.WorldKey,
                _cinematicPlayback.Actors.Select(actor => actor.Root.GaoKey),
                _cinematicPlayback.ScriptCalls.Select(item => item.Call.Target)
                    .Concat(_cinematicPlayback.ScriptTrackTargets),
                _cinematicPlayback.ScriptMessages.Select(item => item.Message),
                _selectedCinematicScriptOwnerId,
                Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks))
            .OrderByDescending(owner => IsLinkedToClip(owner, _cinematicPlayback.WorldKey, _cinematicPlayback.Key))
            .ThenByDescending(owner => owner.WorldKey == _cinematicPlayback.WorldKey)
            .ThenBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int crossWorldOwnerCount = sceneScripts.Count(owner => owner.WorldKey != _cinematicPlayback.WorldKey);
        DrawCategoryTitle($"Script owners and event links ({sceneScripts.Length})");
        if (crossWorldOwnerCount > 0)
            ImGui.TextDisabled($"Includes {crossWorldOwnerCount} linked script owner(s) from referenced source worlds.");
        DrawUnmappedCinematicCallbacks(sceneScripts);
        if (sceneScripts.Length == 0)
        {
            ImGui.TextDisabled("No script owners were found in this cinematic world.");
            DrawCinematicTrackCalls();
            return;
        }

        if (_selectedCinematicScriptOwnerId is not { } selectedOwnerId
            || !sceneScripts.Any(owner => owner.Id == selectedOwnerId))
            _selectedCinematicScriptOwnerId = sceneScripts.FirstOrDefault(owner =>
                IsLinkedToClip(owner, _cinematicPlayback.WorldKey, _cinematicPlayback.Key))?.Id
                ?? sceneScripts[0].Id;

        DrawCinematicScriptOwnerList(sceneScripts, _cinematicPlayback.WorldKey, _cinematicPlayback.Key);

        var selected = sceneScripts.FirstOrDefault(owner => owner.Id == _selectedCinematicScriptOwnerId);
        if (selected == null)
        {
            DrawCinematicTrackCalls();
            return;
        }
        ImGui.TextWrapped($"Owner {selected.OwnerKey:X8} · brain {selected.BrainKey:X8} · model {selected.ScriptModelKey:X8}");
        ImGui.TextWrapped($"Events: {FormatScriptKey(selected.EventsKey)} · Scene events: {FormatScriptKey(selected.SceneEventsKey)}");
        if (selected.WorldKey != _cinematicPlayback.WorldKey)
        {
            string sourceWorldName = Document.Worlds.FirstOrDefault(world => world.Key == selected.WorldKey)?.Name
                ?? $"world {selected.WorldKey:X8}";
            ImGui.TextWrapped($"This owner is linked to the cinematic through serialized OVA/event data, actor references, track targets, or a resolved Iris source-code object reference. Its source record is stored in {sourceWorldName}; its brain callbacks and linked event data can be inspected here. Live activation and state are not evaluated.");
        }
        else
        {
            ImGui.TextWrapped(IsLinkedToClip(selected, _cinematicPlayback.WorldKey, _cinematicPlayback.Key)
                ? "This script owner directly references the selected event track. Its runtime conditions and current active state are not evaluated."
                : "This script owner belongs to the cinematic world. The archive does not prove that it runs during the selected track.");
        }
        DrawCinematicOwnerStartLifecycle(selected);
        if (ImGui.CollapsingHeader("Activation and start gates", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string[] behavior = CinematicScriptBehaviorAnalyzer.Analyze(selected,
                Document.CinematicScriptOwners.Values, _cinematicWorld?.Objects);
            if (behavior.Length > 0)
            {
                DrawCategoryTitle("Static trigger analysis");
                foreach (string finding in behavior) ImGui.TextWrapped(finding);
                ImGui.TextDisabled("Inferred from the shared Iris callback implementation and serialized OVA values; save-game state is not loaded.");
            }
            DrawCinematicOwnerInterruptionGateEvidence(selected, selected.Id);
            DrawCinematicMessageGateCandidates(selected);
            DrawCinematicScriptFlagLinks(selected);
            DrawCinematicTimelineFlagGates(selected);
        }

        if (ImGui.CollapsingHeader("EVE tracks and actions"))
        {
            DrawCinematicScriptActionKitMerges(selected);
            DrawCinematicScriptEventTracks(selected);
            DrawCinematicScriptEventTrackCalls(selected);
            DrawCinematicExternalOwnerEventTracks(selected);
            DrawCinematicActorActions(selected);
        }

        if (ImGui.CollapsingHeader("Callbacks and call paths"))
        {
            if (selected.ModelFunctionNames.Length > 0)
            {
                DrawCategoryTitle($"Script-model function references ({selected.ModelFunctionNames.Length})");
                int visibleFunctionCount = Math.Min(selected.ModelFunctionNames.Length, 40);
                for (int functionIndex = 0; functionIndex < visibleFunctionCount; functionIndex++)
                    ImGui.BulletText(CinematicScriptReader.DescribeCallbackName(
                        selected.ModelFunctionNames[functionIndex]));
                if (selected.ModelFunctionNames.Length > visibleFunctionCount)
                    ImGui.TextDisabled($"Showing {visibleFunctionCount} of {selected.ModelFunctionNames.Length} model references.");
                if (selected.ModelFunctionNames.Contains("sc_dummy", StringComparer.Ordinal))
                    ImGui.TextDisabled("sc_dummy is a placeholder function in the supplied Iris source, not a recovered gameplay implementation.");
                ImGui.TextDisabled("References found in the script-model resource; they are not callback-slot order or proof that the owner calls them.");
            }
            if (selected.CallbackSlots is { } callbackSlots)
            {
                DrawCategoryTitle($"AI update tracks · model defaults ({callbackSlots.Length})");
                ImGui.TextDisabled("These five possible AI track functions are loaded from the resolved script model OVA. Iris overlays instance OVA values by variable name; it does not replace the model track list. The world AI pass advances these mutable tracks independently of EVE timelines and engine-event hooks.");
                CinematicCallbackSlotRange? configuredRange =
                    CinematicScriptCallbackCoverage.ResolveConfiguredRange(callbackSlots);
                ImGui.TextDisabled(configuredRange is { } range
                    ? $"Configured AI scheduler span: tracks {range.FirstSlot}–{range.LastSlot}, visited in ascending order; empty gaps are skipped."
                    : "No AI update tracks are configured on this model, so it has no model track span to run.");
                foreach (var slot in callbackSlots)
                {
                    string functionDescription = slot.FunctionKey == 0 ? "empty"
                        : slot.FunctionName is { } functionName
                            ? CinematicScriptReader.DescribeCallbackName(functionName)
                            : $"unmapped function {slot.FunctionKey:X8}";
                    ImGui.BulletText($"Track {slot.Index:D2}: {functionDescription}");
                }
                if (callbackSlots.Any(slot => slot.FunctionKey != 0 && slot.FunctionName == null))
                    ImGui.TextDisabled("Unmapped track keys are shown as raw IDs because the bundled Iris function table has no matching name; their behavior is unknown.");
                ImGui.TextDisabled("Scripts can replace or stop tracks. World-table membership, distance, process flags and live timers also determine whether a track runs; JadeView does not observe that runtime state.");
            }
            else if (selected.ModelFunctionNames.Length == 0)
            {
                ImGui.TextDisabled(CinematicScriptReader.HasCallbackCatalog(selected.ScriptModelKey)
                    ? "The bundled Iris catalog contains this script model, but no callback slots were referenced."
                    : "This script model key is absent from the bundled Iris/archive callback catalog.");
            }
            else
            {
                ImGui.TextDisabled("Owner callback-slot OVA data was not decoded; model references do not establish callback slot assignments.");
            }

            DrawCinematicEngineCallbackAssignments(selected);

            DrawCinematicScriptSchedulerEvidence(selected);
            DrawCinematicEventSchedulerEvidence(selected);
            DrawCinematicScriptReferencedObjectCalls(selected);
            DrawCinematicScriptCallPaths(selected);
            DrawCinematicScriptIncomingReferences(selected, sceneScripts);
            DrawCinematicScriptVariables(selected);
        }

        if (ImGui.CollapsingHeader("Dialogue, audio and render bindings"))
        {
            DrawCinematicScriptAudioBindings(selected);
            DrawCinematicScriptRenderBindings(selected);
            var dialogueLines = selected.DialogueLines ?? [];
            if (dialogueLines.Length > 0)
            {
                DrawCategoryTitle($"Dialogue line metadata ({dialogueLines.Length} slots)");
                var configuredLines = dialogueLines.Where(line => line.TextBankKey != 0 || line.TextId != 0
                    || line.SoundId is > 0).Take(32).ToArray();
                foreach (var line in configuredLines)
                    ImGui.BulletText(DescribeDialogueLine(line));
                if (configuredLines.Length == 32 && dialogueLines.Count(line => line.TextBankKey != 0
                        || line.TextId != 0 || line.SoundId is > 0) > configuredLines.Length)
                    ImGui.TextDisabled("Additional configured dialogue slots are omitted.");
                ImGui.TextDisabled("Text, sound and spatial metadata come from per-line OVA arrays. Localized strings, live audio state and facial playback are not resolved here.");
            }
        }

        if (selected.ObjectReferences.Length > 0
            && ImGui.CollapsingHeader($"Object references ({selected.ObjectReferences.Length})"))
        {
            foreach (var reference in selected.ObjectReferences.Take(24))
            {
                string target = reference.TargetName ?? "unresolved object / actor slot";
                string variable = reference.ArrayIndex is { } arrayIndex
                    ? $"{reference.VariableName}[{arrayIndex}]" : reference.VariableName;
                string label = $"{reference.Source}: {variable} → {target} ({reference.TargetKey:X8})";
                var targetObject = _cinematicWorld?.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == reference.TargetKey);
                if (targetObject != null && ImGui.Selectable($"{label}##ref{reference.Source}{reference.VariableName}{reference.TargetKey:X8}"))
                {
                    SelectedObject = targetObject;
                    Renderer.FrameSelected();
                }
                else if (targetObject == null)
                {
                    ImGui.BulletText(label);
                }
            }
            if (selected.ObjectReferences.Length > 24)
                ImGui.TextDisabled($"{selected.ObjectReferences.Length - 24} more references omitted from this view.");
        }

        if (ImGui.CollapsingHeader("Clip-level calls and messages"))
            DrawCinematicTrackCalls();
    }

    private void DrawCinematicScriptFlagLinks(CinematicScriptOwner owner)
    {
        if (Document == null) return;
        var accesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner);
        var eventTracks = Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks).ToArray();

        if (!ReferenceEquals(_cinematicFlagFlowDocument, Document)
            || _cinematicFlagFlowOwnerCount != Document.CinematicScriptOwners.Count
            || _cinematicFlagFlowTrackCount != eventTracks.Length)
        {
            _cinematicFlagFlowLinks = CinematicScriptFlagFlowAnalyzer.BuildLinkGraph(
                Document.CinematicScriptOwners.Values, eventTracks);
            _cinematicFlagFlowDocument = Document;
            _cinematicFlagFlowOwnerCount = Document.CinematicScriptOwners.Count;
            _cinematicFlagFlowTrackCount = eventTracks.Length;
        }
        var links = _cinematicFlagFlowLinks.GetValueOrDefault(owner.Id, []);
        if (accesses.Length == 0 && links.Length == 0) return;
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader($"Saved flag / event links ({links.Length})##FlagEventLinksV2")) return;
        ImGui.TextDisabled("Static links from callback/OVA predicates to literal event-track flag commands; execution order is not simulated.");
        foreach (var access in accesses.Take(24))
        {
            string state = access.RequiredSet is { } requiredSet
                ? $" · requires {(requiredSet ? "set" : "clear")}"
                : access.WrittenSet is { } writtenSet
                    ? $" · writes {(writtenSet ? "set" : "clear")}"
                    : string.Empty;
            ImGui.BulletText($"Flag {access.Flag} · {access.Access} · {access.VariableName}{state}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(access.Description);
        }
        if (accesses.Length > 24)
            ImGui.TextDisabled($"Showing 24 of {accesses.Length} callback flag accesses.");
        if (links.Length == 0)
        {
            ImGui.TextDisabled("No related owner or literal event flag command was found among currently loaded resources.");
            return;
        }

        foreach (var link in links.Take(32))
        {
            var relatedOwner = link.RelatedOwner;
            string worldName = Document.Worlds.FirstOrDefault(world => world.Key == relatedOwner.WorldKey)?.Name
                ?? $"world {relatedOwner.WorldKey:X8}";
            string label = $"Flag {link.Flag} · {relatedOwner.Name} · {worldName} · {link.Direction}";
            if (ImGui.Selectable($"{label}##flaglink{relatedOwner.WorldKey:X8}{relatedOwner.OwnerKey:X8}{link.Flag}{link.Direction}"))
                _selectedCinematicScriptOwnerId = relatedOwner.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(link.Direction);
            DrawCopyNameContextMenu($"CinematicScriptFlagName##{relatedOwner.WorldKey:X8}{relatedOwner.OwnerKey:X8}{link.Flag}",
                relatedOwner.Name);
        }
        if (links.Length > 32)
            ImGui.TextDisabled($"Showing 32 of {links.Length} matching links.");
        ImGui.TextDisabled("Callback accesses combine Iris source calls with decoded OVA values; links also include literal event commands. They do not prove that a callback or track ran, or that the live condition passed.");
    }

    private void DrawCinematicTimelineFlagGates(CinematicScriptOwner owner)
    {
        if (_cinematicPlayback == null) return;
        var externalStateCalls = CinematicTimelineFlagAnalyzer.ExternalTimelineCalls(owner,
            _cinematicPlayback.Key, _cinematicPlayback.ScriptStateCalls);
        var gates = CinematicTimelineFlagAnalyzer.Analyze(owner,
            externalStateCalls, _cinematicPlayback.Time);
        if (gates.Length == 0) return;

        DrawCategoryTitle("Saved-flag gates at playhead");
        ImGui.TextDisabled(externalStateCalls == null
            ? "The selected clip is this owner's SceneEvents resource, so its own writes are excluded from predicting its start gates. Prior save/runtime flag state remains unknown."
            : "State is inferred from literal writes in the selected clip and its resolved SceneEvents routes. Flags without a prior write remain unknown; conditional callbacks, other worlds and live save state are not evaluated.");
        foreach (var gate in gates)
        {
            string required = gate.RequiredSet ? "set" : "clear";
            string state = gate.CurrentSet switch
            {
                true => "set by resolved timeline writes",
                false => "clear by resolved timeline writes",
                _ => gate.NextWriteTime is { } nextTime
                    ? $"unknown so far; next resolved write at {nextTime:F2}s"
                    : "unknown (no resolved write observed)"
            };
            string result = gate.Satisfied switch
            {
                true => "predicate matches",
                false => "predicate does not match",
                _ => "predicate cannot be evaluated yet"
            };
            string lastWrite = gate.LastWriteTime is { } writeTime
                ? $"; last write {writeTime:F2}s"
                : "";
            ImGui.BulletText($"Flag {gate.Flag} · requires {required} · {state}{lastWrite} · {result}");
            if (gate.CurrentSet == null)
                DrawPotentialSavedFlagWriters(owner, gate.Flag);
        }
    }

    private void DrawPotentialSavedFlagWriters(CinematicScriptOwner owner, int flag)
    {
        var candidates = CinematicScriptFlagFlowAnalyzer.PotentialWritersForFlag(
            _cinematicFlagFlowLinks.GetValueOrDefault(owner.Id, []), flag);
        if (candidates.Length == 0) return;

        ImGui.Indent();
        ImGui.TextDisabled($"Possible writers among loaded script owners ({candidates.Length}); not time-resolved:");
        foreach (var candidate in candidates.Take(4))
        {
            var relatedOwner = candidate.RelatedOwner;
            string worldName = Document?.Worlds.FirstOrDefault(world => world.Key == relatedOwner.WorldKey)?.Name
                ?? $"world {relatedOwner.WorldKey:X8}";
            if (ImGui.Selectable($"{relatedOwner.Name} · {worldName}##flagwriter{owner.OwnerKey:X8}{flag}{relatedOwner.OwnerKey:X8}"))
                _selectedCinematicScriptOwnerId = relatedOwner.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(candidate.Direction);
        }
        if (candidates.Length > 4)
            ImGui.TextDisabled($"Showing 4 of {candidates.Length} possible writer owners.");
        ImGui.Unindent();
    }

    private void DrawCinematicActorActions(CinematicScriptOwner owner)
    {
        if (Document == null) return;
        var actionKit = CinematicActionKitScopeResolver.Resolve(owner.OwnerKey, owner.WorldKey,
            Document.CinematicActionKitsByOwner);
        if (!actionKit.IsResolved) return;
        var actions = actionKit.Actions!;

        if (actionKit.SourceWorldKey != owner.WorldKey)
            ImGui.TextDisabled($"Action table resolved from source world {actionKit.SourceWorldKey!.Value:X8}.");
        if (actions.Count == 0)
            return;

        if (!ImGui.CollapsingHeader($"Actor action table ({actions.Count} slots)")) return;
        ImGui.TextDisabled("Action slots come from this actor's skeleton table. They are available gameplay clips, not proof of the action active in the game at this time.");
        int[] baseEventSlots = CinematicActorActionLinkResolver.FindBaseEventActionSlots(owner, actions);
        if (baseEventSlots.Length > 0)
            ImGui.TextWrapped($"Base Events {owner.EventsKey:X8} also appears in action slot(s): {string.Join(", ", baseEventSlots)}. This is a serialized resource match, not evidence that those slots were selected.");
        else if (owner.EventsKey is > 2 and < uint.MaxValue)
            ImGui.TextDisabled($"Base Events {owner.EventsKey:X8} does not match a clip in this actor's decoded action table.");
        ImGui.BeginChild("CinematicActorActionSlots", new Vector2(0, 150), ImGuiChildFlags.Borders);
        foreach (var (slot, action) in actions.OrderBy(pair => pair.Key).Take(48))
        {
            string items = string.Join(", ", action.Items.Select(item =>
                $"{item.Clip.Key:X8} (shape {FormatScriptKey(item.ShapeKey)})"));
            ImGui.BulletText($"Action {slot}: {items}");
        }
        if (actions.Count > 48)
            ImGui.TextDisabled($"Showing 48 of {actions.Count} action slots.");
        ImGui.EndChild();
        DrawCinematicScriptActionRequests(owner, actions);
    }
}
