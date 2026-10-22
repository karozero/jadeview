namespace JadeView;

internal static class CinematicScriptActionRequestResolver
{
    internal static CinematicScriptActionRequestAnalysis Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> sourceCalls,
        IReadOnlyDictionary<int, CinematicAction> actions)
    {
        var resolved = new List<CinematicScriptActionRequest>();
        var unresolved = new List<CinematicScriptUnresolvedActionRequest>();
        foreach (var call in sourceCalls.Where(call => call.Api is
                     "ACTActionSet" or "ACTActionFrameSet" or "ACTActionFrequencySet"))
        {
            int ownerArgumentOffset = call.Variant == "CURRENT" ? 0 : 1;
            int actionArgumentIndex = ownerArgumentOffset;
            if (call.Arguments.Length <= actionArgumentIndex)
            {
                unresolved.Add(new(call, "The source call does not contain an action index."));
                continue;
            }
            if (!CinematicScriptTrackIndexResolver.TryResolveSignedIntegerArgument(owner, call,
                    actionArgumentIndex, out int requestedAction, out string binding))
            {
                string reason = call.LoopContexts is { Length: > 0 }
                    ? "The action index is inside a loop and is not expanded by this action-link view."
                    : "The action index is not a literal or uniquely assigned scalar OVA expression.";
                unresolved.Add(new(call, reason));
                continue;
            }
            int actionIndex = requestedAction < 0 ? requestedAction & 0x7FFFFFFF : requestedAction;
            if (!actions.TryGetValue(actionIndex, out var action))
            {
                unresolved.Add(new(call, $"Action slot {actionIndex} is absent from the owner's decoded action table."));
                continue;
            }

            int? controlValue = null;
            string? controlBinding = null;
            if (call.Api is "ACTActionFrameSet" or "ACTActionFrequencySet")
            {
                int controlArgumentIndex = ownerArgumentOffset + 1;
                if (call.Arguments.Length <= controlArgumentIndex
                    || !CinematicScriptTrackIndexResolver.TryResolveSignedIntegerArgument(owner, call,
                        controlArgumentIndex, out int resolvedControlValue, out string resolvedControlBinding))
                {
                    unresolved.Add(new(call,
                        $"The {ControlName(call.Api)} is not a literal or uniquely assigned scalar OVA expression."));
                    continue;
                }
                controlValue = resolvedControlValue;
                controlBinding = resolvedControlBinding;
            }

            resolved.Add(new(call, actionIndex, action, binding, controlValue, controlBinding));
        }
        return new(resolved.ToArray(), unresolved.ToArray());
    }

    private static string ControlName(string api) => api switch
    {
        "ACTActionFrameSet" => "action frame",
        "ACTActionFrequencySet" => "action frequency",
        _ => "action control value"
    };
}
