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
        if (_cinematicPlayers.Length == 0)
        {
            ImGui.TextWrapped("No shared cinematic animation clip was linked to multiple loaded actor rigs. Static script and EVE records remain available for inspection.");
            DrawCinematicStaticInspectorTabs();
            return;
        }

        SelectCinematicClip();
        if (!ImGui.BeginTabBar("CinematicInspectorWorkspace")) return;
        if (ImGui.BeginTabItem("Playback"))
        {
            DrawCinematicPlaybackWorkspace();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Execution"))
        {
            DrawCinematicOwnerExecutionLedger();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Scripts"))
        {
            DrawCinematicEventSearch();
            DrawCinematicScriptInspector();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("EVE & actions"))
        {
            DrawCinematicEventSearch();
            DrawCinematicEventWorkspace();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawCinematicStaticInspectorTabs()
    {
        if (!ImGui.BeginTabBar("CinematicInspectorWorkspace")) return;
        if (ImGui.BeginTabItem("Scripts"))
        {
            DrawCinematicEventSearch();
            DrawCinematicScriptInventoryWithoutPlayback();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("EVE inventory"))
        {
            DrawCinematicEventSearch();
            DrawCinematicEmbeddedEventTrackInventory();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawCinematicPlaybackWorkspace()
    {
        DrawCategoryTitle("Shared animation tracks");
        if (_cinematicPlayback is { EventTrackTransformCount: > 0 } playbackWithEventTracks)
            ImGui.TextWrapped($"{playbackWithEventTracks.EventTrackTransformCount} script-linked EVE transform channels are overlaid from the clip start. Callback gates and runtime activation are not simulated.");
        DrawPlaybackControls();
        DrawCinematicAudioControls();
        DrawCategoryTitle("Scene state");
        ImGui.Checkbox("Show hidden / conditional objects (debug)", ref Renderer.ShowGameHidden);
        DrawCinematicScreenFadeStatus();
        DrawCinematicCameraControls();
        DrawParticipants();
    }

    private void DrawCinematicEventWorkspace()
    {
        DrawCategoryTitle("Clip callback owners");
        DrawCallbackSceneEventOwnerControls();
        DrawCategoryTitle("Script-owned previews");
        DrawManualSceneEventOwnerControls();
        DrawCategoryTitle("Selected object commands");
        DrawCinematicSelectedObjectEventCommands();
        DrawCinematicEmbeddedEventTrackInventory();
    }

    private void DrawCallbackSceneEventOwnerControls()
    {
        if (Document == null || _cinematicPlayback is not { CallbackSceneEventOwners.Length: > 0 } playback)
            return;

        DrawCategoryTitle("Callback-linked EVE channels");
        ImGui.TextWrapped("These source-reachable callback owners start this clip's SceneEvents list. Select an owner to inspect its callback; disable it to omit linked tracks from preview and GLB export. The clip is decoded from that same SceneEvents resource, so its own flag writes cannot establish when it started. Configured Cine_Events delays remain available as an explicit estimate; callback dispatch and runtime gates are not executed.");
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
            if (owner != null && DrawCallbackSceneEventStartTiming(owner, ownerId, playback))
                return;
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

    private bool DrawCallbackSceneEventStartTiming(CinematicScriptOwner owner,
        CinematicScriptOwnerId ownerId, CinematicPlayback playback)
    {
        float? flagSignalTime = CinematicTimelineFlagAnalyzer.FindFirstExternalStartFlagTime(
            owner, playback.Key, playback.ScriptCalls);
        CinematicOwnerStartTimingEstimate? flagTiming = flagSignalTime is { } signalTime
            ? CinematicOwnerStartTimingEstimator.Estimate(owner, signalTime)
            : null;
        CinematicOwnerStartTimingEstimate configuredTiming = CinematicOwnerStartTimingEstimator.Estimate(owner, 0);
        bool hasOverride = _previewedCinematicSceneEventStartTimes.TryGetValue(ownerId, out float currentStartTime);
        bool useFlagTiming = flagTiming != null && !hasOverride;
        CinematicOwnerStartTimingEstimate timing = useFlagTiming ? flagTiming! : configuredTiming;
        bool hasUsefulTiming = useFlagTiming || timing.UsedConfiguredTiming && timing.PreviewStartTime > 0;
        if (!hasUsefulTiming && !hasOverride) return false;

        string buttonLabel = hasOverride
            ? $"Reset start to 0s##callback-start-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"
            : $"Use {(useFlagTiming ? "flag" : "configured")} start {timing.PreviewStartTime:F2}s##callback-start-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}";
        if (!ImGui.SmallButton(buttonLabel))
        {
            if (ImGui.IsItemHovered())
            {
                string timingEvidence = hasOverride
                    ? $"Current preview start is {currentStartTime:F3}s. Reset restores the time-zero preview."
                : timing.Evidence + (useFlagTiming
                        ? " The independent selected-clip flag write does not prove this owner activated or its callback ran."
                        : " This is an explicit preview offset; it does not prove the owner callback ran at time zero.");
                ImGui.SetTooltip(timingEvidence);
            }
            return false;
        }

        if (hasOverride) _previewedCinematicSceneEventStartTimes.Remove(ownerId);
        else _previewedCinematicSceneEventStartTimes[ownerId] = timing.PreviewStartTime;
        RebuildCinematicPlayersForScriptSelection(playback);
        return true;
    }

    private void DrawSceneEventOwnerGateEvidence(CinematicScriptOwner owner,
        CinematicScriptOwnerId ownerId)
    {
        CinematicPlayback? playback = _cinematicPlayback;
        var externalStateCalls = CinematicTimelineFlagAnalyzer.ExternalTimelineCalls(owner,
            playback?.Key, playback?.ScriptStateCalls);
        var evaluation = CinematicStartGateEvaluator.Evaluate(owner, _cinematicWorld?.Objects,
            externalStateCalls, playback?.Time ?? 0);
        if (evaluation.Gates.Length > 0 || evaluation.Branch != null)
        {
            if (ImGui.TreeNode($"Start gates: {evaluation.Status}##typed-scene-event-gates-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"))
            {
                ImGui.TextDisabled("Preview evidence only; this does not prove game-side owner activation or callback dispatch.");
                if (playback != null && externalStateCalls == null)
                    ImGui.TextDisabled("This owner's SceneEvents writes are excluded from its own start-gate state; prior save/runtime flags are unknown.");
                if (evaluation.Branch is { } branch)
                {
                    string branchLabel = branch.Status switch
                    {
                        CinematicStartGateBranchStatus.PredicatesBypassed => "Predicate branch: bypassed",
                        CinematicStartGateBranchStatus.PredicatesEvaluated => "Predicate branch: evaluated",
                        _ => "Predicate branch: unknown"
                    };
                    ImGui.TextDisabled(branchLabel);
                    ImGui.TextWrapped(branch.Evidence);
                    foreach (string input in branch.RequiredInputs)
                        ImGui.BulletText(input);
                }
                foreach (var gate in evaluation.Gates)
                {
                    Vector4 statusColor = gate.Status switch
                    {
                        CinematicStartGateStatus.Satisfied => new(0.30f, 0.88f, 0.52f, 1),
                        CinematicStartGateStatus.Rejected => new(0.94f, 0.35f, 0.32f, 1),
                        _ => new(0.62f, 0.66f, 0.67f, 1)
                    };
                    ImGui.TextColored(statusColor, $"{gate.Status}: {gate.Name}");
                    ImGui.Indent();
                    ImGui.TextWrapped(gate.Evidence);
                    if (gate.RequiredInputs.Length > 0)
                    {
                        ImGui.TextDisabled("Required inputs:");
                        foreach (string input in gate.RequiredInputs) ImGui.BulletText(input);
                    }
                    if (gate.InitialActivationForecast is { } forecast)
                    {
                        ImGui.TextDisabled($"Initial activation forecast · serialized state 0x{forecast.SerializedStateFlags:X8} → runtime flags2 0x{forecast.InitialRuntimeFlags2:X8}");
                        foreach (var path in forecast.Paths)
                        {
                            string eligibility = path.Eligibility == GameScriptActivationEligibility.Eligible
                                ? "eligible" : "blocked";
                            ImGui.BulletText($"{path.WorldMode}: {eligibility} — {path.Reason}");
                            if (path.EotMembership is { } membership)
                            {
                                string tables = membership.TableNames.Length == 0
                                    ? "no EOT update table"
                                    : string.Join(", ", membership.TableNames);
                                ImGui.TextDisabled($"If Iris activates the GAO, identity flags 0x{membership.IdentityFlags:X8} select: {tables}.");
                            }
                        }
                        ImGui.TextWrapped(forecast.Scope);
                    }
                    ImGui.Unindent();
                }
                ImGui.TreePop();
            }

            DrawCinematicOwnerInterruptionGateEvidence(owner, ownerId);
            return;
        }

        string[] evidence = CinematicScriptBehaviorAnalyzer.AnalyzeStartGates(owner,
            _cinematicWorld?.Objects);
        if (evidence.Length == 0)
        {
            string[] dispatchEvidence = CinematicEventSchedulerAnalyzer.Analyze(owner);
            ImGui.TextDisabled(dispatchEvidence.Length > 1
                ? dispatchEvidence[1]
                : "No source-defined Cine_Events start gate is available for this owner.");
            DrawCinematicOwnerInterruptionGateEvidence(owner, ownerId);
            return;
        }

        ImGui.TextDisabled(evidence[0]);
        if (evidence.Length <= 1
            || !ImGui.TreeNode($"Gate evidence ({evidence.Length - 1})##scene-event-gates-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"))
        {
            DrawCinematicOwnerInterruptionGateEvidence(owner, ownerId);
            return;
        }

        foreach (string detail in evidence.Skip(1)) ImGui.BulletText(detail);
        ImGui.TreePop();
        DrawCinematicOwnerInterruptionGateEvidence(owner, ownerId);
    }

    private void DrawCinematicOwnerInterruptionGateEvidence(CinematicScriptOwner owner,
        CinematicScriptOwnerId ownerId)
    {
        CinematicPlayback? playback = _cinematicPlayback;
        var evaluation = CinematicInterruptionGateEvaluator.Evaluate(owner,
            _cinematicWorld?.Objects, playback?.ScriptStateCalls, playback?.Time ?? 0);
        if (evaluation.EnabledMask is not { } mask) return;

        if (!ImGui.TreeNode($"Exit gates · 0x{mask:X} · {evaluation.Status}##cine-exit-gates-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"))
            return;
        ImGui.TextWrapped(evaluation.Scope);
        foreach (var gate in evaluation.Gates)
        {
            Vector4 statusColor = gate.Status switch
            {
                CinematicStartGateStatus.Satisfied => new(0.30f, 0.88f, 0.52f, 1),
                CinematicStartGateStatus.Rejected => new(0.94f, 0.35f, 0.32f, 1),
                _ => new(0.62f, 0.66f, 0.67f, 1)
            };
            ImGui.TextColored(statusColor, $"{gate.Status}: {gate.Name}");
            ImGui.Indent();
            ImGui.TextWrapped(gate.Evidence);
            foreach (string input in gate.RequiredInputs) ImGui.BulletText(input);
            ImGui.Unindent();
        }
        ImGui.TextDisabled("Predicate snapshot only; this does not trigger TRAStopAll or predict callback polling time.");
        ImGui.TreePop();
    }

    private void DrawManualSceneEventOwnerControls()
    {
        if (Document == null || _cinematicPlayback is not { PreviewableSceneEventOwners.Length: > 0 } playback)
            return;

        DrawCategoryTitle("Script-owned EVE preview");
        ImGui.TextWrapped("These owners have separate SceneEvents lists in the cinematic or paired gameplay world. Preview includes their channels in playback and synchronized export. Flag buttons use matching writes in the selected clip; message buttons show serialized queue candidates. Neither proves that runtime gates passed or that a message stayed in the queue.");
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
            DrawSceneEventOwnerActionContribution(playback, ownerId);
            float? flagStartTime = owner == null ? null
                : CinematicTimelineFlagAnalyzer.FindFirstExternalStartFlagTime(owner,
                    playback.Key, playback.ScriptCalls);
            float? messageStartTime = owner == null ? null
                : FindCinematicMessageGatePreviewTime(owner, playback);
            CinematicOwnerStartTimingEstimate? flagTiming = owner != null && flagStartTime is { } flagTime
                ? CinematicOwnerStartTimingEstimator.Estimate(owner, flagTime) : null;
            CinematicOwnerStartTimingEstimate? messageTiming = owner != null && messageStartTime is { } messageTime
                ? CinematicOwnerStartTimingEstimator.Estimate(owner, messageTime) : null;
            if (DrawSceneEventPreviewStartButton(ownerId, playback, flagTiming,
                    "Flag", "First matching saved-flag write in the selected clip")) return;
            if (DrawSceneEventPreviewStartButton(ownerId, playback, messageTiming,
                    "Message", "First serialized message sent to the configured start-gate queue with a matching ID and Int1 mask")) return;
            ImGui.SameLine();
            bool enabled = _previewedCinematicSceneEventOwners.Contains(ownerId);
            if (!ImGui.Checkbox($"Preview##script-eve-preview-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}",
                    ref enabled))
                continue;

            if (enabled) _previewedCinematicSceneEventOwners.Add(ownerId);
            else
            {
                _previewedCinematicSceneEventOwners.Remove(ownerId);
                _previewedCinematicSceneEventStartTimes.Remove(ownerId);
            }
            RebuildCinematicPlayersForScriptSelection(playback);
            return;
        }
    }

    private void DrawSceneEventOwnerActionContribution(CinematicPlayback playback,
        CinematicScriptOwnerId ownerId)
    {
        string treeId = $"Body action coverage##script-eve-actions-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}";
        if (!ImGui.TreeNode(treeId)) return;

        string[] contributions = GetCinematicOwnerActionContributions(playback, ownerId);
        if (contributions.Length == 0)
        {
            ImGui.TextDisabled("Adding this owner's SceneEvents did not resolve an outstanding body-action request.");
        }
        else
        {
            foreach (string contribution in contributions) ImGui.BulletText(contribution);
            ImGui.TextDisabled("This compares serialized action requests with the current preview plus this owner. Runtime owner gates and dispatch are not verified.");
        }

        ImGui.TreePop();
    }

    private bool DrawSceneEventPreviewStartButton(CinematicScriptOwnerId ownerId,
        CinematicPlayback playback, CinematicOwnerStartTimingEstimate? timing,
        string sourceLabel, string description)
    {
        if (timing is not { } estimate) return false;
        float time = estimate.PreviewStartTime;
        ImGui.SameLine();
        if (!ImGui.SmallButton($"{sourceLabel} {time:F2}s##script-eve-{sourceLabel}-{ownerId.WorldKey:X8}-{ownerId.OwnerKey:X8}"))
        {
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Preview this owner's serialized SceneEvents from {time:F2}s, aligned to the {description.ToLowerInvariant()}. {estimate.Evidence} Other runtime gates and live game state are not simulated.");
            return false;
        }

        _previewedCinematicSceneEventOwners.Add(ownerId);
        _previewedCinematicSceneEventStartTimes[ownerId] = time;
        RebuildCinematicPlayersForScriptSelection(playback);
        return true;
    }

    private void RebuildCinematicPlayersForScriptSelection(CinematicPlayback previousPlayback)
    {
        if (Document == null || _cinematicWorld == null) return;
        _messageGatePreviewTimeCache.Clear();
        _cinematicAiTrackMutationCache.Clear();
        _cinematicOwnerActionContributionCache.Clear();
        uint clipKey = previousPlayback.Key;
        float playhead = previousPlayback.Time;
        bool resumePlayback = previousPlayback.Playing;
        int cameraIndex = previousPlayback.CameraIndex;
        previousPlayback.Deactivate();

        _cinematicPlayers = CinematicPlayback.Discover(Document, _cinematicWorld, Resolver, Archive?.Entries,
            _disabledCinematicCallbackOwners, _previewedCinematicSceneEventOwners,
            _previewedCinematicSceneEventStartTimes);
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
        Renderer.CinematicLights = null;
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
        Renderer.CinematicLights = _cinematicPlayback.LightState;
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
        bool shouldFollowCamera = _followCinematicCamera && !Renderer.IsFlying;
        if (_cinematicCameraWasFollowing && !shouldFollowCamera)
            Renderer.SynchronizeManualCameraOrientation();
        _cinematicCameraWasFollowing = shouldFollowCamera;
        if (shouldFollowCamera) ApplyCinematicCamera(_cinematicPlayback);
    }

}
