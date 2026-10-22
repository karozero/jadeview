namespace JadeView;

internal static class CinematicEventActionLinkResolver
{
    internal static CinematicEventActionLinkAnalysis Analyze(CinematicScriptEventTrack track,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> actionKits)
        => AnalyzeCalls(track.Calls, track.WorldKey, actionKits);

    internal static CinematicEventActionLinkAnalysis AnalyzeCalls(
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls,
        uint preferredWorldKey,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> actionKits)
    {
        var resolved = new List<CinematicEventActionLink>();
        var unresolved = new List<CinematicEventUnresolvedActionLink>();
        var effectiveKits = new Dictionary<uint, Dictionary<int, SourcedAction>>();
        foreach (var (time, call) in calls.OrderBy(item => item.Time))
        {
            string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
            if (IsKitMergeCommand(function))
            {
                ApplyKitMerge(time, call, function);
                continue;
            }
            if (!IsActionCommand(function)) continue;

            Dictionary<int, SourcedAction>? targetKit = GetEffectiveKit(call.Target, out string failure);
            if (targetKit == null)
            {
                unresolved.Add(new(time, call, function, failure));
                continue;
            }
            if (call.Arguments.Length == 0)
            {
                unresolved.Add(new(time, call, function, "The serialized command has no action index."));
                continue;
            }

            int requestedAction = unchecked((int)call.Arguments[0]);
            int actionIndex = requestedAction < 0 ? requestedAction & 0x7FFFFFFF : requestedAction;
            if (!targetKit.TryGetValue(actionIndex, out var sourcedAction))
            {
                unresolved.Add(new(time, call, function,
                    $"Action slot {actionIndex} is absent from the target's effective action table at {time:0.###}s."));
                continue;
            }

            int? controlValue = function is "ACTActionFrameSet" or "ACTActionFrequencySet"
                ? call.Arguments.Length > 1 ? unchecked((int)call.Arguments[1]) : null
                : null;
            if ((function is "ACTActionFrameSet" or "ACTActionFrequencySet") && controlValue == null)
            {
                unresolved.Add(new(time, call, function,
                    $"The serialized command is missing its {ControlName(function)} value."));
                continue;
            }

            resolved.Add(new(time, call, function, actionIndex, sourcedAction.Action, controlValue,
                sourcedAction.SourceWorldKey));
        }
        return new(resolved.ToArray(), unresolved.ToArray());

        Dictionary<int, SourcedAction>? GetEffectiveKit(uint ownerKey, out string failure)
        {
            if (effectiveKits.TryGetValue(ownerKey, out var effectiveKit))
            {
                failure = "";
                return effectiveKit;
            }

            var resolution = CinematicActionKitScopeResolver.Resolve(ownerKey, preferredWorldKey, actionKits);
            if (!resolution.IsResolved)
            {
                failure = resolution.Failure;
                return null;
            }

            effectiveKit = resolution.Actions!.ToDictionary(pair => pair.Key,
                pair => new SourcedAction(pair.Value, resolution.SourceWorldKey!.Value));
            effectiveKits.Add(ownerKey, effectiveKit);
            failure = "";
            return effectiveKit;
        }

        void ApplyKitMerge(float time, CinematicLiteralCall call, string functionName)
        {
            if (call.Arguments.Length < 3)
            {
                unresolved.Add(new(time, call, functionName,
                    "The serialized action-kit merge is missing its source GAO or action range."));
                return;
            }

            Dictionary<int, SourcedAction>? targetKit = GetEffectiveKit(call.Target, out string targetFailure);
            if (targetKit == null)
            {
                unresolved.Add(new(time, call, functionName, $"Destination action table: {targetFailure}"));
                return;
            }

            uint sourceKey = call.Arguments[0];
            Dictionary<int, SourcedAction>? sourceKit = GetEffectiveKit(sourceKey, out string sourceFailure);
            if (sourceKit == null)
            {
                unresolved.Add(new(time, call, functionName, $"Source action table {sourceKey:X8}: {sourceFailure}"));
                return;
            }

            int firstAction = unchecked((int)call.Arguments[1]);
            int lastAction = unchecked((int)call.Arguments[2]);
            if (firstAction > lastAction) return;

            bool overwrite = functionName == "ACTKitMergeOver";
            var sourceActions = sourceKit
                .Where(pair => pair.Key >= firstAction && pair.Key <= lastAction).ToArray();
            if (overwrite)
            {
                foreach (int slot in targetKit.Keys
                             .Where(slot => slot >= firstAction && slot <= lastAction).ToArray())
                    targetKit.Remove(slot);
            }

            foreach (var (slot, action) in sourceActions)
            {
                if (overwrite || !targetKit.ContainsKey(slot))
                    targetKit[slot] = action;
            }
        }
    }

    internal static string? Describe(CinematicLiteralCall call, uint preferredWorldKey,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> actionKits)
    {
        var analysis = AnalyzeCalls([(0, call)], preferredWorldKey, actionKits);
        if (analysis.Resolved.FirstOrDefault() is { } link)
        {
            string control = link.Function switch
            {
                "ACTActionSet" => "action clock reset",
                "ACTActionFrameSet" => $"frame {link.ControlValue} (source stores int16)",
                "ACTActionFrequencySet" => $"frequency input {link.ControlValue} (engine scale 60/64)",
                _ => ""
            };
            string clips = link.Action.Items.Length == 0
                ? "no decoded animation items"
                : string.Join(", ", link.Action.Items.Select(item =>
                    $"{item.Clip.Key:X8} (shape {item.ShapeKey:X8})"));
            string sourceWorld = link.ActionTableWorldKey == preferredWorldKey
                ? "target-world action table" : $"action table from world {link.ActionTableWorldKey:X8}";
            return $"Action slot {link.ActionIndex} · {control} · {sourceWorld} · clip/shape: {clips}";
        }
        return analysis.Unresolved.FirstOrDefault() is { } unresolved
            ? $"Action link unresolved: {unresolved.Reason}" : null;
    }

    private static bool IsActionCommand(string function) => function is
        "ACTActionSet" or "ACTActionFrameSet" or "ACTActionFrequencySet";

    private static bool IsKitMergeCommand(string function) => function is
        "ACTKitMerge" or "ACTKitMergeOver";

    private static string ControlName(string function) => function == "ACTActionFrameSet"
        ? "frame" : "frequency";
}

internal sealed record SourcedAction(CinematicAction Action, uint SourceWorldKey);

internal sealed record CinematicEventActionLink(float Time, CinematicLiteralCall Call, string Function,
    int ActionIndex, CinematicAction Action, int? ControlValue, uint ActionTableWorldKey);

internal sealed record CinematicEventUnresolvedActionLink(float Time, CinematicLiteralCall Call,
    string Function, string Reason);

internal sealed record CinematicEventActionLinkAnalysis(CinematicEventActionLink[] Resolved,
    CinematicEventUnresolvedActionLink[] Unresolved);
