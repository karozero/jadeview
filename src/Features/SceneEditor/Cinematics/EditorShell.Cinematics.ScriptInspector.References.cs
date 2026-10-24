using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
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
                    { description, DescribeCinematicWorldListBinding(call), embeddedTrack, soundBinding, actionLink }
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
}
