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

    private void DrawCinematicMessageGateCandidates(CinematicScriptOwner gateOwner)
    {
        if (Document == null) return;
        if (!ReferenceEquals(_messageGateCacheDocument, Document))
        {
            _messageGateCacheDocument = Document;
            _messageGateReferencesCache = null;
            _messageGateTrackCache = null;
            _messageGateAnalysisCache.Clear();
        }

        if (!_messageGateAnalysisCache.TryGetValue(gateOwner.Id, out var analysis))
        {
            _messageGateReferencesCache ??= Document.Worlds
                .SelectMany(world => world.Objects.Select(sceneObject => new CinematicScriptNamedReference(
                    world.Key, sceneObject.GaoKey, sceneObject.Name, IsObject: true)))
                .Concat(Document.CinematicScriptOwners.Values.Select(owner => new CinematicScriptNamedReference(
                    owner.WorldKey, owner.OwnerKey, owner.Name, IsObject: false)))
                .ToArray();
            _messageGateTrackCache ??= Document.CinematicMessageTracks
                .Concat(CinematicScriptMessageTrack.FromScriptEventTracks(
                    Document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks)))
                .Concat(CinematicScriptMessageTrack.FromCinematicClips(Document.CinematicClips.Values))
                .Concat(CinematicScriptMessageTrack.FromEmbeddedEventTracks(
                    Document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists)))
                .Concat(CinematicScriptMessageTrack.FromActorClips(Document.Worlds))
                .ToArray();
            var matches = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(gateOwner,
                _messageGateTrackCache, _messageGateReferencesCache);
            var engineMatches = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(gateOwner,
                _messageGateTrackCache, _messageGateReferencesCache);
            if (matches != null || engineMatches != null)
            {
                var differentContexts = CinematicScriptBehaviorAnalyzer.FindMessageGateContextMismatches(
                    gateOwner, _messageGateTrackCache, _messageGateReferencesCache) ?? [];
                var engineDifferentTargets =
                    CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateTargetMismatches(gateOwner,
                        _messageGateTrackCache, _messageGateReferencesCache) ?? [];
                var engineInt1MaskMismatches =
                    CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateInt1MaskMismatches(gateOwner,
                        _messageGateTrackCache, _messageGateReferencesCache) ?? [];
                analysis = new(matches ?? [], differentContexts, engineMatches ?? [], engineDifferentTargets,
                    engineInt1MaskMismatches);
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

        DrawCategoryTitle($"MSGSendEx writes matching this gate ({analysis.EngineMatches.Length})");
        ImGui.TextDisabled("Iris zero-initializes MSGSendEx messages and sets only sender, message ID, and float payload. These candidates match the configured queue target and message ID; they can pass the gate's Int1 test only when its mask is zero. Queue capacity and execution order remain runtime-dependent.");
        DrawEngineMessageGateRows(analysis.EngineMatches);

        if (analysis.EngineDifferentTargets.Length > 0)
        {
            DrawCategoryTitle($"MSGSendEx with matching ID but a different queue target ({analysis.EngineDifferentTargets.Length})");
            DrawEngineMessageGateRows(analysis.EngineDifferentTargets);
        }

        if (analysis.EngineInt1MaskMismatches.Length > 0)
        {
            DrawCategoryTitle($"MSGSendEx rejected by this gate's Int1 mask ({analysis.EngineInt1MaskMismatches.Length})");
            ImGui.TextDisabled("These sends match the gate's queue target and message ID, but MSGSendEx leaves Int1 at zero while this gate requires a nonzero bit overlap.");
            DrawEngineMessageGateRows(analysis.EngineInt1MaskMismatches);
        }

        void DrawMessageGateRows(CinematicScriptMessageGateCandidate[] candidates)
        {
            foreach (var candidate in candidates.Take(32))
            {
                var trackOwner = CinematicScriptOwnerResolver.Resolve(candidate.Track.OwnerKey,
                    candidate.Track.WorldKey, Document.CinematicScriptOwners.Values);
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
                    candidate.Track.WorldKey, Document.CinematicScriptOwners.Values);
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
    }
}
