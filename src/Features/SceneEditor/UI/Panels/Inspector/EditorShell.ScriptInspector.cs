using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private readonly Dictionary<CinematicScriptOwnerId,
        (CinematicScriptOwner Owner, CinematicScriptCallbackStateEffect[] Effects)> _scriptInspectorEffectCache = [];
    private readonly Dictionary<CinematicScriptOwnerId,
        (SceneDocument Document, CinematicScriptOwner Owner, CinematicScriptMessageRoute[] Routes)> _scriptInspectorMessageRouteCache = [];

    private CinematicScriptOwner? ResolveSelectedScriptOwner()
    {
        if (Document == null || ActiveWorld == null || SelectedObject == null) return null;
        return CinematicScriptOwnerResolver.Resolve(SelectedObject.GaoKey, ActiveWorld.Key,
            Document.CinematicScriptOwners.Values);
    }

    private void DrawSelectedScriptOwner(CinematicScriptOwner owner)
    {
        string sourceWorld = Document?.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name
            ?? $"World {owner.WorldKey:X8}";
        DrawCategoryTitle("Script owner");
        ImGui.TextWrapped($"{owner.Name} · {owner.OwnerKey:X8}");
        ImGui.TextDisabled($"Source world: {sourceWorld} · brain {FormatScriptKey(owner.BrainKey)} · model {FormatScriptKey(owner.ScriptModelKey)}");
        ImGui.TextDisabled($"Events: {FormatScriptKey(owner.EventsKey)} · SceneEvents: {FormatScriptKey(owner.SceneEventsKey)}");
        ImGui.Separator();

        DrawScriptCallbackAssignments(owner);
        DrawScriptRuntimeEvidence(owner);
        DrawScriptMessageRoutes(owner);
        DrawScriptStateEffectCandidates(owner);
    }

    private static void DrawScriptRuntimeEvidence(CinematicScriptOwner owner)
    {
        if (!ImGui.CollapsingHeader("Runtime scheduling evidence")) return;

        DrawCinematicScriptSchedulerEvidence(owner);
        DrawCinematicEventSchedulerEvidence(owner);
        DrawCinematicScriptReferencedObjectCalls(owner);
    }

    private void DrawScriptMessageRoutes(CinematicScriptOwner owner)
    {
        CinematicScriptMessageRoute[] routes = GetScriptMessageRoutes(owner);
        if (!ImGui.CollapsingHeader($"Potential message routes ({routes.Length})")) return;
        if (routes.Length == 0)
        {
            ImGui.TextDisabled("No statically resolved MSGSend route to an indexed receiver with a matching queue reader was found.");
            return;
        }

        ImGui.TextWrapped("Source-derived links only. A route means a reachable callback sends this message to a resolved object with a matching queue reader; it does not prove the callback ran, the branch passed, or the message was delivered.");
        foreach (var route in routes)
        {
            string readerNames = string.Join(", ", route.Readers
                .Select(reader => $"{reader.FunctionName} ({reader.ReadApi})")
                .Distinct(StringComparer.Ordinal));
            string label = $"Message {route.MessageId} → {route.Receiver.Name} ({route.RecipientKey:X8})##script-route-{route.Receiver.WorldKey:X8}-{route.RecipientKey:X8}-{route.SendCall.Line}";
            if (!ImGui.TreeNode(label)) continue;

            ImGui.TextDisabled($"Sender: {route.SenderName} · callback {route.SendCall.FunctionName}");
            ImGui.TextDisabled($"Reader: {readerNames}");
            ImGui.TextDisabled($"Source: {Path.GetFileName(route.SendCall.Source)}:{route.SendCall.Line}");
            ImGui.TextDisabled($"Callback path: {route.SendCall.CallbackPath}");
            if (route.SendCall.BranchConditions.Length > 0)
                ImGui.TextWrapped("Path guards: " + string.Join(" and ", route.SendCall.BranchConditions
                    .Select(condition => condition.Kind == "else"
                        ? $"else of ({condition.Expression})" : $"if ({condition.Expression})")));
            ImGui.TextDisabled($"Arguments: {string.Join(", ", route.SendCall.Arguments)}");
            ImGui.TreePop();
        }
    }

    private CinematicScriptMessageRoute[] GetScriptMessageRoutes(CinematicScriptOwner owner)
    {
        if (Document == null) return [];
        if (_scriptInspectorMessageRouteCache.TryGetValue(owner.Id, out var cached)
            && ReferenceEquals(cached.Document, Document)
            && ReferenceEquals(cached.Owner, owner))
            return cached.Routes;

        CinematicScriptMessageRoute[] routes = CinematicScriptMessageFlowResolver.Resolve(owner,
            Document.CinematicScriptOwners.Values);
        _scriptInspectorMessageRouteCache[owner.Id] = (Document, owner, routes);
        return routes;
    }

    private static void DrawScriptCallbackAssignments(CinematicScriptOwner owner)
    {
        if (!ImGui.CollapsingHeader("Callback assignments", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (owner.CallbackSlots is { } callbackSlots)
        {
            DrawCategoryTitle($"AI track callbacks ({callbackSlots.Length})");
            foreach (var slot in callbackSlots)
            {
                string function = slot.FunctionKey == 0 ? "empty"
                    : slot.FunctionName ?? $"unmapped function {slot.FunctionKey:X8}";
                ImGui.BulletText($"Slot {slot.Index:D2} · {function}");
            }
            ImGui.TextWrapped("Iris copies the model slots into the live AI instance; AI_RunInstance calls a live slot when its callback delay expires. Track-change operations can replace that slot. This archive view does not know the instance's current track or timer.");
        }
        else
        {
            ImGui.TextDisabled("AI track callback assignments were not decoded.");
        }

        if (owner.EngineCallbackSlots is not { } engineCallbacks)
        {
            ImGui.TextDisabled("Optional engine-event callback assignments were not decoded.");
            return;
        }

        var configuredEngineCallbacks = engineCallbacks.Where(callback => callback.FunctionKey != 0).ToArray();
        DrawCategoryTitle($"Engine-event hooks ({configuredEngineCallbacks.Length})");
        if (configuredEngineCallbacks.Length == 0)
        {
            ImGui.TextDisabled("No engine-event hooks are assigned in this model OVA.");
            return;
        }

        foreach (var callback in configuredEngineCallbacks)
        {
            string function = callback.FunctionName ?? $"unmapped function {callback.FunctionKey:X8}";
            ImGui.BulletText($"{CinematicEngineCallbackCatalog.Describe(callback.EventId)} · {function}");
            DrawEngineCallbackDispatchSites(callback.EventId);
        }
        ImGui.TextWrapped("Call sites identify where Iris requests a dispatch. Instance flags, AI state, and call-site branches can still skip it; this archive preview does not replay those runtime gates.");
    }

    private static void DrawEngineCallbackDispatchSites(int eventId)
    {
        CinematicScriptCallbackDispatchSite[] dispatchSites =
            CinematicScriptReader.EngineDispatchSitesFor(eventId);
        foreach (var site in dispatchSites)
            DrawCinematicCallbackDispatchSite(site);
        if (dispatchSites.Length == 0)
            ImGui.TextDisabled(CinematicEngineCallbackCatalog.IsConfirmedByIrisCallSite(eventId)
                ? "No direct Iris call site was indexed for this event ID."
                : "The event name is corroborated by Jaded; Iris has no indexed call site for this ID.");
    }

    private void DrawScriptStateEffectCandidates(CinematicScriptOwner owner)
    {
        CinematicScriptCallbackStateEffect[] effects = GetScriptStateEffectCandidates(owner);
        if (!ImGui.CollapsingHeader($"Source-reachable state effects ({effects.Length})")) return;
        if (effects.Length == 0)
        {
            ImGui.TextDisabled("No supported state-changing engine calls were found in reachable callback source.");
            return;
        }

        ImGui.TextWrapped("Static source candidates only. Playback does not execute these callbacks or apply their effects.");
        ImGui.BeginChild($"ScriptStateEffects{owner.WorldKey:X8}{owner.OwnerKey:X8}",
            new Vector2(0, 280 * _scale), ImGuiChildFlags.Borders);
        foreach (var apiGroup in effects.GroupBy(effect => effect.Api, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (!ImGui.TreeNode($"{apiGroup.Key} ({apiGroup.Count()})##{owner.WorldKey:X8}{owner.OwnerKey:X8}{apiGroup.Key}"))
                continue;

            foreach (var effect in apiGroup)
            {
                string receiver = effect.ReceiverKey is { } receiverKey
                    ? $"receiver {receiverKey:X8}" : "receiver unresolved";
                ImGui.BulletText($"{Path.GetFileName(effect.Source)}:{effect.Line} · {effect.FunctionName} · {receiver}");
                ImGui.TextWrapped(effect.StateEffect);
                ImGui.TextDisabled($"Arguments: {string.Join(", ", effect.Arguments)}");
                if (effect.BranchConditions.Length > 0)
                    ImGui.TextWrapped("Path guards: " + string.Join(" and ", effect.BranchConditions
                        .Select(condition => condition.Kind == "else"
                            ? $"else of ({condition.Expression})" : $"if ({condition.Expression})")));
                ImGui.TextDisabled(effect.ReceiverEvidence);
            }

            ImGui.TreePop();
        }
        ImGui.EndChild();
    }

    private CinematicScriptCallbackStateEffect[] GetScriptStateEffectCandidates(CinematicScriptOwner owner)
    {
        if (_scriptInspectorEffectCache.TryGetValue(owner.Id, out var cached)
            && ReferenceEquals(cached.Owner, owner))
            return cached.Effects;

        var effects = CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner);
        _scriptInspectorEffectCache[owner.Id] = (owner, effects);
        return effects;
    }
}
