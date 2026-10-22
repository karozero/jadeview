using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private static void AddSuperSpecialFlagDetectorBehavior(List<string> diagnostics,
        CinematicScriptOwner owner)
    {
        int? mode = EffectiveInteger(owner, "Mode_ET");
        var groups = Enumerable.Range(1, 3)
            .Select(groupIndex => DescribeSuperFlagGroup(owner, groupIndex))
            .ToArray();
        string combination = mode switch
        {
            1 => "Mode_ET=1: each configured group is an AND of its nonzero flag slots; any one passing group is sufficient",
            0 => "Mode_ET=0: each configured group is an OR of its nonzero flag slots; every group must pass",
            null => "Mode_ET is unavailable; the group operators and outer combination cannot be selected",
            _ => $"Mode_ET={mode}: Iris treats nonzero values as ET mode, but this value is not the usual 0/1 encoding"
        };

        diagnostics.Add($"Detect_o_SuperSpecialFlags checks three saved-flag groups. {combination}. "
            + string.Join("; ", groups) + ".");

        float? interval = EffectiveFloat(owner, "f_every");
        diagnostics.Add(interval switch
        {
            -1 => "f_every=-1 selects the optimized dialogue path: init registers this owner in GST_Global.ao_optim_dialogue, "
                + "selects the ET/OU dialogue callback, and sets owner control flags 0x600000.",
            { } seconds when seconds >= 0 => $"f_every={seconds.ToString("R", CultureInfo.InvariantCulture)} selects the polling path; "
                + "init runs the selected ET/OU test callback, then the callback checks on its configured timer.",
            null => "f_every is unavailable; the detector scheduling mode cannot be derived.",
            { } value => $"f_every={value.ToString("R", CultureInfo.InvariantCulture)} is outside the documented dialogue/polling values; "
                + "the scheduling mode is not inferred."
        });

        int? successFlag = EffectiveInteger(owner, "SF_result_a_1");
        int? failureFlag = EffectiveInteger(owner, "SF_result_a_0");
        var resultEffects = new List<string>();
        if (successFlag is > 0) resultEffects.Add($"sets saved flag {successFlag} on success");
        if (failureFlag is > 0) resultEffects.Add($"clears saved flag {failureFlag} on success");
        diagnostics.Add("When a predicate passes, the owner sets capability 0x100"
            + (resultEffects.Count > 0 ? $" and {string.Join("; ", resultEffects)}" : "") + ".");

        if (interval is { } pollingInterval && pollingInterval != -1)
            diagnostics.Add("Polling test callbacks reverse the configured result flags and clear capability 0x100 when predicates fail. "
                + "Dialogue callbacks only apply the success branch; failure does not perform that polling reset.");
        else if (interval == -1)
            diagnostics.Add("Dialogue callbacks apply only the success branch; they do not clear result flags or capability 0x100 on a failed predicate.");
        else
            diagnostics.Add("f_every is unavailable; the test/dialogue failure effects cannot be selected from this OVA snapshot.");

        diagnostics.Add("This is a source/OVA summary, not a live evaluation: current save flags, dialogue registration order, and callback scheduling are unavailable.");
    }

    private static string DescribeSuperFlagGroup(CinematicScriptOwner owner, int groupIndex)
    {
        string flagArrayName = $"SF_Et_{groupIndex}";
        string stateArrayName = $"Et_{groupIndex}_etatSF";
        var flags = owner.EffectiveIntegerArray(flagArrayName);
        var states = owner.EffectiveIntegerArray(stateArrayName);
        int? mode = EffectiveInteger(owner, "Mode_ET");
        if (owner.EffectiveVariable(flagArrayName) == null)
            return $"group {groupIndex} unavailable ({flagArrayName} missing)";

        int? firstFlag = flags.FirstOrDefault(value => value.Index == 0)?.Value;
        if (firstFlag is null)
            return $"group {groupIndex} unavailable (slot 0 of {flagArrayName} missing; source activation gate cannot be resolved)";
        if (firstFlag == 0)
        {
            return mode switch
            {
                0 => $"group {groupIndex} skipped because slot 0 is zero (OU mode treats the group as passing)",
                { } configuredMode when configuredMode != 0 => $"group {groupIndex} skipped because slot 0 is zero (ET mode treats the group as failing)",
                _ => $"group {groupIndex} has slot 0 zero; its empty-group result depends on the unavailable Mode_ET branch"
            };
        }

        var predicates = new List<string>();
        for (int slot = 0; slot < 5; slot++)
        {
            int? flag = flags.FirstOrDefault(value => value.Index == slot)?.Value;
            if (flag is null) continue;
            if (flag == 0) continue;

            int? requiredState = states.FirstOrDefault(value => value.Index == slot) is { } stateEntry
                ? stateEntry.Value : null;
            if (requiredState is null)
            {
                predicates.Add($"slot {slot} flag {flag} (required state unavailable)");
                continue;
            }

            string requirement = requiredState == 0 ? "clear" : "set";
            predicates.Add($"flag {flag} {requirement}");
        }

        if (predicates.Count == 0)
            return $"group {groupIndex} has no nonzero flag predicates"
                + (owner.EffectiveVariable(stateArrayName) == null ? $" ({stateArrayName} missing)" : "");

        string operation = mode switch
        {
            { } configuredMode when configuredMode != 0 => "all",
            0 => "any",
            _ => "unknown operator"
        };
        return $"group {groupIndex} {operation}: {string.Join(", ", predicates)}";
    }
}
