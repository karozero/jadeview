using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicOwnerExecutionLedger()
    {
        if (_cinematicPlayback is not { } playback || Document == null) return;

        if (_cinematicWorld is { } world)
            DrawCinematicHierarchyBindingDiagnostic(playback, world);
        DrawCinematicAnimationModeDiagnostic(playback);

        var entries = playback.OwnerExecutionLedger
            .Where(entry => Document.CinematicScriptOwners.ContainsKey(entry.OwnerId))
            .Where(MatchesExecutionLedgerSearch)
            .ToArray();
        DrawCategoryTitle($"Owner execution ledger ({entries.Length})");
        ImGui.TextWrapped("This is the preview's static execution map. It combines serialized RunningInit channels, source-reachable callback routes, explicit cinematic calls, and manual owner previews. Runtime gates remain unknown unless the game itself is observed.");
        if (entries.Length == 0)
        {
            ImGui.TextDisabled("No script-owned EVE routes match the current clip and search.");
            return;
        }

        if (!ImGui.BeginTable("CinematicOwnerExecutionLedger", 6,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new System.Numerics.Vector2(0, ImGui.GetContentRegionAvail().Y)))
            return;

        ImGui.TableSetupColumn("Owner", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Source route", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 104 * _scale);
        ImGui.TableSetupColumn("Channels / start", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("EVE state at playhead", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Runtime gate", ImGuiTableColumnFlags.WidthFixed, 112 * _scale);
        ImGui.TableHeadersRow();

        foreach (var entry in entries)
        {
            Document.CinematicScriptOwners.TryGetValue(entry.OwnerId, out var owner);
            string ownerName = owner?.Name ?? $"Owner {entry.OwnerId.OwnerKey:X8}";
            string rowId = $"{entry.OwnerId.WorldKey:X8}-{entry.OwnerId.OwnerKey:X8}-{entry.Route}-{entry.EventKey:X8}-{string.Join("_", entry.ChannelIndices)}";
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (ImGui.Selectable($"{ownerName} · {entry.OwnerId.OwnerKey:X8}##ledger-{rowId}",
                    _selectedCinematicScriptOwnerId == entry.OwnerId))
                _selectedCinematicScriptOwnerId = entry.OwnerId;
            DrawCopyNameContextMenu($"CinematicLedgerOwner##{rowId}", ownerName);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(ExecutionRouteName(entry.Route));
            DrawExecutionEvidenceTooltip(entry);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(PreviewStatusName(entry.PreviewStatus));

            ImGui.TableSetColumnIndex(3);
            string channels = entry.ChannelIndices.Length == 0
                ? "no decoded channels"
                : $"{entry.ChannelIndices.Length}: {string.Join(", ", entry.ChannelIndices)}";
            string start = entry.PreviewStartTime is { } previewStart ? $" · {previewStart:F2}s" : string.Empty;
            ImGui.TextUnformatted($"{channels}{start}");

            ImGui.TableSetColumnIndex(4);
            string channelStates = GetOwnerChannelStateSummary(entry, playback);
            ImGui.TextUnformatted(channelStates);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("State is evaluated from the reconstructed EVE preview clock at the current playhead. It is not a capture of the game's runtime scheduler.");

            ImGui.TableSetColumnIndex(5);
            ImGui.TextDisabled("Unknown");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Static source analysis does not evaluate live save flags, message queues, collision state, scheduler timing, or callback dispatch.");
        }

        ImGui.EndTable();
    }

    private static string GetOwnerChannelStateSummary(CinematicOwnerExecutionLedgerEntry entry,
        CinematicPlayback playback)
    {
        var channels = playback.OwnerChannelExecutions
            .Where(channel => channel.OwnerId == entry.OwnerId && channel.EventKey == entry.EventKey
                && entry.ChannelIndices.Contains(channel.ChannelIndex))
            .DistinctBy(channel => channel.ChannelIndex)
            .OrderBy(channel => channel.ChannelIndex)
            .ToArray();
        if (channels.Length == 0) return "not scheduled";

        return string.Join(" · ", channels.Select(channel =>
        {
            if (!channel.Clock.TryGetSnapshot(playback.Time, out var state))
                return $"{channel.ChannelIndex}: not started";
            string status = state.Paused ? "paused" : state.Running ? "running" : "stopped";
            return $"{channel.ChannelIndex}: {status} @ {state.LocalTime:F2}s";
        }));
    }

    private bool MatchesExecutionLedgerSearch(CinematicOwnerExecutionLedgerEntry entry)
    {
        string filter = _cinematicEveSearch.Trim();
        if (filter.Length == 0) return true;
        if (!Document!.CinematicScriptOwners.TryGetValue(entry.OwnerId, out var owner))
            return entry.OwnerId.OwnerKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase);
        return owner.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.OwnerId.OwnerKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.EventKey.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.ChannelIndices.Any(index => index.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExecutionRouteName(CinematicOwnerExecutionRoute route) => route switch
    {
        CinematicOwnerExecutionRoute.RunningInit => "RunningInit flag",
        CinematicOwnerExecutionRoute.CallbackReachability => "Callback → TRAPlay*",
        CinematicOwnerExecutionRoute.ManualPreview => "Manual preview control",
        CinematicOwnerExecutionRoute.CinematicEventCall => "Cinematic TRAPlay*",
        CinematicOwnerExecutionRoute.NestedEventCall => "Nested EVE TRAPlay*",
        _ => route.ToString()
    };

    private static string PreviewStatusName(CinematicOwnerPreviewStatus status) => status switch
    {
        CinematicOwnerPreviewStatus.Included => "Included",
        CinematicOwnerPreviewStatus.Disabled => "Disabled",
        CinematicOwnerPreviewStatus.Available => "Available",
        _ => status.ToString()
    };

    private static void DrawExecutionEvidenceTooltip(CinematicOwnerExecutionLedgerEntry entry)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextWrapped(entry.Provenance);
        foreach (var callbackSource in entry.CallbackSources)
        {
            string slot = callbackSource.SlotIndex is { } slotIndex
                ? $"slot {slotIndex} · {callbackSource.SlotFunctionName}"
                : CallbackBindingStatusName(callbackSource.Status);
            ImGui.Separator();
            ImGui.TextWrapped($"{slot} → {callbackSource.CallbackPath}");
        }
        ImGui.Separator();
        ImGui.TextWrapped(entry.Limit);
        ImGui.EndTooltip();
    }

    private static string CallbackBindingStatusName(CinematicCallbackSourceBindingStatus status) => status switch
    {
        CinematicCallbackSourceBindingStatus.SlotTableUnavailable => "callback slot table unavailable",
        CinematicCallbackSourceBindingStatus.NoMatchingSlot => "no callback slot matches this path",
        CinematicCallbackSourceBindingStatus.AmbiguousSlot => "callback path matches multiple slots",
        _ => status.ToString()
    };
}
