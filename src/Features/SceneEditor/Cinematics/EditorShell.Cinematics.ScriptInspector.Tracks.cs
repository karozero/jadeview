using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
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
}
