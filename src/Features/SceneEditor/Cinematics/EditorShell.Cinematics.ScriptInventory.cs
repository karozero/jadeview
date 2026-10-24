using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private Archive? _startupInventoryArchive;
    private ArchiveStartupScriptInventory? _startupInventory;
    private readonly Dictionary<uint, (PcSerializedAiFunction? Function, string Error)> _startupFunctions = [];
    private readonly Dictionary<uint, (PcAiGlobalVariableDescriptor[] Variables, string Error)> _startupVariableCatalogs = [];
    private readonly Dictionary<uint, (uint[] CallbackKeys, string Error)> _startupVariableCallbackKeys = [];
    private readonly Dictionary<uint, PcAiDirectFunctionCall[]> _startupCallSites = [];
    private readonly Dictionary<uint, PcAiFunctionCallPath[]> _startupCallPaths = [];
    private readonly Dictionary<uint, PcAiConstantArrayTable[]> _startupConstantArrayTables = [];
    private bool _startupVariableCatalogsLoaded;
    private (uint FunctionKey, int FirstNodeIndex, uint ResourceKey)? _selectedStartupResourceKey;

    private void DrawArchiveStartupScriptInventory()
    {
        if (Archive == null || !ImGui.CollapsingHeader("BF global AI startup resources")) return;

        if (!ReferenceEquals(_startupInventoryArchive, Archive) || _startupInventory == null)
        {
            _startupInventoryArchive = Archive;
            _startupInventory = ArchiveStartupScriptReader.Read(Archive);
            _startupFunctions.Clear();
            _startupVariableCatalogs.Clear();
            _startupVariableCallbackKeys.Clear();
            _startupCallSites.Clear();
            _startupCallPaths.Clear();
            _startupConstantArrayTables.Clear();
            _startupVariableCatalogsLoaded = false;
            _selectedStartupResourceKey = null;
        }

        ArchiveStartupScriptInventory inventory = _startupInventory;
        if (!_startupVariableCatalogsLoaded)
        {
            foreach (ArchiveScriptResourceReference variableResource in inventory.ModelResources
                         .Where(resource => resource.TypeTag == ".ova" && resource.Status == "Resolved"))
                _ = GetArchiveStartupVariableCatalog(variableResource.Key);
            _startupVariableCatalogsLoaded = true;
        }
        DrawCategoryTitle($"BIG startKey · {inventory.StartKey:X8} · {inventory.Status}");
        if (inventory.InstanceName != null)
            ImGui.TextWrapped($"Instance: {inventory.InstanceName}");
        if (inventory.ModelKey is { } modelKey)
            ImGui.TextWrapped($"Model: {inventory.ModelName ?? "unresolved"} ({modelKey:X8})");
        if (inventory.InstanceVariablesKey is { } variablesKey)
            ImGui.TextWrapped($"Instance variables: {inventory.InstanceVariablesName ?? "unresolved"} ({variablesKey:X8})");

        ArchiveScriptResourceReference[] scriptResources = inventory.ModelResources
            .Where(resource => resource.TypeTag is ".ofc" or ".ova")
            .ToArray();
        if (scriptResources.Length > 0)
        {
            DrawCategoryTitle($"Global model links ({scriptResources.Length})");
            foreach (ArchiveScriptResourceReference resource in scriptResources)
            {
                string name = resource.Name ?? "unresolved";
                if (resource.TypeTag == ".ova" && resource.Status == "Resolved")
                {
                    if (ImGui.TreeNode($".ova · {name} · {resource.Key:X8}##global-ai-vars-{resource.Key:X8}"))
                    {
                        DrawArchiveStartupVariableCatalog(resource.Key);
                        ImGui.TreePop();
                    }
                    continue;
                }

                if (resource.TypeTag != ".ofc" || resource.Status != "Resolved")
                {
                    ImGui.BulletText($"{resource.TypeTag} · {name} · {resource.Key:X8} · {resource.Status}");
                    continue;
                }

                if (!ImGui.TreeNode($".ofc · {name} · {resource.Key:X8}##global-ai-{resource.Key:X8}")) continue;
                DrawArchiveStartupFunction(resource.Key);
                ImGui.TreePop();
            }
        }
        else if (inventory.ModelKey != null)
        {
            ImGui.TextDisabled("No .ova/.ofc model links were resolved.");
        }

        if (inventory.Diagnostic != null) ImGui.TextDisabled(inventory.Diagnostic);
        ImGui.TextDisabled("This is the archive's global startup-AI resource chain. It is not a runtime dispatch trace and does not identify the currently active world.");
    }

    private void DrawArchiveStartupFunction(uint functionKey)
    {
        (PcSerializedAiFunction? Function, string Error) parsedFunction = GetArchiveStartupFunction(functionKey);
        if (parsedFunction.Function is not { } function)
        {
            ImGui.TextDisabled(parsedFunction.Error);
            return;
        }

        ImGui.TextDisabled($"Local stack: {function.StackOffsetCount} · nodes: {function.Nodes.Length} · debug nodes: {function.DebugNodes.Length} · constants: {function.LocalStringConstants.Length} bytes · locals: {function.LocalVariables.Length}");
        ImGui.TextDisabled("Encoded nodes only. A narrow constant-return call / Not / conditional-jump pattern is source-resolved; other branches and callback dispatch are not evaluated.");
        DrawArchiveStartupConstantArrayTables(functionKey, function);
        PcAiDirectFunctionCall[] directCalls = GetArchiveStartupCallSites(functionKey, function);
        if (directCalls.Length > 0 && ImGui.TreeNode($"Direct AIExecute calls ({directCalls.Length})##global-ai-calls-{functionKey:X8}"))
        {
            foreach (PcAiDirectFunctionCall call in directCalls)
            {
                string target = call.TargetKey is { } key
                    ? $"0x{key:X8} · {call.TargetName ?? call.Status}"
                    : call.Status;
                ImGui.BulletText($"node {call.CallNodeIndex} → {target}");
            }
            ImGui.TreePop();
        }
        PcAiFunctionCallPath[] callPaths = GetArchiveStartupCallPaths(functionKey, function);
        if (callPaths.Length > 0 && ImGui.TreeNode($"Reachable AI function flows ({callPaths.Length})##global-ai-paths-{functionKey:X8}"))
        {
            ImGui.TextDisabled("AIExecute is a synchronous nested call; AITrackCurChangeNow replaces the current track function. Returned control sentinels can alter scheduling. These links remain static, and most arguments/branches are unresolved.");
            foreach (PcAiFunctionCallPath path in callPaths)
            {
                string functions = string.Join(" → ", path.FunctionNames);
                string callNodes = path.CallNodeIndexes.Length > 0
                    ? $" · {string.Join(" → ", path.TransferKinds.Zip(path.CallNodeIndexes,
                        (kind, nodeIndex) => $"{kind}@{nodeIndex}"))}" : string.Empty;
                ImGui.BulletText($"{functions}{callNodes} · {path.Status}");
            }
            ImGui.TreePop();
        }
        Dictionary<uint, string> linkedFunctionNames = _startupInventory?.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Status == "Resolved" && resource.Name != null)
            .GroupBy(resource => resource.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name!) ?? [];
        PcAiTrackMutation[] trackMutations = PcAiTrackMutationAnalyzer.Analyze(function, linkedFunctionNames);
        if (trackMutations.Length > 0
            && ImGui.TreeNode($"AI track mutation candidates ({trackMutations.Length})##global-ai-track-mutations-{functionKey:X8}"))
        {
            ImGui.TextDisabled("These serialized calls can replace or stop AI update tracks. They are not a runtime trace; branch results, dynamic operands, and scheduler timing are not executed.");
            foreach (PcAiTrackMutation mutation in trackMutations)
            {
                string track = mutation.TrackIndex is { } trackIndex
                    ? $"track {trackIndex}"
                    : mutation.Operation is "AITrackCurChange" or "AITrackCurChangeNow" or "AITrackCurStop"
                        ? "current track"
                        : "track unresolved";
                string target = mutation.TargetKey is { } targetKey
                    ? $" → {mutation.TargetName ?? $"OFC {targetKey:X8}"} ({targetKey:X8})"
                    : string.Empty;
                ImGui.BulletText($"node {mutation.NodeIndex} · {mutation.Operation} · {track}{target} · {mutation.Status}");
            }
            ImGui.TreePop();
        }
        DrawArchiveStartupCallBindings(function);
        DrawArchiveStartupConstantBranches(function);
        DrawArchiveStartupScalarConstantAssignments(function);
        if (function.LocalVariables.Length > 0 && ImGui.TreeNode($"Local variable metadata ({function.LocalVariables.Length})"))
        {
            foreach (PcAiLocalVariable variable in function.LocalVariables)
                ImGui.BulletText($"{variable.Name} · offset {variable.Offset} · type {variable.Type}");
            ImGui.TreePop();
        }

        if (function.LocalStringConstants.Length > 0 && ImGui.TreeNode($"Local constant bytes ({function.LocalStringConstants.Length})"))
        {
            ImGui.TextWrapped(Convert.ToHexString(function.LocalStringConstants));
            ImGui.TreePop();
        }

        ImGui.TextDisabled($"Trailing uninterpreted bytes: {function.TrailingBytes.Length}");
        ImGui.BeginChild($"GlobalAiNodes{functionKey:X8}", new Vector2(0, 240 * _scale), ImGuiChildFlags.Borders);
        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(function.Nodes.Length);
            while (clipper.Step())
            {
                for (int nodeIndex = clipper.DisplayStart; nodeIndex < clipper.DisplayEnd; nodeIndex++)
                {
                    PcAiInstruction node = function.Nodes[nodeIndex];
                    string symbol = FormatArchiveStartupNodeSymbol(node, function);
                    string branch = node.BranchDescription is { Length: > 0 } description
                        ? $" · {description}" : string.Empty;
                    ImGui.TextDisabled($"{node.Index:D4} · {node.CategoryName}{symbol} · value {node.Value} (0x{node.Value:X8}) · value2 0x{node.EncodedValue2:X4} · flags 0x{node.Flags:X2}{branch}");
                }
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
    }

    private void DrawArchiveStartupCallBindings(PcSerializedAiFunction function)
    {
        PcAiFunctionCallBinding[] bindings = PcAiFunctionCallBindingAnalyzer.Analyze(function,
            node => ResolveArchiveStartupVariableName(node, function));
        if (bindings.Length == 0
            || !ImGui.TreeNode($"Recognized engine-call operand patterns ({bindings.Length})"))
            return;

        foreach (PcAiFunctionCallBinding binding in bindings)
        {
            string arguments = binding.ArgumentDescription == "no arguments"
                ? string.Empty
                : binding.ResolvedArgumentValue is int argumentValue
                    ? $"{binding.ArgumentDescription} = {argumentValue} (from node {binding.ArgumentValueAssignmentNodeIndex})"
                    : binding.ArgumentDescription;
            string result = binding.ResultTargetDescription == null
                ? string.Empty
                : binding.ResolvedIntegerValue is int integerValue
                    ? $" → {binding.ResultTargetDescription} = {integerValue}"
                    : $" → {binding.ResultTargetDescription}";
            ImGui.BulletText($"node {binding.CallNodeIndex} · {binding.Operation}({arguments}){result} · {binding.Status}");
            string evidence = binding.Status == "ResolvedConstantExpression"
                ? "Constant expression from source-confirmed return value and serialized integer operators; branches and runtime execution are not evaluated."
                : "Serialized adjacent-operand pattern; not VM execution.";
            ImGui.TextDisabled($"{evidence} Contract: {binding.Evidence}.");
        }
        ImGui.TreePop();
    }

    private void DrawArchiveStartupConstantBranches(PcSerializedAiFunction function)
    {
        PcAiConstantBranch[] branches = PcAiConstantBranchAnalyzer.Analyze(function);
        if (branches.Length == 0
            || !ImGui.TreeNode($"Source-backed constant branches ({branches.Length})"))
            return;

        foreach (PcAiConstantBranch branch in branches)
        {
            ImGui.BulletText($"node {branch.CallNodeIndex} · {branch.ConditionDescription} · "
                + $"jump node {branch.JumpNodeIndex} → {branch.ChosenEdge} node {branch.ChosenNodeIndex}");
            ImGui.TextDisabled($"Only this constant condition is resolved; other runtime state is unknown. {branch.Evidence}.");
        }
        ImGui.TreePop();
    }

    private void DrawArchiveStartupScalarConstantAssignments(PcSerializedAiFunction function)
    {
        PcAiScalarConstantAssignment[] assignments = PcAiScalarConstantAssignmentAnalyzer.Analyze(function,
            node => ResolveArchiveStartupVariableName(node, function));
        if (assignments.Length == 0
            || !ImGui.TreeNode($"Serialized scalar int assignments ({assignments.Length})"))
            return;

        foreach (PcAiScalarConstantAssignment assignment in assignments)
            ImGui.BulletText($"node {assignment.AssignmentNodeIndex} · "
                + $"{assignment.TargetDescription} = {assignment.Value}");
        ImGui.TextDisabled("These are serialized Affect patterns. They show writes only when control reaches the nodes; they are not live values.");
        ImGui.TreePop();
    }

    private string? ResolveArchiveStartupVariableName(PcAiInstruction node, PcSerializedAiFunction function)
    {
        if (node.EncodedCategory == 0x14)
        {
            PcAiGlobalVariableDescriptor[] matches = _startupVariableCatalogs.Values
                .SelectMany(catalog => catalog.Variables)
                .Where(variable => variable.Offset == node.Value).Take(2).ToArray();
            return matches.Length == 1 ? matches[0].Name : null;
        }

        if (node.EncodedCategory == 0x05)
        {
            PcAiLocalVariable[] matches = function.LocalVariables
                .Where(variable => variable.Offset == node.Value).Take(2).ToArray();
            return matches.Length == 1 ? matches[0].Name : null;
        }

        return null;
    }

    private void DrawArchiveStartupConstantArrayTables(uint functionKey, PcSerializedAiFunction function)
    {
        PcAiConstantArrayTable[] tables = GetArchiveStartupConstantArrayTables(functionKey, function);
        if (tables.Length == 0
            || !ImGui.TreeNode($"Repeated integer-array initializers ({tables.Length})##global-ai-arrays-{functionKey:X8}"))
            return;

        ImGui.TextDisabled("Pattern candidates from repeated serialized writes; stack expressions and branch conditions are not executed.");
        foreach (PcAiConstantArrayTable table in tables)
        {
            string firstArrayName = ResolveArchiveStartupGlobalVariableName(table.FirstArrayOffset);
            string secondArrayName = ResolveArchiveStartupGlobalVariableName(table.SecondArrayOffset);
            string tableLabel = $"{firstArrayName} ↔ {secondArrayName} · {table.Pairs.Length} entries";
            if (!ImGui.TreeNode($"{tableLabel}##global-ai-array-{functionKey:X8}-{table.FirstNodeIndex}")) continue;

            ImGui.TextDisabled($"Serialized initializer starts at node {table.FirstNodeIndex} and repeats every {table.NodeStride} nodes.");
            ImGui.BeginChild($"GlobalAiArrayPairs{functionKey:X8}{table.FirstNodeIndex}",
                new Vector2(0, Math.Min(220, table.Pairs.Length * 22) * _scale), ImGuiChildFlags.Borders);
            unsafe
            {
                var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clipper.Begin(table.Pairs.Length);
                while (clipper.Step())
                {
                    for (int pairIndex = clipper.DisplayStart; pairIndex < clipper.DisplayEnd; pairIndex++)
                    {
                        PcAiConstantArrayPair pair = table.Pairs[pairIndex];
                        string resourceButton = $"Inspect key##startup-key-{functionKey:X8}-{table.FirstNodeIndex}-{pairIndex}";
                        if (ImGui.SmallButton(resourceButton))
                            _selectedStartupResourceKey = (functionKey, table.FirstNodeIndex, pair.SecondValue);
                        ImGui.SameLine();
                        ImGui.TextDisabled($"{pairIndex:D3} · {pair.FirstValue} ↔ 0x{pair.SecondValue:X8}");
                    }
                }
                clipper.End();
                clipper.Destroy();
            }
            ImGui.EndChild();
            if (_selectedStartupResourceKey is { } selected
                && selected.FunctionKey == functionKey
                && selected.FirstNodeIndex == table.FirstNodeIndex)
                DrawStartupWorldListResourceCandidate(selected.ResourceKey);
            ImGui.TreePop();
        }
        ImGui.TreePop();
    }

    private void DrawStartupWorldListResourceCandidate(uint key)
    {
        ImGui.TextDisabled($"Archive-key candidate 0x{key:X8} · checking whether it is a PC WOL resource.");
        GameScriptWorldListResource? resource = GetScriptWorldListResource(key);
        if (resource == null)
        {
            ImGui.TextDisabled("No BF archive is open for resource lookup.");
            return;
        }

        if (resource.Descriptor is { } descriptor)
        {
            ImGui.TextWrapped($"PC WOL · {resource.EntryName} · ordered roots [{string.Join(", ", descriptor.OrderedWorldRootKeys.Select(root => root.ToString("X8")))}]. This is serialized resource metadata; no script call or runtime load is implied.");
            return;
        }

        ImGui.TextWrapped($"{resource.EntryName}: {resource.Diagnostic ?? resource.Status}. The candidate is not a decoded PC WOL list.");
    }

    private PcAiConstantArrayTable[] GetArchiveStartupConstantArrayTables(uint functionKey,
        PcSerializedAiFunction function)
    {
        if (_startupConstantArrayTables.TryGetValue(functionKey, out PcAiConstantArrayTable[]? cached))
            return cached;
        PcAiConstantArrayTable[] tables = PcAiConstantArrayTableAnalyzer.Analyze(function);
        _startupConstantArrayTables[functionKey] = tables;
        return tables;
    }

    private string ResolveArchiveStartupGlobalVariableName(int offset)
    {
        PcAiGlobalVariableDescriptor[] matches = _startupVariableCatalogs.Values
            .SelectMany(catalog => catalog.Variables)
            .Where(variable => variable.Offset == offset)
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0].Name : $"Global array 0x{offset:X8}";
    }

    private PcAiDirectFunctionCall[] GetArchiveStartupCallSites(uint functionKey,
        PcSerializedAiFunction function)
    {
        if (_startupCallSites.TryGetValue(functionKey, out PcAiDirectFunctionCall[]? cached)) return cached;
        Dictionary<uint, string> linkedFunctions = _startupInventory?.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Status == "Resolved" && resource.Name != null)
            .GroupBy(resource => resource.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name!) ?? [];
        PcAiDirectFunctionCall[] callSites = PcAiDirectFunctionCallAnalyzer.Analyze(function, linkedFunctions);
        _startupCallSites[functionKey] = callSites;
        return callSites;
    }

    private PcAiFunctionCallPath[] GetArchiveStartupCallPaths(uint functionKey,
        PcSerializedAiFunction function)
    {
        if (_startupCallPaths.TryGetValue(functionKey, out PcAiFunctionCallPath[]? cached)) return cached;
        Dictionary<uint, string> linkedFunctions = _startupInventory?.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Status == "Resolved" && resource.Name != null)
            .GroupBy(resource => resource.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name!) ?? [];
        string rootName = linkedFunctions.GetValueOrDefault(functionKey, $"OFC {functionKey:X8}");
        PcAiFunctionCallPath[] paths = PcAiFunctionCallGraphAnalyzer.Analyze(functionKey, rootName,
            function, linkedFunctions, targetKey => GetArchiveStartupFunction(targetKey).Function);
        _startupCallPaths[functionKey] = paths;
        return paths;
    }

    private void DrawArchiveStartupVariableCatalog(uint variableKey)
    {
        (PcAiGlobalVariableDescriptor[] Variables, string Error) catalog = GetArchiveStartupVariableCatalog(variableKey);
        if (catalog.Error.Length > 0)
        {
            ImGui.TextDisabled(catalog.Error);
            return;
        }

        ImGui.TextDisabled($"Iris AIVarElem descriptors: {catalog.Variables.Length} · offsets/types/names only; runtime values are unavailable.");
        DrawArchiveStartupVariableCallbacks(variableKey);
        foreach (PcAiGlobalVariableDescriptor variable in catalog.Variables)
        {
            string dimensions = variable.ArrayDimensions == 0
                ? string.Empty
                : $" · {variable.ArrayDimensions}D [{variable.ArrayLength}]";
            ImGui.BulletText($"{variable.Name} · offset 0x{variable.Offset:X8} · {PcAiNodeCatalog.TypeName(variable.Type)} · flags 0x{variable.Flags:X4}{dimensions}");
        }
    }

    private void DrawArchiveStartupVariableCallbacks(uint variableKey)
    {
        (uint[] CallbackKeys, string Error) callbacks = GetArchiveStartupVariableCallbackKeys(variableKey);
        if (callbacks.Error.Length > 0)
        {
            ImGui.TextDisabled(callbacks.Error);
            return;
        }

        DrawCategoryTitle($"OVA callback slots ({callbacks.CallbackKeys.Length})");
        ImGui.TextDisabled("Iris loads five per-track callback references after the defaults buffer. This is a static reference audit; callbacks are not executed.");
        for (int slotIndex = 0; slotIndex < callbacks.CallbackKeys.Length; slotIndex++)
        {
            uint callbackKey = callbacks.CallbackKeys[slotIndex];
            if (callbackKey is 0 or uint.MaxValue)
            {
                ImGui.BulletText($"Track {slotIndex} · empty");
                continue;
            }

            Entry[] matches = Archive?.Entries.Where(entry => entry.Key == callbackKey).Take(2).ToArray() ?? [];
            string target = matches.Length switch
            {
                1 => $"{matches[0].Name} · {callbackKey:X8}",
                0 => $"{callbackKey:X8} · no matching BIG resource",
                _ => $"{callbackKey:X8} · ambiguous BIG resource"
            };
            ImGui.BulletText($"Track {slotIndex} · {target}");
        }
    }

    private string FormatArchiveStartupNodeSymbol(PcAiInstruction node, PcSerializedAiFunction function)
    {
        if (node.SymbolName is { Length: > 0 } symbolName) return $" {symbolName}";
        if (node.EncodedCategory == 0x01)
        {
            string typeName = PcAiNodeCatalog.TypeName(node.EncodedValue2);
            string linkedFunction = node.EncodedValue2 == 0x27
                ? ResolveArchiveStartupFunctionName(unchecked((uint)node.Value))
                : string.Empty;
            return linkedFunction.Length > 0
                ? $" {typeName} → {linkedFunction}"
                : $" {typeName}";
        }

        if (node.EncodedCategory is >= 0x14 and <= 0x17)
        {
            PcAiGlobalVariableDescriptor[] matches = _startupVariableCatalogs.Values
                .SelectMany(catalog => catalog.Variables)
                .Where(variable => variable.Offset == node.Value)
                .Take(2).ToArray();
            return matches.Length == 1 ? $" {matches[0].Name}" : string.Empty;
        }

        if (node.EncodedCategory is >= 0x05 and <= 0x08)
        {
            PcAiLocalVariable[] matches = function.LocalVariables
                .Where(variable => variable.Offset == node.Value).Take(2).ToArray();
            return matches.Length == 1 ? $" {matches[0].Name}" : string.Empty;
        }
        return string.Empty;
    }

    private string ResolveArchiveStartupFunctionName(uint functionKey)
    {
        ArchiveScriptResourceReference[] matches = _startupInventory?.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Key == functionKey
                && resource.Status == "Resolved").Take(2).ToArray() ?? [];
        return matches.Length == 1 ? matches[0].Name ?? $"OFC {functionKey:X8}" : string.Empty;
    }

    private (PcAiGlobalVariableDescriptor[] Variables, string Error) GetArchiveStartupVariableCatalog(uint variableKey)
    {
        if (_startupVariableCatalogs.TryGetValue(variableKey, out var cached)) return cached;
        if (Archive == null) return Cache([], "No BF archive is open.");

        Entry[] matches = Archive.Entries.Where(entry => entry.Key == variableKey).Take(2).ToArray();
        if (matches.Length != 1)
            return Cache([], matches.Length == 0
                ? $"Global AI OVA {variableKey:X8} is missing from the archive."
                : $"Global AI OVA {variableKey:X8} has ambiguous archive entries.");

        try
        {
            byte[] payload = Archive.Read(matches[0], 16 * 1024 * 1024);
            return PcAiGlobalVariableCatalogReader.TryRead(payload,
                    out PcAiGlobalVariableDescriptor[] variables, out string error)
                ? Cache(variables, string.Empty)
                : Cache([], error);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Cache([], exception.Message);
        }

        (PcAiGlobalVariableDescriptor[] Variables, string Error) Cache(
            PcAiGlobalVariableDescriptor[] variables, string error)
        {
            var result = (variables, error);
            _startupVariableCatalogs[variableKey] = result;
            return result;
        }
    }

    private (uint[] CallbackKeys, string Error) GetArchiveStartupVariableCallbackKeys(uint variableKey)
    {
        if (_startupVariableCallbackKeys.TryGetValue(variableKey, out var cached)) return cached;
        if (Archive == null) return Cache([], "No BF archive is open.");

        Entry[] matches = Archive.Entries.Where(entry => entry.Key == variableKey).Take(2).ToArray();
        if (matches.Length != 1)
            return Cache([], matches.Length == 0
                ? $"Global AI OVA {variableKey:X8} is missing from the archive."
                : $"Global AI OVA {variableKey:X8} has ambiguous archive entries.");

        try
        {
            byte[] payload = Archive.Read(matches[0], 16 * 1024 * 1024);
            return Cache(PcAiGlobalVariableDefaultReader.ReadInitializationCallbackKeys(payload), string.Empty);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or OverflowException)
        {
            return Cache([], exception.Message);
        }

        (uint[] CallbackKeys, string Error) Cache(uint[] callbackKeys, string error)
        {
            var result = (callbackKeys, error);
            _startupVariableCallbackKeys[variableKey] = result;
            return result;
        }
    }

    private (PcSerializedAiFunction? Function, string Error) GetArchiveStartupFunction(uint functionKey)
    {
        if (_startupFunctions.TryGetValue(functionKey, out var cached)) return cached;
        if (Archive == null)
            return Cache(null, "No BF archive is open.");

        Entry[] matchingEntries = Archive.Entries.Where(entry => entry.Key == functionKey).Take(2).ToArray();
        if (matchingEntries.Length != 1)
            return Cache(null, matchingEntries.Length == 0
                ? $"OFC {functionKey:X8} is missing from the archive."
                : $"OFC {functionKey:X8} has ambiguous archive entries.");

        try
        {
            byte[] payload = Archive.Read(matchingEntries[0], 16 * 1024 * 1024);
            return PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? function,
                    out string error)
                ? Cache(function, string.Empty)
                : Cache(null, error);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Cache(null, exception.Message);
        }

        (PcSerializedAiFunction? Function, string Error) Cache(PcSerializedAiFunction? function, string error)
        {
            var result = (function, error);
            _startupFunctions[functionKey] = result;
            return result;
        }
    }
}
