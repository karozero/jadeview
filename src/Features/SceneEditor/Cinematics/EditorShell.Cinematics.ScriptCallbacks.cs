using System.Numerics;
using ImGuiNET;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicRuntimeCallbackGatePreview(CinematicScriptOwner owner,
        CinematicScriptChannelTransfer[] transfers)
    {
        CinematicPlayback? playback = _cinematicPlayback;
        if (playback == null) return;

        CinematicScriptChannelTransfer[] branchGatedTransfers = transfers
            .Where(transfer => transfer.BranchConditions.Length > 0).ToArray();
        if (branchGatedTransfers.Length == 0
            || !ImGui.CollapsingHeader($"Callback branch state ({branchGatedTransfers.Length})"))
            return;

        WorldData? cinematicWorld = _cinematicWorld;
        SceneObject? ownerObject = cinematicWorld == null ? null
            : cinematicWorld.Objects.Concat(cinematicWorld.Actors.SelectMany(actor => actor.Members))
                .FirstOrDefault(sceneObject => sceneObject.GaoKey == owner.OwnerKey);
        ImGui.TextDisabled($"Selected playhead: {playback.Time:F2}s");
        ImGui.TextDisabled("Source branch predicates and recovered callback-route gates are evaluated from OVA values and selected-clip EVE state. This preview applies only uniquely resolved linked-channel transfers and paired visibility changes; world scheduling, queue capacity/order, and unsupported runtime inputs remain outside the simulation.");

        if (Archive != null && ImGui.SmallButton(_cinematicSavedFlagSnapshot?.Status == "Loaded"
                ? "Change save slot" : "Load save slot"))
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Title = "Select a Beyond Good & Evil save slot for the opened BF",
                Filter = "BGE save slot (*.sav)|*.sav|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                PcAiSavedFlagSnapshot snapshot = PcAiGlobalSavedFlagSnapshotReader.Read(Archive, dialog.FileName);
                if (snapshot.Status == "Loaded")
                {
                    _cinematicSavedFlagSnapshot = snapshot;
                    playback.BindSavedFlagAssumptions(GetCinematicSavedFlagStates());
                    playback.Seek(playback.Time);
                    _statusMessage = $"Loaded {Path.GetFileName(snapshot.Path)} · {snapshot.Flags.Count} global flags";
                }
                else
                {
                    SetError(snapshot.Diagnostic);
                }
            }
        }
        if (_cinematicSavedFlagSnapshot is { Status: "Loaded" } loadedSave)
        {
            ImGui.TextDisabled($"Save input: {Path.GetFileName(loadedSave.Path)} · AI block {loadedSave.ExpectedAiBufferLength} bytes · {loadedSave.Flags.Count} flags");
            ImGui.TextWrapped(loadedSave.Diagnostic);
        }
        else
        {
            ImGui.TextDisabled("No save slot loaded. Unknown saved flags remain unknown unless set by timeline writes or scenario overrides.");
        }

        CinematicScriptReachableEngineCall[] reachableCalls =
            CinematicScriptReader.ReachableEngineCalls(owner);
        CinematicScriptCallbackStateEffect[] savedFlagEffects =
            CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner, reachableCalls)
                .Where(effect => CinematicScriptPredicateEvaluator.SavedFlagIndices(
                    effect.BranchConditions).Length > 0)
                .ToArray();
        int[] savedFlagIndices = CinematicScriptPredicateEvaluator.SavedFlagIndices(
                branchGatedTransfers.SelectMany(transfer => transfer.BranchConditions)
                    .Concat(savedFlagEffects.SelectMany(effect => effect.BranchConditions)))
            .Distinct().Order().ToArray();
        if (savedFlagIndices.Length > 0)
        {
            ImGui.TextDisabled("Explicit save-slot bits provide initial state; scenario overrides take precedence. Click a flag to cycle between the loaded value and an override.");
            bool assumptionsChanged = false;
            foreach (int flagIndex in savedFlagIndices)
            {
                bool hasOverride = _cinematicSavedFlagAssumptions.TryGetValue(flagIndex, out bool? assumedValue);
                bool slotValue = false;
                bool hasSlotValue = _cinematicSavedFlagSnapshot != null
                    && _cinematicSavedFlagSnapshot.Flags.TryGetValue(flagIndex, out slotValue);
                string state = hasOverride
                    ? $"override {(assumedValue == true ? "set" : "clear")}"
                    : hasSlotValue ? $"slot {(slotValue ? "set" : "clear")}" : "unknown";
                if (ImGui.SmallButton($"Flag {flagIndex}: {state}"))
                {
                    assumptionsChanged = true;
                    if (!hasOverride)
                        _cinematicSavedFlagAssumptions[flagIndex] = hasSlotValue ? !slotValue : false;
                    else if (assumedValue == false)
                        _cinematicSavedFlagAssumptions[flagIndex] = true;
                    else
                        _cinematicSavedFlagAssumptions.Remove(flagIndex);
                }
            }
            if (_cinematicSavedFlagAssumptions.Count > 0 && ImGui.SmallButton("Clear saved-flag assumptions"))
            {
                _cinematicSavedFlagAssumptions.Clear();
                assumptionsChanged = true;
            }

            if (assumptionsChanged)
            {
                playback.BindSavedFlagAssumptions(GetCinematicSavedFlagStates());
                playback.Seek(playback.Time);
            }

            ImGui.Separator();
            ImGui.TextDisabled("Source callback effects guarded by these saved flags:");
            foreach (CinematicScriptCallbackStateEffect effect in savedFlagEffects.Take(32))
            {
                CinematicScriptBranchEvaluation result = CinematicScriptRuntimeBranchEvaluator.Evaluate(
                    owner, ownerObject, effect.BranchConditions, playback.Time, playback.ScriptStateCalls,
                    GetCinematicSavedFlagStates());
                string target = effect.ReceiverKey is { } receiverKey
                    ? $"target {receiverKey:X8}"
                    : effect.ReceiverEvidence;
                ImGui.BulletText($"{effect.FunctionName} · {effect.Api} · {result.Value.ToString().ToLowerInvariant()}");
                ImGui.TextWrapped(string.Join("; ", effect.BranchConditions.Select(branch =>
                    branch.Kind == "else" ? $"else ({branch.Expression})" : branch.Expression)));
                ImGui.TextDisabled($"{target} · {effect.Source}:{effect.Line} · {effect.ExecutionStatus}; only resolved visibility effects linked to a channel handoff are previewed.");
            }
            if (savedFlagEffects.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {savedFlagEffects.Length} saved-flag callback effects.");
        }

        foreach (CinematicScriptChannelTransfer transfer in branchGatedTransfers.Take(32))
        {
            CinematicScriptBranchEvaluation pathResult = CinematicScriptRuntimeBranchEvaluator.EvaluateCallbackPath(
                owner, ownerObject, transfer.CallbackPathSteps, playback.Time, playback.ScriptStateCalls,
                GetCinematicSavedFlagStates());
            CinematicScriptBranchEvaluation branchResult = CinematicScriptRuntimeBranchEvaluator.Evaluate(
                owner, ownerObject, transfer.BranchConditions, playback.Time, playback.ScriptStateCalls,
                GetCinematicSavedFlagStates(),
                transfer.ReceiverGuardAliases.ToDictionary(alias => alias, _ => (long?)1));
            CinematicScriptBranchValue combinedValue = CinematicScriptRuntimeBranchEvaluator.And(
                pathResult.Value, branchResult.Value);
            Vector4 color = combinedValue switch
            {
                CinematicScriptBranchValue.True => new(0.30f, 0.88f, 0.52f, 1),
                CinematicScriptBranchValue.False => new(0.94f, 0.35f, 0.32f, 1),
                _ => new(0.62f, 0.66f, 0.67f, 1)
            };
            string status = combinedValue switch
            {
                CinematicScriptBranchValue.True => "route and predicate true",
                CinematicScriptBranchValue.False => "route or predicate false",
                _ => "route or predicate unknown"
            };
            ImGui.TextColored(color, $"{transfer.FunctionName} · {status}");
            ImGui.Indent();
            ImGui.TextWrapped(pathResult.Evidence);
            ImGui.TextWrapped(branchResult.Evidence);
            foreach (string condition in pathResult.Conditions.Concat(branchResult.Conditions))
                ImGui.BulletText(condition);
            ImGui.TextDisabled($"When the resolved route and guards are true, playback transfers {transfer.OvaField} channel {transfer.SourceChannel} → {transfer.TargetName} channel {transfer.TargetChannel}. Other callback timing and scheduler behavior remain unsimulated.");
            ImGui.Unindent();
        }
        if (branchGatedTransfers.Length > 32)
            ImGui.TextDisabled($"Showing 32 of {branchGatedTransfers.Length} branch-gated transfers.");
    }

    private Dictionary<int, bool?> GetCinematicSavedFlagStates()
    {
        var states = _cinematicSavedFlagSnapshot?.Flags.ToDictionary(pair => pair.Key,
                pair => (bool?)pair.Value) ?? [];
        foreach ((int flagIndex, bool? value) in _cinematicSavedFlagAssumptions)
            states[flagIndex] = value;
        return states;
    }

    private static CinematicScriptChannelTransfer[] DrawCinematicScriptReferencedObjectCalls(
        CinematicScriptOwner owner)
    {
        CinematicScriptReferencedObjectCall[] objectCalls =
            CinematicScriptReferencedObjectCallResolver.Resolve(owner);
        if (objectCalls.Length > 0
            && ImGui.CollapsingHeader($"Source calls using linked objects ({objectCalls.Length})"))
        {
            ImGui.TextDisabled("Calls in source-reachable callbacks that use an object reference resolved from this owner's serialized OVA. They show possible scripted operations, not proof of execution or live object state.");
            foreach (var operation in objectCalls.Take(64))
            {
                string target = operation.TargetName ?? $"{operation.ObjectKey:X8}";
                string arguments = string.Join(", ", operation.Arguments);
                string alias = operation.AliasVariable == null
                    ? ""
                    : $" via {operation.AliasVariable} assigned at line {operation.AssignmentLine}";
                ImGui.BulletText($"{operation.VariableName}{alias} → {target} · {operation.Api}({arguments})");
                ImGui.TextDisabled($"{operation.FunctionName} · {operation.Source}:{operation.Line} · path: {operation.CallbackPath}");
                if (operation.Branches.Length > 0)
                    ImGui.TextWrapped($"Source conditions: {string.Join("; ", operation.Branches)}");
            }
            if (objectCalls.Length > 64)
                ImGui.TextDisabled($"Showing 64 of {objectCalls.Length} source calls.");
        }

        CinematicScriptChannelTransfer[] channelTransfers =
            CinematicScriptChannelTransferAnalyzer.Analyze(owner);
        if (channelTransfers.Length == 0
            || !ImGui.CollapsingHeader($"Linked morph-channel handoffs ({channelTransfers.Length})"))
            return channelTransfers;

        ImGui.TextDisabled("Source-backed transfer patterns copy channel progression from the callback owner to a uniquely resolved object. Model functions without a recovered dispatch path are shown as configured candidates only.");
        foreach (CinematicScriptChannelTransfer transfer in channelTransfers.Take(32))
        {
            ImGui.BulletText($"{transfer.OvaField} → {transfer.TargetName} · channel {transfer.SourceChannel} → {transfer.TargetChannel}");
            string visibility = transfer.ClearsInvisibleFlag
                ? $" · clears invisible flag at {transfer.InvisibleFlagLine}"
                : "";
            string sourcePath = transfer.SourcePathResolved
                ? "source call path found"
                : "source call path unresolved";
            ImGui.TextDisabled($"{transfer.FunctionName} · {transfer.Source}:{transfer.GetterLine} → {transfer.SetterLine}{visibility} · {sourcePath} · {transfer.CallbackPath}");
            if (transfer.BranchConditions.Length > 0)
                ImGui.TextWrapped($"Source conditions: {string.Join("; ", transfer.BranchConditions.Select(branch => branch.Kind == "else" ? $"else ({branch.Expression})" : branch.Expression))}");
        }
        if (channelTransfers.Length > 32)
            ImGui.TextDisabled($"Showing 32 of {channelTransfers.Length} channel handoffs.");
        return channelTransfers;
    }

    private static void DrawCinematicScriptSchedulerEvidence(CinematicScriptOwner owner)
    {
        EditorUi.DrawCategoryTitle("World AI callback scheduling");
        ImGui.TextDisabled($"Serialized GAO flags: 0x{owner.SerializedGaoFlags:X8} · state flags: 0x{owner.SerializedStateFlags:X8}");
        ImGui.TextDisabled("Runtime interpretation follows the retail Iris source. The earlier Jaded engine has different activation gates; use it as architectural context, not as a retail scheduler substitute.");
        foreach (string finding in CinematicScriptSchedulerAnalyzer.Analyze(owner))
            ImGui.TextWrapped(finding);
    }

    private static void DrawCinematicEventSchedulerEvidence(CinematicScriptOwner owner)
    {
        string[] findings = CinematicEventSchedulerAnalyzer.Analyze(owner);
        if (findings.Length == 0) return;

        EditorUi.DrawCategoryTitle("World EVE event dispatch");
        foreach (string finding in findings) ImGui.TextWrapped(finding);
    }

    private void DrawUnmappedCinematicCallbacks(IEnumerable<CinematicScriptOwner> owners)
    {
        CinematicUnmappedCallback[] unmappedCallbacks = CinematicScriptCallbackCoverage.FindUnmapped(owners);
        if (unmappedCallbacks.Length == 0) return;

        var callbackKeys = unmappedCallbacks.GroupBy(callback => callback.Slot.FunctionKey)
            .OrderBy(group => group.Key).ToArray();
        if (!ImGui.CollapsingHeader(
                $"Unmapped callback IDs ({callbackKeys.Length} keys · {unmappedCallbacks.Length} slots)")) return;

        ImGui.TextDisabled("These OVA slots contain configured keys absent from the bundled Iris name table. Their behavior and runtime execution are unknown.");
        foreach (var callbackGroup in callbackKeys.Take(40))
        {
            ImGui.BulletText($"{callbackGroup.Key:X8} · {callbackGroup.Count()} slot(s)");
            string ownerNames = string.Join(", ", callbackGroup.Select(callback => callback.Owner.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            ImGui.TextWrapped($"  Owners: {ownerNames}");
        }
        if (callbackKeys.Length > 40)
            ImGui.TextDisabled($"Showing 40 of {callbackKeys.Length} distinct callback IDs.");
    }
}
