using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicInspector()
    {
        DrawPanelTitle("CINEMATIC TIMELINE", "Select a shared source clip to evaluate matching actors and camera at one timestamp.");
        if (_cinematicWorld == null)
        {
            ImGui.TextDisabled("Choose a cinematic from the Cutscenes browser below.");
            return;
        }

        DrawCategoryTitle("Source");
        ImGui.TextWrapped($"{_cinematicWorld.Name}\n{_cinematicWorld.Actors.Count} actor rigs · {_cinematicWorld.Objects.Count} objects");
        DrawCopyNameContextMenu("CinematicSourceName", _cinematicWorld.Name);
        DrawCinematicEventSearch();
        DrawCategoryTitle("Shared animation tracks");
        if (_cinematicPlayers.Length == 0)
        {
            ImGui.TextWrapped("No shared cinematic animation clip was linked to multiple loaded actor rigs. Playback is unavailable, but static script records can still be inspected.");
            DrawCinematicScriptInventoryWithoutPlayback();
            return;
        }

        SelectCinematicClip();
        if (_cinematicPlayback is { EventTrackTransformCount: > 0 } playbackWithEventTracks)
            ImGui.TextWrapped($"{playbackWithEventTracks.EventTrackTransformCount} script-linked EVE transform channels are overlaid from the clip start. Callback gates and runtime activation are not simulated.");
        DrawPlaybackControls();
        DrawCinematicAudioControls();
        DrawCategoryTitle("Scene state");
        ImGui.Checkbox("Show hidden / conditional objects (debug)", ref Renderer.ShowGameHidden);
        DrawCallbackSceneEventOwnerControls();
        DrawManualSceneEventOwnerControls();
        DrawCinematicScreenFadeStatus();
        DrawCinematicCameraControls();
        DrawParticipants();
        DrawCinematicScriptInspector();
        DrawCinematicSelectedObjectEventCommands();
        DrawCinematicEmbeddedEventTrackInventory();
    }

    private void DrawCallbackSceneEventOwnerControls()
    {
        if (Document == null || _cinematicPlayback is not { CallbackSceneEventOwners.Length: > 0 } playback)
            return;

        DrawCategoryTitle("Callback-linked EVE channels");
        ImGui.TextWrapped("These source-reachable callback owners start this clip's SceneEvents list. Select an owner to inspect its callback; disable it to omit linked tracks from preview and GLB export. Gate inputs are summarized here, but the preview does not execute the runtime tests.");
        foreach (var ownerId in playback.CallbackSceneEventOwners)
        {
            Document.CinematicScriptOwners.TryGetValue(ownerId, out var owner);
            string ownerName = owner?.Name ?? "Unknown callback owner";
            string ownerLabel = $"{ownerName} · {ownerId.OwnerKey:X8}";
            if (ImGui.Selectable($"{ownerLabel}##callback-scene-owner-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}",
                    _selectedCinematicScriptOwnerId == ownerId))
                _selectedCinematicScriptOwnerId = ownerId;
            if (owner != null)
                DrawSceneEventOwnerGateEvidence(owner, ownerId);
            ImGui.SameLine();
            bool enabled = !_disabledCinematicCallbackOwners.Contains(ownerId);
            if (!ImGui.Checkbox($"Include##callback-scene-events-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}",
                    ref enabled))
                continue;

            if (enabled) _disabledCinematicCallbackOwners.Remove(ownerId);
            else _disabledCinematicCallbackOwners.Add(ownerId);
            RebuildCinematicPlayersForScriptSelection(playback);
            return;
        }
    }

    private void DrawSceneEventOwnerGateEvidence(CinematicScriptOwner owner,
        CinematicScriptOwnerId ownerId)
    {
        string[] evidence = CinematicScriptBehaviorAnalyzer.AnalyzeStartGates(owner,
            _cinematicWorld?.Objects);
        if (evidence.Length == 0)
        {
            string[] dispatchEvidence = CinematicEventSchedulerAnalyzer.Analyze(owner);
            ImGui.TextDisabled(dispatchEvidence.Length > 1
                ? dispatchEvidence[1]
                : "No source-defined Cine_Events start gate is available for this owner.");
            return;
        }

        ImGui.TextDisabled(evidence[0]);
        if (evidence.Length <= 1
            || !ImGui.TreeNode($"Gate evidence ({evidence.Length - 1})##scene-event-gates-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"))
            return;

        foreach (string detail in evidence.Skip(1)) ImGui.BulletText(detail);
        ImGui.TreePop();
    }

    private void DrawManualSceneEventOwnerControls()
    {
        if (Document == null || _cinematicPlayback is not { PreviewableSceneEventOwners.Length: > 0 } playback)
            return;

        DrawCategoryTitle("Script-owned EVE preview");
        ImGui.TextWrapped("These owners have separate SceneEvents lists in the cinematic or paired gameplay world. Enable an owner to preview all its serialized channels from time zero; source callback gates are not executed. Enabled channels are included in synchronized export.");
        foreach (var ownerId in playback.PreviewableSceneEventOwners)
        {
            Document.CinematicScriptOwners.TryGetValue(ownerId, out var owner);
            string ownerName = owner?.Name ?? "Unknown script owner";
            string ownerLabel = $"{ownerName} · {ownerId.OwnerKey:X8}";
            if (ImGui.Selectable($"{ownerLabel}##script-eve-owner-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}",
                    _selectedCinematicScriptOwnerId == ownerId))
                _selectedCinematicScriptOwnerId = ownerId;
            if (owner != null)
                DrawSceneEventOwnerGateEvidence(owner, ownerId);
            ImGui.SameLine();
            bool enabled = _previewedCinematicSceneEventOwners.Contains(ownerId);
            if (!ImGui.Checkbox($"Preview##script-eve-preview-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}",
                    ref enabled))
                continue;

            if (enabled) _previewedCinematicSceneEventOwners.Add(ownerId);
            else _previewedCinematicSceneEventOwners.Remove(ownerId);
            RebuildCinematicPlayersForScriptSelection(playback);
            return;
        }
    }

    private void RebuildCinematicPlayersForScriptSelection(CinematicPlayback previousPlayback)
    {
        if (Document == null || _cinematicWorld == null) return;
        uint clipKey = previousPlayback.Key;
        float playhead = previousPlayback.Time;
        bool resumePlayback = previousPlayback.Playing;
        int cameraIndex = previousPlayback.CameraIndex;
        previousPlayback.Deactivate();

        _cinematicPlayers = CinematicPlayback.Discover(Document, _cinematicWorld, Resolver, Archive?.Entries,
            _disabledCinematicCallbackOwners, _previewedCinematicSceneEventOwners);
        _cinematicPlayback = _cinematicPlayers.FirstOrDefault(candidate => candidate.Key == clipKey)
            ?? _cinematicPlayers.FirstOrDefault();
        if (_cinematicPlayback == null) return;

        _cinematicPlayback.CameraIndex = cameraIndex;
        _cinematicPlayback.Activate();
        _cinematicPlayback.Seek(playhead);
        if (resumePlayback) _cinematicPlayback.Play();
    }

    private void DrawCinematicScreenFadeStatus()
    {
        if (_cinematicPlayback?.ScreenFade is not { } fade) return;
        var clampedFade = Vector4.Clamp(fade, Vector4.Zero, Vector4.One);
        ImGui.TextDisabled($"Source screen fade active · RGB {clampedFade.X:F2}, {clampedFade.Y:F2}, {clampedFade.Z:F2} · opacity {clampedFade.W:P0}");
    }

    private void SelectCinematicClip()
    {
        int selectedIndex = Array.IndexOf(_cinematicPlayers, _cinematicPlayback);
        if (selectedIndex < 0) selectedIndex = 0;
        string[] clipNames = _cinematicPlayers.Select(player =>
            $"{player.Name} · {player.ParticipantCount} tracks · {player.Duration:F1}s").ToArray();
        ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.Combo("##CinematicClip", ref selectedIndex, clipNames, clipNames.Length);
        DrawCopyNameContextMenu("CinematicClipName", _cinematicPlayers[selectedIndex].Name);
        if (changed || _cinematicPlayback == null)
        {
            _cinematicPlayback?.Deactivate();
            ResetCinematicAudio();
            _cinematicVideoPostRollActive = false;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
            _cinematicPlayback = _cinematicPlayers[selectedIndex];
            _cinematicPlayback.Activate();
        }
        EnsureCinematicAudio();
    }

    private void DrawPlaybackControls()
    {
        DrawCategoryTitle("Playback");
        bool waitingForAudio = _cinematicAudio is { Enabled: true, IsLoading: true };
        ImGui.BeginDisabled(waitingForAudio && !_cinematicPlayback!.Playing);
        if (ImGui.Button(_cinematicPlayback!.Playing ? "Pause" : "Play"))
        {
            if (_cinematicPlayback.Playing) _cinematicPlayback.Pause();
            else _cinematicPlayback.Play();
            _cinematicVideoPostRollActive = false;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Restart"))
        {
            _cinematicPlayback.Restart();
            _cinematicVideoPostRollActive = false;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
        }
        ImGui.SameLine();
        if (ImGui.Button("Export synchronized scene")) OpenCinematicExport();

        float playhead = _cinematicPlayback.Time;
        ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.SliderFloat("##CinematicTime", ref playhead, 0,
            Math.Max(_cinematicPlayback.Duration, .001f),
            $"{_cinematicPlayback.Time:F2} / {_cinematicPlayback.Duration:F2} s");
        if (changed)
        {
            _cinematicPlayback.Pause();
            _cinematicPlayback.Seek(playhead);
            _cinematicVideoPostRollActive = false;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
        }
        if (_cinematicVideoPostRollActive)
            ImGui.TextDisabled($"Script-triggered video post-roll{(_cinematicVideoPostRollCompleted ? " complete" : " active")} · {_cinematicVideoPostRollSeconds:F2}s");
    }

    private void OpenCinematicExport()
    {
        _exportTarget = null;
        _exportOptions.Actors = true;
        _exportOptions.Animations = true;
        _exportOptions.AllClips = false;
        _exportOptions.ExperimentalMouthCycle = false;
        _openExport = true;
    }

    private void DrawParticipants()
    {
        DrawCategoryTitle("Participants");
        int participantIndex = 0;
        foreach (string participant in _cinematicPlayback!.ParticipantNames)
        {
            ImGui.BulletText(participant);
            DrawCopyNameContextMenu($"CinematicParticipantName##{participantIndex++}", participant);
        }
        DrawCategoryTitle("Body animation bindings");
        foreach (string diagnostic in _cinematicPlayback.BodyDiagnostics)
            ImGui.TextWrapped(diagnostic);
        DrawCategoryTitle("Facial animation bindings");
        foreach (string diagnostic in _cinematicPlayback.FacialDiagnostics)
            ImGui.TextWrapped(diagnostic);
        foreach (string diagnostic in _cinematicPlayback.TransformDiagnostics)
            ImGui.TextWrapped(diagnostic);
        ImGui.TextWrapped("Actors, camera, video and effects follow the application playback clock. Audio is streamed from the same source timeline; device-buffer timing is not used to drive scene updates. Mouth forms use authored controls; phoneme-level lip sync and the full script VM are not reconstructed.");
    }

    private void DrawCinematicScriptInspector()
    {
        if (_cinematicPlayback == null || Document == null) return;
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

        ImGui.BeginChild("CinematicScriptOwners", new Vector2(0, 132), ImGuiChildFlags.Borders);
        foreach (var owner in sceneScripts)
        {
            bool linked = IsLinkedToClip(owner, _cinematicPlayback.WorldKey, _cinematicPlayback.Key);
            string sourceWorldName = Document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name
                ?? $"world {owner.WorldKey:X8}";
            string relation = owner.WorldKey != _cinematicPlayback.WorldKey
                ? $"[linked owner · {sourceWorldName}]"
                : linked ? "[track-linked]" : "[world script]";
            if (ImGui.Selectable($"{relation} {owner.Name} · {owner.OwnerKey:X8}##script{owner.WorldKey:X8}{owner.OwnerKey:X8}",
                owner.Id == _selectedCinematicScriptOwnerId))
                _selectedCinematicScriptOwnerId = owner.Id;
            DrawCopyNameContextMenu($"CinematicScriptName##{owner.WorldKey:X8}{owner.OwnerKey:X8}", owner.Name);
        }
        ImGui.EndChild();

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
        string[] behavior = CinematicScriptBehaviorAnalyzer.Analyze(selected,
            Document.CinematicScriptOwners.Values, _cinematicWorld?.Objects);
        if (behavior.Length > 0)
        {
            DrawCategoryTitle("Static trigger analysis");
            foreach (string finding in behavior) ImGui.TextWrapped(finding);
            ImGui.TextDisabled("Inferred from the shared Iris callback implementation and serialized OVA values; save-game state is not loaded.");
        }
        DrawCinematicScriptAudioBindings(selected);
        DrawCinematicScriptActionKitMerges(selected);
        DrawCinematicMessageGateCandidates(selected);
        DrawCinematicScriptFlagLinks(selected);
        DrawCinematicTimelineFlagGates(selected);
        DrawCinematicScriptEventTracks(selected);
        DrawCinematicScriptEventTrackCalls(selected);
        DrawCinematicExternalOwnerEventTracks(selected);
        DrawCinematicActorActions(selected);
        DrawCinematicScriptRenderBindings(selected);
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
            DrawCategoryTitle($"Script-model callback slots ({callbackSlots.Length})");
            ImGui.TextDisabled("These five callbacks are loaded from the resolved script model's OVA. Iris overlays instance OVA values by variable name; it does not replace the model callback list.");
            CinematicCallbackSlotRange? configuredRange =
                CinematicScriptCallbackCoverage.ResolveConfiguredRange(callbackSlots);
            ImGui.TextDisabled(configuredRange is { } range
                ? $"Iris scans configured callback span {range.FirstSlot}–{range.LastSlot} in ascending slot order; empty gaps are skipped."
                : "No callback slots are configured on this model, so the AI instance has no callback span to run.");
            foreach (var slot in callbackSlots)
            {
                string functionDescription = slot.FunctionKey == 0 ? "empty"
                    : slot.FunctionName is { } functionName
                        ? CinematicScriptReader.DescribeCallbackName(functionName)
                        : $"unmapped function {slot.FunctionKey:X8}";
                ImGui.BulletText($"Slot {slot.Index:D2}: {functionDescription}");
            }
            if (callbackSlots.Any(slot => slot.FunctionKey != 0 && slot.FunctionName == null))
                ImGui.TextDisabled("Unmapped callback keys are shown as raw IDs because the bundled Iris function table has no matching name; their behavior is unknown.");
            ImGui.TextDisabled("The runtime also skips slots with a positive callback timer. World-table membership, distance, process flags and live timers determine whether a configured callback runs; JadeView does not observe those runtime gates.");
        }
        else if (selected.ModelFunctionNames.Length == 0)
        {
            ImGui.TextDisabled(CinematicScriptReader.HasCallbackCatalog(selected.ScriptModelKey)
                ? "The bundled Iris catalog contains this script model, but no callback slots were referenced."
                : "This script model key is absent from the bundled Iris/archive callback catalog.");
        }
        else
        {
            ImGui.TextDisabled("Owner callback-slot OVA data was not decoded; the model references above do not establish callback slot assignments.");
        }

        DrawCinematicScriptSchedulerEvidence(selected);
        DrawCinematicEventSchedulerEvidence(selected);
        DrawCinematicScriptReferencedObjectCalls(selected);
        DrawCinematicScriptCallPaths(selected);
        DrawCinematicScriptIncomingReferences(selected, sceneScripts);
        DrawCinematicScriptVariables(selected);

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

        DrawCategoryTitle($"Object references ({selected.ObjectReferences.Length})");
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
        var gates = CinematicTimelineFlagAnalyzer.Analyze(owner,
            _cinematicPlayback.ScriptStateCalls, _cinematicPlayback.Time);
        if (gates.Length == 0) return;

        DrawCategoryTitle("Saved-flag gates at playhead");
        ImGui.TextDisabled("State is inferred from literal writes in the selected clip and its resolved SceneEvents routes. Flags without a prior write remain unknown; conditional callbacks, other worlds and live save state are not evaluated.");
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
        }
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

    private void DrawCinematicScriptEventTracks(CinematicScriptOwner owner)
    {
        var document = Document;
        if (document == null || !document.CinematicScriptEventTracks.TryGetValue(owner.Id, out var tracks)
            || tracks.Length == 0) return;

        int commandCount = tracks.Sum(track => track.Calls.Length);
        int messageCount = tracks.Sum(track => track.Messages.Length);
        DrawCategoryTitle($"Linked event signals ({commandCount} commands, {messageCount} typed messages)");
        ImGui.TextDisabled("Events is the base actor animation/action reference; SceneEvents is the script EVE list used by TRA* commands. These records are decoded statically and are not proof of runtime execution.");
        foreach (var track in tracks)
        {
            var actionLinks = CinematicEventActionLinkResolver.Analyze(track,
                document.CinematicActionKitsByOwner);
            string linkRole = track.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase)
                ? "script EVE list" : "actor animation event resource";
            ImGui.TextWrapped($"{track.LinkName} ({linkRole}) · {track.Name} · {track.EventKey:X8} · {track.Duration:F2}s");
            if (track.Calls.Length == 0 && track.Messages.Length == 0)
                ImGui.TextDisabled("No literal engine calls were recognized in this event record.");

            foreach (var (time, call) in track.Calls.Take(64))
            {
                string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                string targetName = ResolveCinematicObjectName(call.Target, track.WorldKey)
                    ?? "unresolved target";
                string arguments = string.Join(", ", call.Arguments.Select(argument => $"0x{argument:X8}"));
                string description = CinematicEngineFunctionCatalog.Describe(call);
                ImGui.BulletText($"{time,6:F2}s · {function} · {targetName} ({call.Target:X8}) · [{arguments}]");
                if (!string.IsNullOrWhiteSpace(description)) ImGui.TextWrapped($"  {description}");
                var actionLink = actionLinks.Resolved.FirstOrDefault(link => link.Time == time
                    && ReferenceEquals(link.Call, call));
                if (actionLink != null)
                {
                    string clips = actionLink.Action.Items.Length == 0
                        ? "no decoded animation items"
                        : string.Join(", ", actionLink.Action.Items.Select(item =>
                            $"{item.Clip.Key:X8} (shape {FormatScriptKey(item.ShapeKey)})"));
                    string actionControl = actionLink.Function switch
                    {
                        "ACTActionSet" => "action clock reset",
                        "ACTActionFrameSet" => $"frame {actionLink.ControlValue} (source stores int16)",
                        "ACTActionFrequencySet" => $"frequency input {actionLink.ControlValue} (engine scale 60/64)",
                        _ => ""
                    };
                    ImGui.TextWrapped($"  Action slot {actionLink.ActionIndex} · {actionControl} · "
                        + $"action table world {actionLink.ActionTableWorldKey:X8} · clip/shape: {clips}");
                }
                else
                {
                    var unresolvedActionLink = actionLinks.Unresolved.FirstOrDefault(link => link.Time == time
                        && ReferenceEquals(link.Call, call));
                    if (unresolvedActionLink != null)
                        ImGui.TextDisabled($"  Action link unresolved: {unresolvedActionLink.Reason}");
                }
                if (Document != null
                    && CinematicSoundCommandResolver.Describe(call, track.WorldKey, Document,
                        Document.CinematicScriptOwners.Values) is { Length: > 0 } soundBinding)
                    ImGui.TextWrapped($"  Audio binding: {soundBinding}");
                if (Document != null
                    && CinematicEmbeddedEventTrackResolver.Describe(call, Document, owner.WorldKey) is { } embeddedTrack)
                    ImGui.TextWrapped($"  {embeddedTrack}");
            }

            if (track.Calls.Length > 64)
                ImGui.TextDisabled($"{track.Calls.Length - 64} more decoded calls omitted.");

            foreach (var (time, message) in track.Messages.Take(64))
            {
                string typeDescription = message.Type switch
                {
                    30 => "dialogue line request",
                    46 => "typed script message",
                    53 => "facial-control message",
                    _ => "typed message"
                };
                string context = DescribeCinematicMessageReference(message.ExecutionObjectKey, track.WorldKey);
                string gao = DescribeCinematicMessageReference(message.Gao, track.WorldKey);
                string gao2 = DescribeCinematicMessageReference(message.Gao2, track.WorldKey);
                ImGui.BulletText($"{time,6:F2}s · message {message.Type} ({typeDescription}) · EVE target {context} · GAO {gao} · GAO2 {gao2} · Int1 {message.Int1} · Int2 {message.Int2}");
                if (Document != null
                    && CinematicScriptMessageAnalyzer.Describe(message, Document.CinematicScriptOwners.Values,
                        track.WorldKey) is { } behavior)
                    ImGui.TextWrapped($"  {behavior}");
                if (message.Type == 30)
                    DrawCinematicDialogueMessageLookup(message, track.WorldKey);
            }

            if (track.Messages.Length > 64)
                ImGui.TextDisabled($"{track.Messages.Length - 64} more typed messages omitted.");
        }
    }

    private void DrawCinematicScriptEventTrackCalls(CinematicScriptOwner owner)
    {
        if (Document == null) return;
        var analysis = CinematicScriptEventTrackCallResolver.Analyze(owner,
            CinematicScriptReader.ReachableEngineCalls(owner), Document.CinematicEmbeddedEventTracks);
        var invocations = analysis.Resolved;

        if (invocations.Length > 0)
        {
            DrawCategoryTitle($"Callback-driven SceneEvents calls ({invocations.Length})");
            ImGui.TextDisabled("These source-reachable TRA* calls resolve a current-owner or serialized OVA receiver to SceneEvents. Indices may use bounded scalar OVA arithmetic outside loops; loops, runtime functions and live callback execution are not evaluated. OVA values are serialized defaults, not live state.");
        }
        foreach (var invocation in invocations)
        {
            var call = invocation.SourceCall;
            var branchConditions = call.BranchConditions.Select(condition => condition.Kind == "else"
                    ? $"else of ({condition.Expression})"
                    : $"if ({condition.Expression})")
                .ToArray();
            string branchSummary = branchConditions.Length == 0
                ? "no enclosing branch recorded"
                : string.Join("; ", branchConditions);
            string location = $"{Path.GetFileName(call.Source)}:{call.Line}";
            ImGui.BulletText($"{call.FunctionName} → {invocation.Operation} · {invocation.EventList.OwnerName} / SceneEvents ({invocation.EventList.EventKey:X8}) · receiver: {invocation.TargetBinding} · {location}");
            ImGui.TextWrapped($"  callback path: {call.CallbackPath} · branch: {branchSummary}");
            foreach (string staticBranch in CinematicScriptBranchAnalyzer.Describe(owner,
                         call.BranchConditions))
                ImGui.TextWrapped($"  {staticBranch}");
            if (invocation.TrackIndex == null && invocation.Operation.EndsWith("All", StringComparison.Ordinal))
            {
                int targetCount = invocation.Tracks.Select(track => track.TargetKey).Distinct().Count();
                int totalKeyCount = invocation.Tracks.Sum(track => track.KeyCount);
                ImGui.TextWrapped($"  Addresses all {invocation.Tracks.Length} serialized channels · {targetCount} distinct target key(s) · {totalKeyCount} keys");
            }
            else
            {
                foreach (var track in invocation.Tracks)
                {
                    string target = track.TargetKey switch
                    {
                        0 => "owner/default target",
                        uint.MaxValue => "invalid target key",
                        _ => ResolveCinematicObjectName(track.TargetKey, invocation.EventList.WorldKey) is { } targetName
                            ? $"{targetName} ({track.TargetKey:X8})"
                            : $"unresolved target {track.TargetKey:X8}"
                    };
                    string trackLabel = $"EVE track {track.Index} · {target} · bone {track.Bone} · {track.KeyCount} keys · {track.Duration:F2}s";
                    var sceneTarget = _cinematicWorld?.Objects.FirstOrDefault(sceneObject =>
                        sceneObject.GaoKey == track.TargetKey);
                    if (sceneTarget != null && track.TargetKey is > 0 and < uint.MaxValue)
                    {
                        if (ImGui.Selectable($"  {trackLabel}##callbackEventTrack{owner.WorldKey:X8}{owner.OwnerKey:X8}"
                                + $"{invocation.EventList.EventKey:X8}{track.Index}{track.TargetKey:X8}"))
                        {
                            SelectedObject = sceneTarget;
                            Renderer.FrameSelected();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Select and frame this source SceneEvents target in the cinematic scene.");
                    }
                    else
                    {
                        ImGui.TextWrapped($"  {trackLabel}");
                    }
                }
                if (invocation.IndexBinding is { Length: > 0 })
                    ImGui.TextWrapped($"  Index source: {invocation.IndexBinding}");
            }

            int commandCount = invocation.Tracks.Sum(track => track.Calls.Length);
            int messageCount = invocation.Tracks.Sum(track => track.Messages.Length);
            string channelLabel = $"Inspect addressed EVE channels · {invocation.Tracks.Length} channels · {commandCount} commands · {messageCount} messages";
            string channelId = $"##resolvedEventChannels{owner.WorldKey:X8}{owner.OwnerKey:X8}"
                + $"{invocation.EventList.EventKey:X8}{call.Source}:{call.Line}:{invocation.TrackIndex}";
            if (ImGui.TreeNode(channelLabel + channelId))
            {
                ImGui.TextDisabled("These are the decoded channels addressed by this source-reachable TRA* call. Command targets can be selected in the viewport; this shows serialized event data, not live script execution.");
                ImGui.PushID(channelId);
                int shownChannels = 0;
                var addressedList = invocation.EventList with { Tracks = invocation.Tracks };
                DrawCinematicEmbeddedEventTrackList(addressedList, ref shownChannels,
                    invocation.Tracks.Length, expandEventCommands: true);
                ImGui.PopID();
                ImGui.TreePop();
            }
        }
        if (invocations.Length > 0)
            ImGui.TextDisabled("A resolved call proves a configured callback can address the listed EVE track; it does not prove that its conditions passed or that the call ran.");

        if (analysis.Unresolved.Length == 0
            || !ImGui.CollapsingHeader($"Unresolved TRA* source calls ({analysis.Unresolved.Length})"))
            return;
        ImGui.TextDisabled("These recognized Iris calls could not be joined to a decoded SceneEvents channel. Each entry gives the unresolved link or analysis limit.");
        foreach (var unresolved in analysis.Unresolved)
        {
            var call = unresolved.SourceCall;
            string arguments = string.Join(", ", call.Arguments);
            string branchSummary = call.BranchConditions.Length == 0
                ? "no enclosing branch recorded"
                : string.Join("; ", call.BranchConditions.Select(condition => condition.Kind == "else"
                    ? $"else of ({condition.Expression})" : $"if ({condition.Expression})"));
            ImGui.BulletText($"{call.Api}({arguments}) · {Path.GetFileName(call.Source)}:{call.Line}");
            ImGui.TextWrapped($"  callback path: {call.CallbackPath} · branch: {branchSummary}");
            foreach (string staticBranch in CinematicScriptBranchAnalyzer.Describe(owner,
                         call.BranchConditions))
                ImGui.TextWrapped($"  {staticBranch}");
            ImGui.TextWrapped($"  unresolved: {unresolved.Reason}");
        }
    }

    private void DrawCinematicEmbeddedEventTrackInventory()
    {
        if (Document == null || _cinematicWorld == null) return;
        var allLists = Document.CinematicEmbeddedEventTracks
            .Where(entry => entry.Key.WorldKey == _cinematicWorld.Key)
            .SelectMany(entry => entry.Value)
            .OrderBy(list => list.OwnerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(list => list.LinkName, StringComparer.Ordinal)
            .ThenBy(list => list.EventKey)
            .ToArray();
        string filter = _cinematicEveSearch.Trim();
        var lists = string.IsNullOrEmpty(filter)
            ? allLists
            : allLists.Where(list => MatchesCinematicEventSearch(list, filter, _cinematicWorld.Key)).ToArray();
        int channelCount = lists.Sum(list => list.Tracks.Length);
        if (!string.IsNullOrEmpty(filter)) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (!ImGui.CollapsingHeader($"SceneEvents EVE inventory ({lists.Length}/{allLists.Length} lists, {channelCount} channels)"))
            return;

        ImGui.TextWrapped(string.IsNullOrEmpty(filter)
            ? "Static inventory of the extended SceneEvents EVE lists. TRA* indices address these lists on the target GAO; channel data does not show whether a script started them at this playhead."
            : $"Showing lists that match ‘{filter}’ in owner, event key, channel, command name, target, or message. This is static data; it does not prove the command ran.");
        int shownChannels = 0;
        foreach (var list in lists)
            DrawCinematicEmbeddedEventTrackList(list, ref shownChannels, 512);
        if (channelCount > shownChannels)
            ImGui.TextDisabled($"Expanded channels shown: {shownChannels} / {channelCount}.");
    }

    private void DrawCinematicEventSearch()
    {
        int matchCount = Document?.CinematicEmbeddedEventTracks
            .Where(entry => entry.Key.WorldKey == _cinematicWorld?.Key)
            .SelectMany(entry => entry.Value)
            .Count(list => MatchesCinematicEventSearch(list, _cinematicEveSearch.Trim(), _cinematicWorld!.Key)) ?? 0;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##CinematicEveSearch", "Find script event owner, GAO, key, or command…",
            ref _cinematicEveSearch, 160);
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(_cinematicEveSearch)
            ? "Search SceneEvents / EVE commands in this cinematic (results below)."
            : $"{matchCount} matching SceneEvents list(s) in the inventory below.");
    }

    private bool MatchesCinematicEventSearch(CinematicEmbeddedEventTrackList list, string filter, uint worldKey)
    {
        if (filter.Length == 0) return true;
        if (Contains(list.OwnerName) || Contains(list.LinkName) || Contains($"{list.OwnerKey:X8}")
            || Contains($"{list.EventKey:X8}")) return true;
        foreach (var track in list.Tracks)
        {
            if (Contains($"EVE {track.Index}") || Contains($"{track.Index}")
                || Contains($"{track.TargetKey:X8}")) return true;
            foreach (var (_, call) in track.Calls)
            {
                if (Contains(CinematicEngineFunctionCatalog.NameFor(call.Selector))
                    || Contains($"{call.Target:X8}")
                    || Contains(ResolveCinematicObjectName(call.Target, worldKey) ?? string.Empty)) return true;
            }
            foreach (var (_, message) in track.Messages)
                if (Contains(message.ToString() ?? string.Empty)) return true;
        }
        return false;

        bool Contains(string value) => value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawCinematicExternalOwnerEventTracks(CinematicScriptOwner owner)
    {
        if (Document == null || _cinematicPlayback == null
            || owner.WorldKey == _cinematicPlayback.WorldKey
            || !Document.CinematicEmbeddedEventTracks.TryGetValue((owner.WorldKey, owner.OwnerKey),
                out var lists)
            || lists.Length == 0)
            return;

        int channelCount = lists.Sum(list => list.Tracks.Length);
        DrawCategoryTitle($"Source-world SceneEvents EVE lists ({lists.Length}, {channelCount} channels)");
        ImGui.TextDisabled("These linked records belong to the actor's source world. Their presence does not prove that the cinematic instance runs them.");
        int actionRequestCount = lists.Sum(list => list.Tracks.Sum(track =>
            {
                var links = CinematicEventActionLinkResolver.AnalyzeCalls(track.Calls,
                    list.WorldKey, Document.CinematicActionKitsByOwner);
                return links.Resolved.Length + links.Unresolved.Length;
            }));
        if (actionRequestCount > 0)
            ImGui.TextDisabled($"{actionRequestCount} action-control command link(s) are indexed below; matching EVE channels open automatically.");
        int shownChannels = 0;
        foreach (var list in lists)
            DrawCinematicEmbeddedEventTrackList(list, ref shownChannels, 256, highlightActionLinks: true);
        if (channelCount > shownChannels)
            ImGui.TextDisabled($"Expanded channels shown: {shownChannels} / {channelCount}.");
    }

    private void DrawCinematicEmbeddedEventTrackList(CinematicEmbeddedEventTrackList list,
        ref int shownChannels, int maximumChannels, bool highlightActionLinks = false,
        bool expandEventCommands = false)
    {
        if (shownChannels >= maximumChannels) return;
        if (Document is not { } document) return;
        int actionRequestCount = highlightActionLinks ? list.Tracks.Sum(track =>
        {
            var links = CinematicEventActionLinkResolver.AnalyzeCalls(track.Calls,
                list.WorldKey, document.CinematicActionKitsByOwner);
            return links.Resolved.Length + links.Unresolved.Length;
        }) : 0;
        string actionSummary = actionRequestCount > 0 ? $" · {actionRequestCount} action command links" : "";
        string label = $"{list.OwnerName} · {list.LinkName} {list.EventKey:X8} · {list.Tracks.Length} channels{actionSummary}";
        ImGui.SetNextItemOpen(actionRequestCount > 0, ImGuiCond.FirstUseEver);
        if (!ImGui.TreeNode($"{label}##eve{list.WorldKey:X8}{list.OwnerKey:X8}{list.EventKey:X8}{list.LinkName}"))
            return;

        int visibleTrackCount = Math.Min(128, maximumChannels - shownChannels);
        foreach (var track in list.Tracks.Take(visibleTrackCount))
        {
            var actionLinks = CinematicEventActionLinkResolver.AnalyzeCalls(track.Calls,
                list.WorldKey, document.CinematicActionKitsByOwner);
            int trackActionRequestCount = actionLinks.Resolved.Length + actionLinks.Unresolved.Length;
            string target = track.TargetKey switch
            {
                0 => "owner/default target",
                uint.MaxValue => "invalid target",
                _ => ResolveCinematicObjectName(track.TargetKey, list.WorldKey) is { } targetName
                    ? $"{targetName} ({track.TargetKey:X8})" : $"{track.TargetKey:X8}"
            };
            string channelLabel = $"EVE index {track.Index} · {target} · bone {track.Bone} · "
                + $"{track.KeyCount} keys · {track.Duration:F2}s · flags 0x{track.ChannelFlags:X4} "
                + $"· {track.Calls.Length} calls · {track.Messages.Length} messages"
                + (trackActionRequestCount > 0 ? $" · {trackActionRequestCount} action links" : "");
            if (highlightActionLinks)
                ImGui.SetNextItemOpen(trackActionRequestCount > 0, ImGuiCond.FirstUseEver);
            else if (expandEventCommands)
                ImGui.SetNextItemOpen(track.Calls.Length > 0 || track.Messages.Length > 0,
                    ImGuiCond.FirstUseEver);
            if (ImGui.TreeNode($"{channelLabel}##eve-channel{list.WorldKey:X8}{list.OwnerKey:X8}{list.EventKey:X8}{track.Index}"))
            {
                DrawCinematicEventTrackPreviewControl(list, track);
                foreach (var (time, call) in track.Calls.Take(32))
                {
                    string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                    string targetName = ResolveCinematicObjectName(call.Target, list.WorldKey) ?? $"{call.Target:X8}";
                    string arguments = string.Join(", ", call.Arguments.Select(argument => $"0x{argument:X8}"));
                    string commandLabel = $"{time:F2}s · {function} · {targetName} · [{arguments}]";
                    var sceneTarget = _cinematicWorld?.Objects.FirstOrDefault(sceneObject =>
                        sceneObject.GaoKey == call.Target);
                    if (sceneTarget != null && list.WorldKey == _cinematicWorld?.Key)
                    {
                        if (ImGui.Selectable($"{commandLabel}##eve-command{list.OwnerKey:X8}{track.Index}{time:F3}{call.Target:X8}"))
                        {
                            SelectedObject = sceneTarget;
                            Renderer.FrameSelected();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Select and frame this command target in the cinematic scene.");
                    }
                    else
                    {
                        ImGui.BulletText(commandLabel);
                    }
                    string description = CinematicEngineFunctionCatalog.Describe(call);
                    if (!string.IsNullOrWhiteSpace(description)) ImGui.TextWrapped($"  {description}");
                    DrawCinematicCommandObjectArguments(call, list.WorldKey,
                        $"{list.OwnerKey:X8}{track.Index}{time:F3}");
                    var actionLink = actionLinks.Resolved.FirstOrDefault(link => link.Time == time
                        && ReferenceEquals(link.Call, call));
                    if (actionLink != null)
                    {
                        string clips = actionLink.Action.Items.Length == 0
                            ? "no decoded animation items"
                            : string.Join(", ", actionLink.Action.Items.Select(item =>
                                $"{item.Clip.Key:X8} (shape {FormatScriptKey(item.ShapeKey)})"));
                        string actionControl = actionLink.Function switch
                        {
                            "ACTActionSet" => "action clock reset",
                            "ACTActionFrameSet" => $"frame {actionLink.ControlValue} (source stores int16)",
                            "ACTActionFrequencySet" => $"frequency input {actionLink.ControlValue} (engine scale 60/64)",
                            _ => ""
                        };
                        ImGui.TextWrapped($"  Action slot {actionLink.ActionIndex} · {actionControl} · "
                            + $"action table world {actionLink.ActionTableWorldKey:X8} · clip/shape: {clips}");
                    }
                    else if (actionLinks.Unresolved.FirstOrDefault(link => link.Time == time
                                 && ReferenceEquals(link.Call, call)) is { } unresolvedActionLink)
                    {
                        ImGui.TextDisabled($"  Action link unresolved: {unresolvedActionLink.Reason}");
                    }
                    if (CinematicSoundCommandResolver.Describe(call, list.WorldKey, document,
                            document.CinematicScriptOwners.Values) is { Length: > 0 } soundBinding)
                        ImGui.TextWrapped($"  Audio binding: {soundBinding}");
                    if (Document != null
                        && CinematicEmbeddedEventTrackResolver.Describe(call, Document, list.WorldKey) is { } nestedTrack)
                        ImGui.TextWrapped($"  {nestedTrack}");
                }
                if (CinematicNestedEventTrackResolver.HasNestedCalls(track)
                    && ImGui.TreeNode($"Follow nested SceneEvents calls##nestedEve{list.WorldKey:X8}"
                        + $"{list.OwnerKey:X8}{list.EventKey:X8}{track.Index}"))
                {
                    var nestedGraph = CinematicNestedEventTrackResolver.Analyze(list, track,
                        document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists));
                    ImGui.TextDisabled("Static recursive links from serialized TRA* commands. A resolved route does not prove its track was started at runtime.");
                    for (int routeIndex = 0; routeIndex < nestedGraph.Routes.Length; routeIndex++)
                    {
                        var route = nestedGraph.Routes[routeIndex];
                        var routeTarget = _cinematicWorld?.Objects.FirstOrDefault(sceneObject =>
                            sceneObject.GaoKey == route.TargetOwnerKey);
                        string row = $"Depth {route.Depth} · {route.Path}";
                        if (route.IsCycle) row += " · cycle";
                        if (route.Limitation is { } limitation) row += $" · {limitation}";
                        if (routeTarget != null && list.WorldKey == _cinematicWorld?.Key
                            && ImGui.Selectable($"{row}##nestedEveRoute{routeIndex}"))
                        {
                            SelectedObject = routeTarget;
                            Renderer.FrameSelected();
                        }
                        else
                        {
                            ImGui.TextWrapped(row);
                        }
                    }
                    if (nestedGraph.WasTruncated)
                        ImGui.TextDisabled("Route traversal stopped at the 512-link safety limit.");
                    ImGui.TreePop();
                }
                if (track.Calls.Length > 32)
                    ImGui.TextDisabled($"{track.Calls.Length - 32} additional calls omitted.");
                foreach (var (time, message) in track.Messages.Take(32))
                {
                    ImGui.BulletText($"{time:F2}s · typed message {message.Type} · EVE target {DescribeCinematicMessageReference(message.ExecutionObjectKey, list.WorldKey)} · GAO {DescribeCinematicMessageReference(message.Gao, list.WorldKey)} · GAO2 {DescribeCinematicMessageReference(message.Gao2, list.WorldKey)}");
                    if (Document != null
                        && CinematicScriptMessageAnalyzer.Describe(message,
                            Document.CinematicScriptOwners.Values, list.WorldKey) is { } messageBehavior)
                        ImGui.TextWrapped($"  {messageBehavior}");
                }
                if (track.Messages.Length > 32)
                    ImGui.TextDisabled($"{track.Messages.Length - 32} additional messages omitted.");
                DrawCinematicRawEventPayloads(list, track);
                if (track.Calls.Length == 0 && track.Messages.Length == 0)
                    ImGui.TextDisabled("No recognized literal commands or typed messages; this channel may contain transforms or unsupported payloads.");
                ImGui.TreePop();
            }
            shownChannels++;
        }
        if (list.Tracks.Length > visibleTrackCount)
            ImGui.TextDisabled($"{list.Tracks.Length - visibleTrackCount} additional channels omitted from this list.");
        ImGui.TreePop();
    }

    private void DrawCinematicEventTrackPreviewControl(CinematicEmbeddedEventTrackList list,
        CinematicEmbeddedEventTrack eventTrack)
    {
        if (_cinematicPlayback == null || _cinematicWorld == null
            || list.WorldKey != _cinematicWorld.Key
            || eventTrack.SourceTrack is not { } sourceTrack
            || !CinematicEventTrackPreview.HasTransformData(sourceTrack))
            return;

        uint targetKey = eventTrack.TargetKey == 0 ? list.OwnerKey : eventTrack.TargetKey;
        var target = _cinematicWorld.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == targetKey);
        if (target == null)
        {
            ImGui.TextDisabled($"Transform preview unavailable: target {targetKey:X8} is not loaded in this cinematic world.");
            return;
        }

        bool isActive = _cinematicPlayback.IsPreviewingEventTrack(target, sourceTrack);
        string buttonLabel = isActive ? "Stop manual EVE transform preview" : "Preview EVE transform from playhead";
        if (ImGui.SmallButton($"{buttonLabel}##eve-transform{list.WorldKey:X8}{list.OwnerKey:X8}{list.EventKey:X8}{eventTrack.Index}"))
        {
            if (isActive) _cinematicPlayback.ClearEventTrackPreview();
            else _cinematicPlayback.PreviewEventTrack(target, sourceTrack);
        }
        ImGui.TextWrapped("Manual transform-only preview. It follows this cinematic playhead from the moment you start it; it does not imply the game's callback or activation gate ran.");
    }

    private void DrawCinematicDialogueMessageLookup(CinematicTimelineMessage message, uint preferredWorldKey)
    {
        if (Document == null) return;
        var resolution = CinematicDialogueMessageResolver.Resolve(message,
            Document.CinematicScriptOwners.Values, preferredWorldKey);
        switch (resolution.Kind)
        {
            case CinematicDialogueMessageResolutionKind.ClearRequest:
                ImGui.TextDisabled("  Dialogue receiver clear/hide request; no text slot is selected.");
                break;
            case CinematicDialogueMessageResolutionKind.AdvanceRequest:
                ImGui.TextDisabled("  Dialogue receiver advance request; the resulting slot depends on its runtime cursor.");
                break;
            case CinematicDialogueMessageResolutionKind.ReceiverUnavailable:
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot}; EVE target {message.ExecutionObjectKey:X8} is unavailable.");
                break;
            case CinematicDialogueMessageResolutionKind.EmptySlot:
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot}; no text-bank reference is configured.");
                break;
            case CinematicDialogueMessageResolutionKind.TextReference:
                var receiver = resolution.ReceiverId is { } receiverId
                    ? Document.CinematicScriptOwners.GetValueOrDefault(receiverId) : null;
                string receiverName = receiver?.Name ?? "receiver";
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot} → text bank {resolution.TextBankKey:X8}, entry {resolution.TextId} ({receiverName}).");
                break;
        }

        foreach (string detail in CinematicDialogueMessageResolver.DescribeSelectedLine(message, resolution))
            ImGui.TextWrapped($"    {detail}");
    }

    private string DescribeCinematicMessageReference(uint key, uint preferredWorldKey)
    {
        if (key == 0) return "null";
        if (key == uint.MaxValue) return "invalid";
        string? name = ResolveCinematicObjectName(key, preferredWorldKey);
        return name == null ? $"{key:X8} (unresolved)" : $"{name} ({key:X8})";
    }

    private CinematicScriptOwner? ResolveLinkedOwner(uint key, uint preferredWorldKey) =>
        Document == null ? null : CinematicScriptOwnerResolver.Resolve(key, preferredWorldKey,
            Document.CinematicScriptOwners.Values);

    private string? ResolveCinematicObjectName(uint key, uint preferredWorldKey)
    {
        if (Document == null) return null;
        var localWorld = Document.Worlds.FirstOrDefault(world => world.Key == preferredWorldKey);
        var localObject = localWorld?.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == key);
        if (localObject != null) return localObject.Name;
        var localOwner = ResolveLinkedOwner(key, preferredWorldKey);
        if (localOwner != null) return localOwner.Name;

        var otherObjects = Document.Worlds.SelectMany(world => world.Objects)
            .Where(sceneObject => sceneObject.GaoKey == key).ToArray();
        if (otherObjects.Length == 1) return otherObjects[0].Name;
        var otherOwners = Document.CinematicScriptOwners.Values.Where(owner => owner.OwnerKey == key).ToArray();
        return otherOwners.Length == 1 ? otherOwners[0].Name : null;
    }

    private static string DescribeDialogueLine(CinematicDialogueLine line)
    {
        var details = new List<string>();
        if (line.TextBankKey != 0 || line.TextId != 0)
            details.Add($"text {line.TextBankKey:X8}:{line.TextId}");
        if (line.SoundId is > 0)
            details.Add(line.SoundVariantMax is > 0
                ? $"associated sound {line.SoundId} (ai_son_differents {line.SoundVariantMax})"
                : $"associated sound {line.SoundId}");
        if (line.TextBox is { } textBox && textBox != 0) details.Add($"box {textBox:G}");
        if (line.Volume is { } volume && volume != 0) details.Add($"volume {volume:G}");
        if (line.Near is { } near && near != 0) details.Add($"near {near:G}");
        if (line.Far is { } far && far != 0) details.Add($"far {far:G}");
        return $"Slot {line.Slot}: {string.Join(" · ", details)}";
    }

    private void DrawCinematicScriptIncomingReferences(CinematicScriptOwner target,
        CinematicScriptOwner[] sceneScripts)
    {
        var incoming = new List<(CinematicScriptOwner? Owner, string Link, float? Time)>();
        foreach (var owner in sceneScripts)
        foreach (var reference in owner.ObjectReferences.Where(reference =>
                     ResolveLinkedOwner(reference.TargetKey, owner.WorldKey)?.Id == target.Id))
        {
            string variableName = reference.ArrayIndex is { } index
                ? $"{reference.VariableName}[{index}]" : reference.VariableName;
            incoming.Add((owner, $"{reference.Source}:{variableName}", null));
        }

        if (Document != null)
        {
            foreach (var track in Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks)
                .Where(track => sceneScripts.Any(owner => owner.Id == track.OwnerId)))
            {
                var owner = sceneScripts.First(candidate => candidate.Id == track.OwnerId);
                incoming.AddRange(track.Calls.Where(item => ResolveLinkedOwner(item.Call.Target, track.WorldKey)?.Id == target.Id)
                    .Select(item => (Owner: (CinematicScriptOwner?)owner,
                        Link: $"{track.LinkName} {track.EventKey:X8} · {item.Time:F2}s · "
                            + CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                        Time: (float?)item.Time)));
                foreach (var (time, message) in track.Messages)
                {
                    if (ResolveLinkedOwner(message.ExecutionObjectKey, track.WorldKey)?.Id == target.Id)
                        incoming.Add((owner, $"{track.LinkName} {track.EventKey:X8} · {time:F2}s · EVE target", time));
                    if (ResolveLinkedOwner(message.Gao, track.WorldKey)?.Id == target.Id)
                        incoming.Add((owner, $"{track.LinkName} {track.EventKey:X8} · {time:F2}s · message GAO", time));
                    if (ResolveLinkedOwner(message.Gao2, track.WorldKey)?.Id == target.Id)
                        incoming.Add((owner, $"{track.LinkName} {track.EventKey:X8} · {time:F2}s · message GAO2", time));
                }
            }
        }
        if (_cinematicPlayback != null)
        {
            incoming.AddRange(CinematicClipIncomingLinkResolver.Find(target.OwnerKey,
                    _cinematicPlayback.ScriptCalls, _cinematicPlayback.ScriptMessages)
                .Select(link => (Owner: (CinematicScriptOwner?)null,
                    Link: $"Selected cinematic {_cinematicPlayback.Key:X8} · {link.Time:F2}s · {link.Description}",
                    Time: (float?)link.Time)));
        }
        var references = incoming.DistinctBy(item => (item.Owner?.Id, item.Link, item.Time)).ToArray();
        if (!ImGui.CollapsingHeader($"Incoming OVA / event links ({references.Length})")) return;
        if (references.Length == 0)
        {
            ImGui.TextDisabled("No incoming owner OVA field or linked event signal was found in the current graph.");
            return;
        }

        foreach (var (owner, link, time) in references)
        {
            string label = owner == null ? link : $"{owner.Name} · {owner.OwnerKey:X8} · {link}";
            bool selectedLink = ImGui.Selectable($"{label}##incoming{owner?.WorldKey:X8}{owner?.OwnerKey:X8}{link}");
            if (owner != null)
            {
                DrawCopyNameContextMenu($"CinematicScriptIncomingName##{owner.WorldKey:X8}{owner.OwnerKey:X8}{link}", owner.Name);
            }
            if (!selectedLink) continue;
            if (owner != null)
            {
                _selectedCinematicScriptOwnerId = owner.Id;
                continue;
            }

            if (_cinematicPlayback == null || time is not { } eventTime) continue;
            _cinematicPlayback.Pause();
            _cinematicPlayback.Seek(eventTime);
        }
    }

    private static void DrawCinematicScriptVariables(CinematicScriptOwner owner)
    {
        if (ImGui.CollapsingHeader($"Serialized OVA variables ({owner.Variables.Length})"))
        {
            ImGui.TextWrapped("These are saved model/instance snapshots. Iris initializes from model values, then overlays matching instance fields by name. If array lengths differ, it copies the shorter prefix and retains the remaining model values. Runtime state may change while the game runs.");
            ImGui.BeginChild("CinematicScriptVariables", new Vector2(0, 150), ImGuiChildFlags.Borders);
            foreach (var variable in owner.Variables)
            {
                string label = string.IsNullOrWhiteSpace(variable.Name) ? "(unnamed)" : variable.Name;
                ImGui.TextWrapped($"{variable.Source} · {label} · type {variable.Type} = {variable.Value}");
            }
            ImGui.EndChild();
        }

        if (!ImGui.CollapsingHeader("OVA fields used by reachable callbacks")) return;
        var fieldUsages = CinematicScriptOvaUsageResolver.Analyze(owner);
        ImGui.TextDisabled("Fields are joined to reached Iris functions when a local pointer is source-resolved to instanceVarDataCurrent/Model. Addressed fields retain unknown read/write direction. Engine-call argument/branch evidence supplements that index. This is source use, not proof the callback executed.");
        if (fieldUsages.Length == 0)
        {
            ImGui.TextDisabled("No serialized owner fields were matched to reachable source accesses.");
            return;
        }

        var fieldGroups = fieldUsages.GroupBy(usage => usage.VariableName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        ImGui.TextDisabled($"{fieldGroups.Length} OVA fields matched at {fieldUsages.Length} source locations.");
        foreach (var fieldGroup in fieldGroups.Take(64))
        {
            var usages = fieldGroup.ToArray();
            string[] accessKinds = usages.Select(usage => usage.Access == "addressed"
                    ? "addressed (direction unresolved)" : usage.Access)
                .Distinct(StringComparer.Ordinal).ToArray();
            string accessSummary = string.Join("/", accessKinds);
            if (!ImGui.TreeNode($"{fieldGroup.Key} · {accessSummary} · {usages.Length} source sites##ova-field-{owner.WorldKey:X8}{owner.OwnerKey:X8}{fieldGroup.Key}"))
                continue;

            var variable = owner.EffectiveVariable(fieldGroup.Key);
            string value = variable?.Value ?? "no decoded effective value";
            ImGui.TextWrapped($"Effective serialized value: {value}");
            var objectReferences = owner.ObjectReferences.Where(reference =>
                reference.VariableName.Equals(fieldGroup.Key, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
            foreach (var reference in objectReferences)
            {
                string arrayIndex = reference.ArrayIndex is { } index ? $"[{index}]" : "";
                ImGui.TextWrapped($"Serialized reference {reference.Source} {fieldGroup.Key}{arrayIndex} → "
                    + $"{reference.TargetName ?? "unresolved"} ({reference.TargetKey:X8})");
            }
            foreach (var usage in usages.Take(8))
            {
                ImGui.BulletText($"{usage.FunctionName} · {Path.GetFileName(usage.Source)}:{usage.Line} · {usage.CallbackPath}");
                foreach (string expression in usage.SourceExpressions.Take(1))
                    ImGui.TextWrapped($"  source: {expression}");
                if (usage.BranchConditions.Length > 0)
                {
                    string conditions = string.Join("; ", usage.BranchConditions.Select(condition =>
                        condition.Kind == "else" ? $"else of ({condition.Expression})" : $"if ({condition.Expression})"));
                    ImGui.TextWrapped($"  source branch: {conditions}");
                }
                foreach (var write in usage.Writes.Take(1))
                    ImGui.TextWrapped($"  OVA write: {write.Expression}");
            }
            if (usages.Length > 8)
                ImGui.TextDisabled($"{usages.Length - 8} more source locations omitted for this field.");
            ImGui.TreePop();
        }
        if (fieldGroups.Length > 64)
            ImGui.TextDisabled($"Showing 64 of {fieldGroups.Length} OVA fields.");
    }

    private void DrawCinematicScriptRenderBindings(CinematicScriptOwner owner)
    {
        if (Document == null || !ImGui.CollapsingHeader("Owner object render resources")) return;
        var sceneObject = Document.Worlds.SelectMany(world => world.Objects)
            .FirstOrDefault(candidate => candidate.GaoKey == owner.OwnerKey);
        if (sceneObject == null)
        {
            ImGui.TextDisabled("This script owner has no matching render object in the loaded worlds.");
            return;
        }

        ImGui.TextWrapped($"GAO {sceneObject.Name} · geometry {sceneObject.GeometryKey:X8} · material {sceneObject.MaterialKey:X8}");
        if (!Resolver.Materials.TryGetValue(sceneObject.MaterialKey, out var rootMaterial))
        {
            ImGui.TextDisabled("The owner's material was not resolved in the loaded resource containers.");
            return;
        }

        var pendingMaterialKeys = new Queue<uint>();
        var visitedMaterialKeys = new HashSet<uint>();
        pendingMaterialKeys.Enqueue(rootMaterial.Id.Key);
        int materialCount = 0;
        while (pendingMaterialKeys.TryDequeue(out uint materialKey) && materialCount < 64)
        {
            if (!visitedMaterialKeys.Add(materialKey)
                || !Resolver.Materials.TryGetValue(materialKey, out var material)) continue;
            materialCount++;
            ImGui.BulletText($"Material {material.Id.Key:X8} · type {material.Type}");
            foreach (uint childKey in material.SubMaterialKeys)
                pendingMaterialKeys.Enqueue(childKey);

            var layers = JadeMaterialLayer.Read(material.SourceBytes);
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                uint textureKey = layers[layerIndex].Texture;
                if (textureKey is 0 or uint.MaxValue) continue;
                string binding = $"Layer {layerIndex} · texture {textureKey:X8}";
                if (Resolver.ProceduralTextures.TryGetValue(textureKey, out var procedural))
                {
                    string media = procedural.MediaKey is { } mediaKey
                        ? $" · media {procedural.MediaName ?? "unresolved"} ({mediaKey:X8})"
                        : " · no media key decoded";
                    ImGui.TextWrapped($"  {binding} · {ProceduralTextureKind.Name(procedural.Subtype)} · {procedural.Width}×{procedural.Height}{media}");
                    ImGui.TextDisabled($"    Preview: {ProceduralTextureKind.PreviewStatus(procedural.Subtype)}");
                    if (procedural.Subtype == 3)
                        ImGui.TextDisabled($"    Runtime: {Renderer.ProceduralVideoStatus(procedural.Key)}");
                }
                else if (Resolver.Textures.TryGetValue(textureKey, out var texture))
                {
                    ImGui.TextWrapped($"  {binding} · image {texture.Width}×{texture.Height} · {texture.Id.Scope}");
                }
                else
                {
                    ImGui.TextDisabled($"  {binding} · no decoded image or procedural descriptor");
                }
            }
        }
        if (pendingMaterialKeys.Count > 0)
            ImGui.TextDisabled("Material graph truncated after 64 resolved nodes.");
    }

    private void DrawCinematicTrackCalls()
    {
        if (_cinematicPlayback == null || _cinematicWorld == null
            || !ImGui.CollapsingHeader("Decoded track commands")) return;
        var calls = _cinematicPlayback.ScriptCalls;
        ImGui.TextDisabled($"{calls.Length} decoded literal engine calls. Other event payloads remain opaque.");
        ImGui.BeginChild("CinematicTrackCalls", new Vector2(0, 190), ImGuiChildFlags.Borders);
        foreach (var (time, call) in calls)
        {
            string functionName = CinematicEngineFunctionCatalog.NameFor(call.Selector);
            string targetName = ResolveCinematicObjectName(call.Target, _cinematicWorld.Key)
                ?? "unresolved target";
            string arguments = string.Join(", ", call.Arguments.Select(argument => $"0x{argument:X8}"));
            string description = CinematicEngineFunctionCatalog.Describe(call);
            string embeddedTrack = Document == null ? ""
                : CinematicEmbeddedEventTrackResolver.Describe(call, Document, _cinematicWorld.Key) ?? "";
            string soundBinding = Document == null ? ""
                : CinematicSoundCommandResolver.Describe(call, _cinematicWorld.Key, Document,
                    Document.CinematicScriptOwners.Values);
            string actionLink = Document == null ? ""
                : CinematicEventActionLinkResolver.Describe(call, _cinematicWorld.Key,
                    Document.CinematicActionKitsByOwner) ?? "";
            string semantics = string.Join(" · ", new[]
                    { description, embeddedTrack, soundBinding, actionLink }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
            if (semantics.Length > 0) semantics = $" · {semantics}";
            string label = $"{time,6:F2}s · {functionName} · {targetName} ({call.Target:X8}) · [{arguments}]{semantics}";
            if (ImGui.Selectable($"{label}##call{time:R}{call.Target:X8}{call.Selector:X4}"))
            {
                var target = _cinematicWorld.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == call.Target);
                if (target != null)
                {
                    SelectedObject = target;
                    Renderer.FrameSelected();
                }
                _cinematicPlayback.Pause();
                _cinematicPlayback.Seek(time);
            }
        }
        ImGui.EndChild();
    }

    private static bool IsLinkedToClip(CinematicScriptOwner owner, uint worldKey, uint clipKey) =>
        owner.WorldKey == worldKey && (owner.EventsKey == clipKey || owner.SceneEventsKey == clipKey);

    private static string FormatScriptKey(uint key) => key == uint.MaxValue ? "none" : $"{key:X8}";

    private void UpdateCinematicPlayback()
    {
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            UpdateCinematicPlaybackCore();
        }
        finally
        {
            Renderer.StatCinematicUpdateMilliseconds = (float)
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        }
    }

    private void UpdateCinematicPlaybackCore()
    {
        Renderer.StatCinematicAudioMilliseconds = 0;
        Renderer.CinematicShutter = null;
        Renderer.CinematicFlashes = [];
        Renderer.CinematicLightnings = [];
        Renderer.CinematicScreenFade = null;
        Renderer.CinematicOffsetEffect = .5f;
        Renderer.CinematicColorBalance = .5f;
        Renderer.CinematicHorizontalFov = null;
        Renderer.CinematicMaterials = null;
        Renderer.CinematicVideoTime = null;
        Renderer.CinematicVideoPlayback = null;
        if (!_cutsceneMode || _cinematicPlayback == null)
        {
            Renderer.CinematicMaterials = ResolveEnvironmentPreview(ActiveWorld);
            return;
        }
        double audioClockTime = 0;
        bool audioClockAvailable = _cinematicAudio?.TryGetTimelineClock(out audioClockTime) == true;
        float frameDelta = CinematicAudioPlaybackClock.Delta(_cinematicPlayback.Time,
            audioClockTime, audioClockAvailable, Raylib_cs.Raylib.GetFrameTime());
        bool wasPlaying = _cinematicPlayback.Playing;
        _cinematicPlayback.Advance(frameDelta);
        bool sourceTimelineJustEnded = wasPlaying && !_cinematicPlayback.Playing
            && _cinematicPlayback.Time >= _cinematicPlayback.Duration;
        if (_cinematicPlayback.Time < _cinematicPlayback.Duration)
        {
            _cinematicVideoPostRollActive = false;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
        }
        Renderer.CinematicVideoPlayback = _cinematicPlayback.VideoPlayback;
        if (sourceTimelineJustEnded && Renderer.HasContinuingCinematicVideo(_cinematicPlayback.Time))
        {
            _cinematicVideoPostRollActive = true;
            _cinematicVideoPostRollCompleted = false;
            _cinematicVideoPostRollSeconds = 0;
        }
        else if (_cinematicVideoPostRollActive && !_cinematicVideoPostRollCompleted)
        {
            _cinematicVideoPostRollSeconds += frameDelta;
            _cinematicVideoPostRollCompleted = !Renderer.HasContinuingCinematicVideo(
                _cinematicPlayback.Time + _cinematicVideoPostRollSeconds);
        }
        Renderer.CinematicVideoTime = _cinematicPlayback.Time + _cinematicVideoPostRollSeconds;
        Renderer.CinematicShutter = _cinematicPlayback.Shutter;
        Renderer.CinematicFlashes = _cinematicPlayback.Flashes;
        Renderer.CinematicLightnings = _cinematicPlayback.Lightnings;
        Renderer.CinematicScreenFade = _cinematicPlayback.ScreenFade;
        Renderer.CinematicMaterials = _cinematicPlayback.MaterialState;
        Renderer.CinematicOffsetEffect = _cinematicPlayback.OffsetEffect;
        Renderer.CinematicColorBalance = _cinematicPlayback.ColorBalance;
        long audioUpdateStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _cinematicAudio?.Update(_cinematicPlayback.Time, _cinematicPlayback.Playing);
        Renderer.StatCinematicAudioMilliseconds = (float)
            System.Diagnostics.Stopwatch.GetElapsedTime(audioUpdateStartedAt).TotalMilliseconds;
        if (_cinematicPlayback.Playing && _soundPlaying)
        {
            Raylib_cs.Raylib.StopSound(_sound);
            _soundPlaying = false;
        }
        if (Renderer.IsFlying) _followCinematicCamera = false;
        if (_followCinematicCamera && !Renderer.IsFlying) ApplyCinematicCamera(_cinematicPlayback);
    }

}
