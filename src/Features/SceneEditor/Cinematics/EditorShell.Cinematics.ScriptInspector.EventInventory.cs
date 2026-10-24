using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
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
        var clipEventLists = lists.Where(list => list.EventKey == _cinematicPlayback?.Key).ToArray();
        var ownerEventLists = lists.Where(list => list.EventKey != _cinematicPlayback?.Key).ToArray();
        if (!ImGui.BeginTabBar("CinematicEveInventoryTabs")) return;
        DrawCinematicEventInventoryTab("Clip-linked", clipEventLists);
        DrawCinematicEventInventoryTab("Script owners", ownerEventLists);
        DrawCinematicEventInventoryTab("All", lists);
        ImGui.EndTabBar();
    }

    private void DrawCinematicEventInventoryTab(string category, CinematicEmbeddedEventTrackList[] lists)
    {
        int channelCount = lists.Sum(list => list.Tracks.Length);
        if (!ImGui.BeginTabItem($"{category} ({lists.Length} lists · {channelCount} channels)")) return;

        if (lists.Length == 0)
        {
            ImGui.TextDisabled("No SceneEvents lists match this category and search.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.TextDisabled(category switch
        {
            "Clip-linked" => "EVE lists keyed by the selected cinematic clip. These are the channels reached directly from this clip's source callbacks.",
            "Script owners" => "Separate SceneEvents lists owned by world objects. Their script gates determine whether the game starts them.",
            _ => "All SceneEvents lists in this cinematic world, grouped above by whether they belong to the selected clip or a separate script owner."
        });
        int shownChannels = 0;
        foreach (var list in lists)
            DrawCinematicEmbeddedEventTrackList(list, ref shownChannels, 512);
        if (channelCount > shownChannels)
            ImGui.TextDisabled($"Expanded channels shown: {shownChannels} / {channelCount}.");
        ImGui.EndTabItem();
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
            ? "This search filters script-owner tabs and the SceneEvents / EVE inventory."
            : $"{matchCount} matching SceneEvents list(s); matching owners are filtered in their category tabs.");
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
                DrawCinematicEventFunctionFlow(list, track);
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
}
