using System.Text.Json;
using JadeView;

internal static class ArchiveStartupScriptAudit
{
    private sealed record FunctionSummary(string Key, string Name, string Status, int? StackOffsetCount,
        int? NodeCount, int? DebugNodeCount, int? ConstantBytes, int? LocalVariableCount,
        int? TrailingBytes, object[] KeyInstructions, object[] DirectCalls,
        object[] ConstantArrayTables, object[] Instructions,
        string? Diagnostic)
    {
        public object[] CallBindings { get; init; } = [];
        public object[] ConstantBranches { get; init; } = [];
        public object[] ScalarConstantAssignments { get; init; } = [];
        public object[] TrackMutations { get; init; } = [];
    }
    private sealed record VariableSummary(string Key, string Name, string Status,
        string DefaultValuesStatus, VariableDescriptorSummary[] Descriptors,
        PcAiGlobalVariableDefault[] Defaults,
        string? Diagnostic)
    {
        public object[] InitializationCallbacks { get; init; } = [];
        public PcAiGlobalSaveLayout? SaveLayout { get; init; }
    }
    private sealed record InstanceVariableSnapshot(string? Key, string Status,
        object[] Values, string? Diagnostic);
    private sealed record VariableDescriptorSummary(int Offset, string Name, ushort Type, string TypeName,
        ushort Flags, int ArrayDimensions, uint ArrayLength);

    internal static void Write(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        ArchiveStartupScriptInventory inventory = ArchiveStartupScriptReader.Read(archive);
        VariableSummary[] variables = inventory.ModelResources
            .Where(resource => resource.TypeTag == ".ova")
            .Select(ReadVariableSummary)
            .ToArray();
        Dictionary<int, string> variableNamesByOffset = variables
            .Where(variable => variable.Status == "Parsed")
            .SelectMany(variable => variable.Descriptors.Select(descriptor =>
                (descriptor.Offset, descriptor.Name)))
            .GroupBy(variable => variable.Offset)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name);
        Dictionary<uint, string> functionNamesByKey = inventory.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Status == "Resolved" && resource.Name != null)
            .GroupBy(resource => resource.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name!);
        FunctionSummary[] functions = inventory.ModelResources
            .Where(resource => resource.TypeTag == ".ofc")
            .Select(ReadFunctionSummary)
            .ToArray();
        InstanceVariableSnapshot instanceVariableSnapshot = ReadInstanceVariableSnapshot(
            inventory.InstanceVariablesKey);
        string outputPath = Path.Combine(output, "archive-startup-script-inventory.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 3,
            Archive = Path.GetFileName(archive.PathName),
            StartKey = inventory.StartKey.ToString("X8"),
            inventory.Status,
            inventory.InstanceName,
            ModelKey = inventory.ModelKey?.ToString("X8"),
            inventory.ModelName,
            InstanceVariablesKey = inventory.InstanceVariablesKey?.ToString("X8"),
            inventory.InstanceVariablesName,
            InstanceVariableSnapshot = instanceVariableSnapshot,
            Resources = inventory.ModelResources.Select(resource => new
            {
                Key = resource.Key.ToString("X8"),
                resource.TypeTag,
                resource.Status,
                resource.Name
            }),
            Variables = variables,
            Functions = functions,
            inventory.Diagnostic,
            Scope = "Static links and serialized AI nodes from BIG startKey plus the archive-stored instance-variable snapshot; callbacks are not executed and snapshot values are not live runtime state."
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"GLOBAL AI STARTUP: {outputPath}; startKey={inventory.StartKey:X8}; "
            + $"status={inventory.Status}; modelLinks={inventory.ModelResources.Length}; "
            + $"functionBodies={functions.Count(function => function.Status == "Parsed")}/{functions.Length}");

        FunctionSummary ReadFunctionSummary(ArchiveScriptResourceReference resource)
        {
            if (resource.Status != "Resolved")
                return new(resource.Key.ToString("X8"), resource.Name ?? "unresolved", resource.Status,
                    null, null, null, null, null, null, [], [], [], [], "Function resource link did not resolve uniquely.");
            Entry[] entries = archive.Entries.Where(entry => entry.Key == resource.Key).Take(2).ToArray();
            if (entries.Length != 1)
                return new(resource.Key.ToString("X8"), resource.Name ?? "unresolved", "AmbiguousResource",
                    null, null, null, null, null, null, [], [], [], [], "Function key no longer resolves uniquely.");

            try
            {
                byte[] payload = archive.Read(entries[0], 16 * 1024 * 1024);
                if (!PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? parsed, out string error)
                    || parsed == null)
                    return new(resource.Key.ToString("X8"), resource.Name ?? entries[0].Name, "UnsupportedLayout",
                        null, null, null, null, null, null, [], [], [], [], error);

                object[] directCalls = PcAiDirectFunctionCallAnalyzer.Analyze(parsed, functionNamesByKey)
                    .Select(call => (object)new
                    {
                        call.CallNodeIndex,
                        call.EngineFunction,
                        call.ReferenceNodeIndex,
                        TargetKey = call.TargetKey is { } targetKey ? $"{targetKey:X8}" : null,
                        call.TargetName,
                        call.Status
                    }).ToArray();

                object[] constantArrayTables = PcAiConstantArrayTableAnalyzer.Analyze(parsed)
                    .Select(table => (object)new
                    {
                        table.FirstArrayOffset,
                        FirstArrayName = variableNamesByOffset.GetValueOrDefault(table.FirstArrayOffset),
                        table.SecondArrayOffset,
                        SecondArrayName = variableNamesByOffset.GetValueOrDefault(table.SecondArrayOffset),
                        table.FirstNodeIndex,
                        table.NodeStride,
                        PairCount = table.Pairs.Length,
                        Interpretation = "Repeated constant integer-array initializer candidate; expressions and branch conditions were not executed.",
                        Pairs = table.Pairs.Select((pair, index) => new
                        {
                            Index = index,
                            pair.FirstValue,
                            SecondValue = $"{pair.SecondValue:X8}"
                        }).ToArray()
                    }).ToArray();

                object[] keyInstructions = parsed.Nodes
                    .Where(node => node.SymbolName is "AITrackChange" or "AITrackStop" or "AITrackCurChange"
                        or "AITrackCurChangeNow" or "AITrackCurStop" or "AIExecute"
                        or "WORLoadList" or "WORActive" || node.BranchDescription != null)
                    .Select(node => (object)new
                    {
                        node.Index,
                        node.CategoryName,
                        node.SymbolName,
                        node.Value,
                        Value2 = $"{node.EncodedValue2:X4}",
                        node.BranchDescription,
                        GlobalVariableName = node.EncodedCategory is >= 0x14 and <= 0x17
                            && variableNamesByOffset.TryGetValue(node.Value, out string? variableName)
                            ? variableName : null,
                        EncodedTypeName = node.EncodedCategory == 0x01
                            ? PcAiNodeCatalog.TypeName(node.EncodedValue2) : null,
                        FunctionReferenceName = node.EncodedCategory == 0x01 && node.EncodedValue2 == 0x27
                            && functionNamesByKey.TryGetValue(unchecked((uint)node.Value), out string? functionName)
                            ? functionName : null
                    }).ToArray();
                object[] callBindings = PcAiFunctionCallBindingAnalyzer.Analyze(parsed,
                    node => ResolveVariableName(node, parsed))
                    .Select(binding => (object)new
                    {
                        binding.CallNodeIndex,
                        binding.Operation,
                        binding.ArgumentNodeIndex,
                        binding.ArgumentDescription,
                        binding.ResultTargetNodeIndex,
                        binding.ResultTargetDescription,
                        binding.Status,
                        binding.ResolvedIntegerValue,
                        binding.ResultAssignmentNodeIndex,
                        binding.ResolvedArgumentValue,
                        binding.ArgumentValueAssignmentNodeIndex,
                        binding.Evidence
                    }).ToArray();
                object[] constantBranches = PcAiConstantBranchAnalyzer.Analyze(parsed)
                    .Select(branch => (object)new
                    {
                        branch.CallNodeIndex,
                        branch.Operation,
                        branch.JumpNodeIndex,
                        branch.ConditionDescription,
                        branch.ConditionIsTrue,
                        branch.BranchTargetNodeIndex,
                        branch.FallthroughNodeIndex,
                        branch.ChosenNodeIndex,
                        branch.ChosenEdge,
                        branch.Evidence
                    }).ToArray();
                object[] scalarConstantAssignments = PcAiScalarConstantAssignmentAnalyzer.Analyze(parsed,
                    node => ResolveVariableName(node, parsed))
                    .Select(assignment => (object)new
                    {
                        assignment.TargetNodeIndex,
                        assignment.TargetDescription,
                        assignment.ValueNodeIndex,
                        assignment.Value,
                        assignment.AssignmentNodeIndex
                    }).ToArray();
                object[] trackMutations = PcAiTrackMutationAnalyzer.Analyze(parsed, functionNamesByKey)
                    .Select(mutation => (object)new
                    {
                        mutation.NodeIndex,
                        mutation.Operation,
                        mutation.TrackIndex,
                        TargetKey = mutation.TargetKey is { } targetKey ? $"{targetKey:X8}" : null,
                        mutation.TargetName,
                        mutation.Status,
                        Evidence = "Serialized AI-track mutation candidate; branches, stack expressions, and runtime dispatch are not evaluated."
                    }).ToArray();
                object[] instructions = parsed.Nodes.Select(node => (object)new
                {
                    node.Index,
                    node.CategoryName,
                    node.SymbolName,
                    node.Value,
                    Value2 = $"{node.EncodedValue2:X4}",
                    node.Flags,
                    node.BranchDescription,
                    GlobalVariableName = node.EncodedCategory is >= 0x14 and <= 0x17
                        && variableNamesByOffset.TryGetValue(node.Value, out string? variableName)
                        ? variableName : null,
                    EncodedTypeName = node.EncodedCategory == 0x01
                        ? PcAiNodeCatalog.TypeName(node.EncodedValue2) : null,
                    FunctionReferenceName = node.EncodedCategory == 0x01 && node.EncodedValue2 == 0x27
                        && functionNamesByKey.TryGetValue(unchecked((uint)node.Value), out string? functionName)
                        ? functionName : null
                }).ToArray();
                return new FunctionSummary(resource.Key.ToString("X8"), entries[0].Name, "Parsed",
                    parsed.StackOffsetCount, parsed.Nodes.Length, parsed.DebugNodes.Length,
                    parsed.LocalStringConstants.Length, parsed.LocalVariables.Length,
                    parsed.TrailingBytes.Length, keyInstructions, directCalls, constantArrayTables,
                    instructions, null)
                {
                    CallBindings = callBindings,
                    ConstantBranches = constantBranches,
                    ScalarConstantAssignments = scalarConstantAssignments,
                    TrackMutations = trackMutations
                };
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                return new(resource.Key.ToString("X8"), entries[0].Name, "ReadError",
                    null, null, null, null, null, null, [], [], [], [], exception.Message);
            }

            string? ResolveVariableName(PcAiInstruction node, PcSerializedAiFunction function)
            {
                if (node.EncodedCategory == 0x14)
                    return variableNamesByOffset.GetValueOrDefault(node.Value);
                if (node.EncodedCategory == 0x05)
                {
                    PcAiLocalVariable[] localMatches = function.LocalVariables
                        .Where(variable => variable.Offset == node.Value).Take(2).ToArray();
                    return localMatches.Length == 1 ? localMatches[0].Name : null;
                }
                return null;
            }
        }

        InstanceVariableSnapshot ReadInstanceVariableSnapshot(uint? key)
        {
            if (key is not { } instanceKey)
                return new(null, "Unavailable", [], "The startup AI does not link an instance-variable resource.");

            Entry[] entries = archive.Entries.Where(entry => entry.Key == instanceKey).Take(2).ToArray();
            if (entries.Length != 1)
                return new(instanceKey.ToString("X8"), entries.Length == 0 ? "MissingResource" : "AmbiguousResource",
                    [], "Instance-variable key does not resolve to one archive entry.");

            try
            {
                byte[] payload = archive.Read(entries[0], 16 * 1024 * 1024);
                CinematicScriptVariable[] values = PcVariableSnapshotReader.Read(payload, "instance");
                object[] serializedValues = values.Select(variable => (object)new
                {
                    variable.Name,
                    variable.Type,
                    variable.Value,
                    variable.IntegerArrayValues,
                    variable.ArrayValueCount
                }).ToArray();
                return new(instanceKey.ToString("X8"), "Parsed", serializedValues,
                    "These values are stored in the archive startup instance OVA, not the live or save-overridden game state.");
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException
                or OverflowException or ArgumentOutOfRangeException)
            {
                return new(instanceKey.ToString("X8"), "UnsupportedLayout", [], exception.Message);
            }
        }

        VariableSummary ReadVariableSummary(ArchiveScriptResourceReference resource)
        {
            if (resource.Status != "Resolved")
                return new(resource.Key.ToString("X8"), resource.Name ?? "unresolved", resource.Status,
                    "Unavailable", [], [], "Variable resource link did not resolve uniquely.");
            Entry[] entries = archive.Entries.Where(entry => entry.Key == resource.Key).Take(2).ToArray();
            if (entries.Length != 1)
                return new(resource.Key.ToString("X8"), resource.Name ?? "unresolved", "AmbiguousResource",
                    "Unavailable", [], [], "Variable key no longer resolves uniquely.");
            try
            {
                byte[] payload = archive.Read(entries[0], 16 * 1024 * 1024);
                if (!PcAiGlobalVariableCatalogReader.TryRead(payload,
                    out PcAiGlobalVariableDescriptor[] parsed, out string error))
                    return new(resource.Key.ToString("X8"), entries[0].Name, "UnsupportedLayout",
                        "Unavailable", [], [], error);
                VariableDescriptorSummary[] descriptors = parsed.Select(variable => new VariableDescriptorSummary(
                    variable.Offset, variable.Name, variable.Type, PcAiNodeCatalog.TypeName(variable.Type),
                    variable.Flags, variable.ArrayDimensions, variable.ArrayLength)).ToArray();
                try
                {
                    PcAiGlobalVariableDefault[] defaults = PcAiGlobalVariableDefaultReader.Read(payload, parsed);
                    string defaultsStatus = defaults.Length > 0
                        && defaults.All(value => value.ValueStatus == "NoSerializedDefaults")
                            ? "NoSerializedDefaults"
                            : "Parsed";
                    object[] initializationCallbacks = PcAiGlobalVariableDefaultReader
                        .ReadInitializationCallbackKeys(payload)
                        .Select((key, slot) =>
                        {
                            Entry[] callbackMatches = key is 0 or uint.MaxValue
                                ? []
                                : archive.Entries.Where(entry => entry.Key == key).Take(2).ToArray();
                            string callbackStatus = key is 0 or uint.MaxValue
                                ? "NullReference"
                                : callbackMatches.Length switch
                                {
                                    1 => "Resolved",
                                    0 => "MissingResource",
                                    _ => "AmbiguousResource"
                                };
                            return (object)new
                            {
                                Slot = slot,
                                Key = key.ToString("X8"),
                                Status = callbackStatus,
                                Name = callbackMatches.Length == 1 ? callbackMatches[0].Name : null
                            };
                        }).ToArray();
                    return new VariableSummary(resource.Key.ToString("X8"), entries[0].Name,
                        "Parsed", defaultsStatus, descriptors, defaults, null)
                    {
                        InitializationCallbacks = initializationCallbacks,
                        SaveLayout = PcAiGlobalSaveLayoutReader.Read(parsed)
                    };
                }
                catch (Exception exception) when (exception is InvalidDataException or OverflowException
                    or ArgumentOutOfRangeException)
                {
                    return new(resource.Key.ToString("X8"), entries[0].Name, "Parsed",
                        "UnsupportedLayout", descriptors, [], exception.Message);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                return new(resource.Key.ToString("X8"), entries[0].Name, "ReadError",
                    "Unavailable", [], [], exception.Message);
            }
        }
    }
}
