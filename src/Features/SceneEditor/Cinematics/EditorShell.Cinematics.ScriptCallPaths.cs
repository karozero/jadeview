using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptCallPaths(CinematicScriptOwner owner)
    {
        var paths = CinematicScriptReader.NestedCallbackPaths(owner);
        var schedulerOperations = CinematicScriptReader.ReachableWorldSchedulerOperations(owner);
        var engineCallSummaries = CinematicScriptReader.SummarizeReachableEngineCalls(owner);
        var messageReaders = CinematicScriptReader.ReachableMessageReaders(owner);
        CinematicScriptMessageRoute[] messageRoutes = Document == null ? []
            : CinematicScriptMessageFlowResolver.Resolve(owner, Document.CinematicScriptOwners.Values);
        var sourceReferences = CinematicScriptReader.ReachableSourceReferences(owner);
        var archiveReferences = Document == null
            ? []
            : CinematicScriptNamedReferenceCatalog.ForDocument(Document);
        if (paths.Length == 0 && schedulerOperations.Length == 0 && engineCallSummaries.Length == 0
            && messageReaders.Length == 0 && messageRoutes.Length == 0 && sourceReferences.Length == 0) return;
        if (!ImGui.CollapsingHeader($"Iris source evidence: callbacks, API calls, MSGSend ({paths.Length})")) return;

        ImGui.TextDisabled("Routes start at this owner's configured callback slots and follow source-confirmed function dispatch within its script model.");
        ImGui.TextDisabled("register-callback:N means a source function registers a possible asynchronous callback for engine ID N; it does not prove the engine dispatched that ID.");
        var dispatchSites = CinematicScriptReader.DispatchSitesFor(paths);
        if (dispatchSites.Length > 0)
        {
            ImGui.TextDisabled("Iris engine call sites for the registered callback IDs:");
            foreach (var site in dispatchSites)
                ImGui.BulletText($"ID {site.CallbackId} · {site.Source}:{site.Line} · {site.Call}");
        }
        else
        {
            ImGui.TextDisabled("No literal engine dispatch call site for these registered callback IDs was found in the bundled Iris source.");
        }
        foreach (var path in paths.Take(64))
            ImGui.BulletText($"{path.FunctionName} · {path.Path}");
        if (paths.Length > 64)
            ImGui.TextDisabled($"Showing 64 of {paths.Length} reachable functions.");
        if (schedulerOperations.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled($"World-scheduler operations in reachable callbacks ({schedulerOperations.Length})");
            foreach (var operation in schedulerOperations)
            {
                ImGui.BulletText($"{operation.FunctionName} · WOR{operation.Operation}({operation.Arguments}) · {operation.Source}:{operation.Line}");
                if (operation.ArgumentSources.Length > 0)
                    ImGui.TextDisabled($"Argument provenance: {string.Join("; ", operation.ArgumentSources)}");
                ImGui.TextDisabled($"{CinematicWorldCommandResolver.DescribeSourceOperation(operation.Operation)} · path: {operation.CallbackPath}");
            }
            ImGui.TextDisabled("Arguments and provenance are source-level only, not evaluated runtime values; branch conditions and actual execution are not reconstructed.");
        }
        if (messageReaders.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled($"Literal message IDs read from this object's queue ({messageReaders.Length} source sites)");
            foreach (var readerGroup in messageReaders.GroupBy(reader => reader.MessageId))
            {
                string callbacks = string.Join("; ", readerGroup.Select(reader =>
                    $"{reader.FunctionName} · {reader.Source}:{reader.Line}").Distinct(StringComparer.Ordinal));
                ImGui.BulletText($"ID {readerGroup.Key} → {callbacks}");
                foreach (var reader in readerGroup.DistinctBy(reader => reader.FunctionName))
                    ImGui.TextDisabled($"Callback path: {reader.CallbackPath}");
            }
            ImGui.TextDisabled("Source-confirmed MSGGetByID_C reads and MSGGet_C reads followed by a literal message-ID comparison identify possible queue consumers. Delivery, callback execution and field interpretation are not inferred.");
        }
        if (messageRoutes.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled($"Static source message routes with a matching queue reader ({messageRoutes.Length})");
            foreach (var route in messageRoutes.Take(32))
            {
                string label = $"ID {route.MessageId} → {route.Receiver.Name} ({route.RecipientKey:X8}) · "
                    + $"MSGSend_C {route.SendCall.Source}:{route.SendCall.Line}";
                if (ImGui.Selectable($"{label}##sourceMessageRoute{owner.WorldKey:X8}{owner.OwnerKey:X8}"
                        + $"{route.RecipientKey:X8}{route.MessageId}{route.SendCall.Line}"))
                    _selectedCinematicScriptOwnerId = route.Receiver.Id;
                foreach (var reader in route.Readers.Take(3))
                {
                    ImGui.TextDisabled($"Receiver read: {reader.FunctionName} · {reader.Source}:{reader.Line} · {reader.ReadApi}");
                    ImGui.TextDisabled($"Callback path: {reader.CallbackPath}");
                }
            }
            if (messageRoutes.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {messageRoutes.Length} source message routes.");
            ImGui.TextDisabled("A route requires a literal message ID, a uniquely resolved MSGSend_C destination and a reachable source reader for the same ID. It describes possible static routing only; runtime queue contents and callback execution are not known.");
        }
        var engineCallGroups = engineCallSummaries
            .Where(call => call.Api is not ("WORPause" or "WORUnPause" or "WORPlayAbsTime"))
            .ToArray();
        if (engineCallGroups.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled($"Direct Iris engine API calls ({engineCallGroups.Length} APIs / {engineCallGroups.Sum(group => group.CallSiteCount)} sites)");
            foreach (var group in engineCallGroups.Take(48))
            {
                string callName = group.Api == "ACTKitMerge"
                    ? group.Variant == "OVERWRITE" ? "ACTKitMergeOver" : "ACTKitMergeNoOver"
                    : $"AI_EvalFunc_{group.Api}_C{(string.IsNullOrEmpty(group.Variant) ? "" : $"_{group.Variant}")}";
                ImGui.BulletText($"{callName} · {group.CallSiteCount} call sites");
                string examples = string.Join("; ", group.Examples.Take(3)
                    .Select(call => FormatEngineCallExample(call, sourceReferences, owner, archiveReferences)));
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Muted);
                ImGui.TextWrapped(examples);
                ImGui.PopStyleColor();
            }
            if (engineCallGroups.Length > 48)
                ImGui.TextDisabled($"Showing 48 of {engineCallGroups.Length} distinct API variants.");
            ImGui.TextDisabled("This is direct source-call evidence from functions reachable through configured callback slots; arguments, branches, execution order and runtime effects are not evaluated here.");
            ImGui.TextDisabled("OVA field annotations match direct C member fields or a unique function-local member assignment. The serialized field value is shown; branch-based pointer overrides and execution are not evaluated.");
            ImGui.TextDisabled("MSGSend annotations show the latest source assignments to the passed buffer before the call; earlier fields may be retained, and conditional branches are not evaluated.");
        }
        if (sourceReferences.Length > 0)
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader($"Source object/function references ({sourceReferences.Length})"))
            {
                ImGui.TextDisabled("Literal keys passed to Iris object/function resolvers by reachable source functions. A reference does not prove that the function executes at runtime.");
                foreach (var reference in sourceReferences.Take(64))
                {
                    string target = ResolveSourceReferenceTarget(reference, owner, archiveReferences);
                    var referencedOwner = reference.Kind == "Object"
                        ? CinematicScriptOwnerResolver.Resolve(reference.Key, owner.WorldKey,
                            Document?.CinematicScriptOwners.Values.AsEnumerable()
                                ?? Enumerable.Empty<CinematicScriptOwner>())
                        : null;
                    string linkLabel = $"{reference.Kind} · {reference.Symbol} → {target} ({reference.Key:X8})";
                    if (referencedOwner == null)
                    {
                        ImGui.BulletText(linkLabel);
                    }
                    else if (ImGui.Selectable($"{linkLabel} · inspect script owner {referencedOwner.Name}##sourceRef{owner.WorldKey:X8}{owner.OwnerKey:X8}{reference.Key:X8}{reference.Line}"))
                    {
                        _selectedCinematicScriptOwnerId = referencedOwner.Id;
                    }
                    ImGui.TextDisabled($"{reference.FunctionName} · {reference.Source}:{reference.Line} · path: {reference.CallbackPath}");
                }
                if (sourceReferences.Length > 64)
                    ImGui.TextDisabled($"Showing 64 of {sourceReferences.Length} source references.");
            }
        }
        ImGui.TextDisabled("Branch predicates, callback timers, current AI track state, and actual execution are not evaluated.");
    }

    private static string ResolveSourceReferenceTarget(CinematicScriptReachableSourceReference reference,
        CinematicScriptOwner owner, CinematicScriptNamedReference[] archiveReferences)
    {
        if (reference.Kind == "Function")
            return CinematicScriptReader.FunctionNameFor(reference.Key) ?? "function name not indexed";

        var resolved = CinematicScriptReferenceNameResolver.Resolve(reference.Key, owner.WorldKey,
            archiveReferences);
        return resolved?.Name ?? "object is not uniquely named in loaded worlds";
    }

    private static string FormatEngineCallExample(CinematicScriptReachableEngineCall call,
        CinematicScriptReachableSourceReference[] sourceReferences, CinematicScriptOwner owner,
        CinematicScriptNamedReference[] archiveReferences)
    {
        string example = $"{call.FunctionName}({string.Join(", ", call.Arguments)}) ({call.Source}:{call.Line})";
        string? worldOperation = CinematicWorldCommandResolver.IsWorldManagementOperation(call.Api)
            ? CinematicWorldCommandResolver.DescribeSourceOperation(call.Api) : null;
        var referencedObjects = CinematicScriptArgumentBindingResolver.ResolveSourceObjectReferences(call,
            sourceReferences);
        var ownerFields = CinematicScriptArgumentBindingResolver.ResolveOwnerFields(call, owner);
        if (worldOperation == null && referencedObjects.Length == 0 && ownerFields.Length == 0
            && call.ArgumentAssignments.Length == 0 && call.MessageFields.Length == 0
            && call.BranchConditions.Length == 0)
            return example;

        string[] objectBindings = referencedObjects.Take(4).Select(reference =>
            $"{reference.Symbol} → {ResolveSourceReferenceTarget(reference, owner, archiveReferences)} ({reference.Key:X8})")
            .ToArray();
        string[] ovaBindings = ownerFields.Take(6).Select(field =>
        {
            var directAlias = call.ArgumentFields.FirstOrDefault(argumentField =>
                argumentField.Name.Equals(field.VariableName, StringComparison.OrdinalIgnoreCase)
                && argumentField.AliasVariable != null);
            var assignmentAlias = call.ArgumentAssignments.FirstOrDefault(assignment =>
                assignment.Expression.Contains($"->{field.VariableName}", StringComparison.OrdinalIgnoreCase));
            string alias = directAlias != null
                ? $" via {directAlias.AliasVariable} assigned at line {directAlias.AssignmentLine}"
                : assignmentAlias != null
                    ? $" via {assignmentAlias.Variable}={assignmentAlias.Expression} at line {assignmentAlias.Line}"
                    : "";
            return $"{field.VariableName}={field.TargetName ?? field.Value} [{field.Source}{alias}]";
        }).ToArray();
        var annotations = new List<string>();
        if (worldOperation != null)
            annotations.Add($"world operation: {worldOperation}");
        if (objectBindings.Length > 0)
            annotations.Add($"source object variable: {string.Join(", ", objectBindings)}");
        if (ovaBindings.Length > 0)
            annotations.Add($"OVA fields: {string.Join(", ", ovaBindings)}");
        if (call.ArgumentAssignments.Length > 0)
        {
            string assignments = string.Join(", ", call.ArgumentAssignments.Take(6)
                .Select(assignment => $"{new string(' ', assignment.Depth * 2)}{assignment.Variable}={assignment.Expression} (line {assignment.Line})"));
            annotations.Add($"local argument assignment chain: {assignments}");
        }
        if (call.MessageFields.Length > 0)
        {
            string message = string.Join(", ", call.MessageFields.Take(8)
                .Select(field => $"{field.Name}={field.Expression} (line {field.Line})"));
            annotations.Add($"message fields: {message}");
        }
        if (call.BranchConditions.Length > 0)
        {
            string conditions = string.Join("; ", call.BranchConditions.Take(4)
                .Select(condition => condition.Kind == "else"
                    ? $"else of if ({condition.Expression}) (line {condition.Line})"
                    : $"if ({condition.Expression}) (line {condition.Line})"));
            annotations.Add($"enclosing source if conditions: {conditions}");
        }
        return $"{example} · {string.Join(" · ", annotations)}";
    }
}
