using ImGuiNET;
using System.Globalization;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private SceneDocument? _messageGateCacheDocument;
    private CinematicScriptNamedReference[]? _messageGateReferencesCache;
    private CinematicScriptMessageTrack[]? _messageGateTrackCache;
    private readonly Dictionary<CinematicScriptOwnerId, CinematicScriptMessageGateAnalysis?>
        _messageGateAnalysisCache = [];
    private readonly Dictionary<(CinematicScriptOwnerId Owner, uint WorldKey, uint ClipKey,
        CinematicScheduledMessage[] ScheduledMessages), float?> _messageGatePreviewTimeCache = [];

    private void DrawCinematicMessageGateCandidates(CinematicScriptOwner gateOwner)
    {
        if (Document is not { } document || !EnsureMessageGateCache()) return;
        var messageTracks = _messageGateTrackCache!;
        var namedReferences = _messageGateReferencesCache!;

        if (!_messageGateAnalysisCache.TryGetValue(gateOwner.Id, out var analysis))
        {
            var matches = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(gateOwner,
                messageTracks, namedReferences);
            var engineMatches = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(gateOwner,
                messageTracks, namedReferences);
            var ownerQueueCandidates = CinematicScriptBehaviorAnalyzer.FindOwnerQueueMessageCandidates(
                gateOwner, messageTracks, namedReferences);
            if (matches != null || engineMatches != null || ownerQueueCandidates != null)
            {
                var differentContexts = CinematicScriptBehaviorAnalyzer.FindMessageGateContextMismatches(
                    gateOwner, messageTracks, namedReferences) ?? [];
                var engineDifferentTargets =
                    CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateTargetMismatches(gateOwner,
                        messageTracks, namedReferences) ?? [];
                var engineInt1MaskMismatches =
                    CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateInt1MaskMismatches(gateOwner,
                        messageTracks, namedReferences) ?? [];
                analysis = new(matches ?? [], differentContexts, engineMatches ?? [], engineDifferentTargets,
                    engineInt1MaskMismatches, ownerQueueCandidates ?? []);
            }
            _messageGateAnalysisCache[gateOwner.Id] = analysis;
        }
        if (analysis == null) return;

        DrawCategoryTitle($"Serialized message-gate matches ({analysis.Matches.Length})");
        if (analysis.Matches.Length == 0)
        {
            ImGui.TextDisabled("No decoded message matched both the queue reference and gate filter. Loaded actor action clips are scanned; main-actor slots, unresolved GAOs, and live callback delivery remain unavailable.");
        }
        else
        {
            ImGui.TextDisabled("Each match has the configured queue object as its execution context. It does not prove that the event ran before the gate checked or that the message passed the runtime queue limit.");
            DrawMessageGateRows(analysis.Matches);
        }

        if (analysis.DifferentContexts.Length > 0)
        {
            DrawCategoryTitle($"Same ID/mask, different execution context ({analysis.DifferentContexts.Length})");
            ImGui.TextDisabled("These serialized messages pass the ID and Int1 checks, but execute on another object, so they are not matches for this queue.");
            DrawMessageGateRows(analysis.DifferentContexts);
        }

        DrawCategoryTitle($"Literal engine-message sends matching this gate ({analysis.EngineMatches.Length})");
        ImGui.TextDisabled("MSGSendEx writes a zero Int1; MSGSendAll carries an explicit Int1. MSGSendEx and MSGSendAll enqueue on the current execution object's queue. These matches satisfy the configured queue, message ID and Int1 mask, but queue capacity and execution order remain runtime-dependent.");
        DrawEngineMessageGateRows(analysis.EngineMatches);

        if (analysis.OwnerQueueCandidates.Length > 0)
        {
            DrawCategoryTitle($"MSGSendAllOwner sends; registered queue unresolved ({analysis.OwnerQueueCandidates.Length})");
            var startupRegistration = CinematicScriptGlobalQueueCatalog.StartupRegistration;
            bool gateMatchesStartupOwner = analysis.OwnerQueueCandidates[0].GateQueueObject.Key
                == startupRegistration.StartupOwnerKey;
            string startupRelation = gateMatchesStartupOwner
                ? "The configured gate queue matches that startup owner key."
                : "The configured gate queue differs from the startup owner key and would require another registration.";
            ImGui.TextDisabled($"The calls match this gate's message ID and Int1. Iris sends them to {startupRegistration.QueueSymbol} via {startupRegistration.RegistrationApi}.");
            ImGui.TextDisabled($"Startup candidate: {startupRegistration.StartupOwnerName} ({startupRegistration.StartupOwnerKey:X8}) · {startupRegistration.StartupSource}:{startupRegistration.StartupLine}");
            ImGui.TextDisabled($"{startupRelation} World teardown clears the pointer at {startupRegistration.WorldTeardownSource}:{startupRegistration.WorldTeardownLine}.");
            ImGui.TextDisabled($"A runtime {startupRegistration.RegistrationApi} call can replace this candidate; the live queue recipient is not established here.");
            DrawOwnerQueueMessageRows(analysis.OwnerQueueCandidates);
        }

        if (analysis.EngineDifferentTargets.Length > 0)
        {
            DrawCategoryTitle($"Engine-message sends with matching ID but a different queue ({analysis.EngineDifferentTargets.Length})");
            DrawEngineMessageGateRows(analysis.EngineDifferentTargets);
        }

        if (analysis.EngineInt1MaskMismatches.Length > 0)
        {
            DrawCategoryTitle($"Engine-message sends rejected by this gate's Int1 mask ({analysis.EngineInt1MaskMismatches.Length})");
            ImGui.TextDisabled("These sends match the gate's queue and message ID, but their serialized Int1 does not overlap the configured mask.");
            DrawEngineMessageGateRows(analysis.EngineInt1MaskMismatches);
        }

        void DrawMessageGateRows(CinematicScriptMessageGateCandidate[] candidates)
        {
            foreach (var candidate in candidates.Take(32))
            {
                var trackOwner = CinematicScriptOwnerResolver.Resolve(candidate.Track.OwnerKey,
                    candidate.Track.WorldKey, document.CinematicScriptOwners.Values);
                string channelDescription = candidate.Track.ChannelIndex is { } channel
                    ? $"channel {channel} · " : "";
                string additionalSources = candidate.Sources.Length > 1
                    ? $" (+{candidate.Sources.Length - 1} duplicate source links)" : "";
                string label = $"{candidate.Track.Name} · {candidate.Track.LinkName} "
                    + $"{candidate.Track.EventKey:X8} · {channelDescription}{candidate.Time:F2}s "
                    + $"→ message {candidate.Message.Type}, Int1={candidate.Message.Int1} "
                    + $"context {candidate.ExecutionContext.Name} ({candidate.ExecutionContext.Key:X8}); "
                    + $"gate queue {candidate.QueueObject.Name} ({candidate.QueueObject.Key:X8})"
                    + additionalSources;
                if (trackOwner == null)
                {
                    ImGui.TextDisabled(label);
                    continue;
                }

                if (ImGui.Selectable($"{label}##messagegate{candidate.Track.WorldKey:X8}"
                        + $"{candidate.Track.OwnerKey:X8}{candidate.Track.EventKey:X8}"
                        + $"{candidate.Track.ChannelIndex}{candidate.Time:R}"))
                    _selectedCinematicScriptOwnerId = trackOwner.Id;
            }

            if (candidates.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {candidates.Length} messages.");
        }

        void DrawEngineMessageGateRows(CinematicScriptEngineMessageGateCandidate[] candidates)
        {
            foreach (var candidate in candidates.Take(32))
            {
                var trackOwner = CinematicScriptOwnerResolver.Resolve(candidate.Track.OwnerKey,
                    candidate.Track.WorldKey, document.CinematicScriptOwners.Values);
                string channelDescription = candidate.Track.ChannelIndex is { } channel
                    ? $"channel {channel} · " : "";
                string payload = unchecked((float)(int)candidate.Call.Arguments[1])
                    .ToString("0.###", CultureInfo.InvariantCulture);
                string additionalSources = candidate.Sources.Length > 1
                    ? $" (+{candidate.Sources.Length - 1} duplicate source links)" : "";
                string label = $"{candidate.Track.Name} · {candidate.Track.LinkName} "
                    + $"{candidate.Track.EventKey:X8} · {channelDescription}{candidate.Time:F2}s "
                    + $"→ MSGSendEx id {candidate.Call.Arguments[0]}, float {payload}; "
                    + $"queue target {candidate.TargetObject.Name} ({candidate.TargetObject.Key:X8}); "
                    + $"gate queue {candidate.QueueObject.Name} ({candidate.QueueObject.Key:X8})"
                    + additionalSources;
                if (trackOwner == null)
                {
                    ImGui.TextDisabled(label);
                    continue;
                }

                if (ImGui.Selectable($"{label}##msgsendex{candidate.Track.WorldKey:X8}"
                        + $"{candidate.Track.OwnerKey:X8}{candidate.Track.EventKey:X8}"
                        + $"{candidate.Track.ChannelIndex}{candidate.Time:R}{candidate.Call.Target:X8}"))
                    _selectedCinematicScriptOwnerId = trackOwner.Id;
            }

            if (candidates.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {candidates.Length} MSGSendEx commands.");
        }

        void DrawOwnerQueueMessageRows(CinematicScriptOwnerQueueMessageCandidate[] candidates)
        {
            foreach (var candidate in candidates.Take(32))
            {
                var trackOwner = CinematicScriptOwnerResolver.Resolve(candidate.Track.OwnerKey,
                    candidate.Track.WorldKey, document.CinematicScriptOwners.Values);
                string channelDescription = candidate.Track.ChannelIndex is { } channel
                    ? $"channel {channel} · " : "";
                _ = CinematicEngineFunctionCatalog.TryReadMessageGateFields(candidate.Call,
                    out int messageId, out int int1, out _);
                string label = $"{candidate.Track.Name} · {candidate.Track.LinkName} "
                    + $"{candidate.Track.EventKey:X8} · {channelDescription}{candidate.Time:F2}s "
                    + $"→ MSGSendAllOwner id {messageId}, Int1={int1}; registered queue target unresolved "
                    + $"(gate expects {candidate.GateQueueObject.Name} ({candidate.GateQueueObject.Key:X8}))";
                string selectableId = $"{label}##msgsendallowner{candidate.Track.WorldKey:X8}"
                    + $"{candidate.Track.OwnerKey:X8}{candidate.Track.EventKey:X8}"
                    + $"{candidate.Track.ChannelIndex}{candidate.Time:R}{candidate.Call.Target:X8}";
                if (trackOwner == null)
                    ImGui.TextDisabled(label);
                else if (ImGui.Selectable(selectableId))
                    _selectedCinematicScriptOwnerId = trackOwner.Id;

                if (candidate.PriorSameChannelRegistrations.Length == 0)
                {
                    ImGui.TextDisabled("No earlier MSGRegOwner found in this serialized EVE channel.");
                    continue;
                }

                foreach (var registration in candidate.PriorSameChannelRegistrations)
                {
                    string registeredOwner = registration.RegisteredOwnerCandidate is { } resolvedOwner
                        ? $"{resolvedOwner.Name} ({resolvedOwner.Key:X8})"
                        : $"unresolved key {registration.Call.Target:X8}";
                    ImGui.TextDisabled($"  Possible prior MSGRegOwner at {registration.Time:F2}s: "
                        + $"{registeredOwner}; another EVE channel or runtime order may overwrite it.");
                }
            }

            if (candidates.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {candidates.Length} MSGSendAllOwner calls.");
        }
    }

    private float? FindCinematicMessageGatePreviewTime(CinematicScriptOwner owner,
        CinematicPlayback playback)
    {
        if (!EnsureMessageGateCache()) return null;
        var cacheKey = (owner.Id, playback.WorldKey, playback.Key, playback.ScheduledMessages);
        if (_messageGatePreviewTimeCache.TryGetValue(cacheKey, out float? cachedTime))
            return cachedTime;

        float? messageTime = CinematicTimelineMessageGateAnalyzer.FindFirstMatchingMessageTime(owner,
            playback.WorldKey, playback.Key, playback.Duration,
            _messageGateTrackCache!, _messageGateReferencesCache!, playback.ScheduledMessages);
        _messageGatePreviewTimeCache[cacheKey] = messageTime;
        return messageTime;
    }

    private bool EnsureMessageGateCache()
    {
        if (Document == null) return false;
        if (!ReferenceEquals(_messageGateCacheDocument, Document))
        {
            _messageGateCacheDocument = Document;
            _messageGateReferencesCache = null;
            _messageGateTrackCache = null;
            _messageGateAnalysisCache.Clear();
            _messageGatePreviewTimeCache.Clear();
        }

        _messageGateReferencesCache ??= CinematicScriptNamedReferenceCatalog.ForDocument(Document);
        _messageGateTrackCache ??= Document.CinematicMessageTracks
            .Concat(CinematicScriptMessageTrack.FromScriptEventTracks(
                Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks)))
            .Concat(CinematicScriptMessageTrack.FromCinematicClips(Document.CinematicClips.Values))
            .Concat(CinematicScriptMessageTrack.FromEmbeddedEventTracks(
                Document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists)))
            .Concat(CinematicScriptMessageTrack.FromActorClips(Document.Worlds))
            .ToArray();
        return true;
    }
}
