using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private static void AddSectorActivatorBehavior(List<string> diagnostics, CinematicScriptOwner owner,
        IEnumerable<CinematicScriptOwner>? loadedOwners)
    {
        bool hasInit = owner.CallbackNames.Contains(SectorActivatorInitCallback, StringComparer.Ordinal);
        bool hasLoop = owner.CallbackNames.Contains(SectorActivatorLoopCallback, StringComparer.Ordinal);
        bool hasAfterCallback = owner.CallbackNames.Contains(SectorActivatorAfterCallback, StringComparer.Ordinal);
        bool hasCutter = owner.CallbackNames.Contains(SectorActivatorCutterCallback, StringComparer.Ordinal);
        var detectionValue = EffectiveUnsigned(owner, "des_o_detect");
        var groupValue = EffectiveUnsigned(owner, "des_o_cacher_groupe_de");
        var ownersByKey = loadedOwners?.Where(candidate => candidate.WorldKey == owner.WorldKey)
            .GroupBy(candidate => candidate.OwnerKey)
            .ToDictionary(group => group.Key, group => group.First());

        if (hasInit)
        {
            int? register = EffectiveInteger(owner, "des_i_register");
            diagnostics.Add(register is > 0
                ? "Activator_act_init registers this owner as a render sector and stores the returned sector ID in myID."
                : register == 0
                    ? "Activator_act_init does not register this owner as a render sector because des_i_register is zero."
                    : "Activator_act_init has no decoded des_i_register value, so whether it registers a render sector is unknown.");
            diagnostics.Add(groupValue is > 0
                ? $"Activator_act_init marks the referenced group ({DescribeObjectReference(owner, "des_o_cacher_groupe_de")}) as sector-managed; the engine marks its group members with the sector-participation bit."
                : groupValue == 0
                    ? "Activator_act_init has a null des_o_cacher_groupe_de; there is no configured group to mark as sector-managed."
                    : "The effective des_o_cacher_groupe_de reference is unavailable, so group setup cannot be derived.");
        }

        if (hasLoop || hasAfterCallback)
        {
            diagnostics.Add($"Activator_act uses a BV-pivot collision test against {DescribeObjectReference(owner, "des_o_detect")}. "
                + (hasAfterCallback
                    ? "The afterrec callback also tests the current viewport object when the configured detection test does not collide."
                    : "The loop callback uses this test to publish or clear the global current-activator reference."));
            if (detectionValue is null)
                diagnostics.Add("des_o_detect is not decoded; the actual collision target cannot be identified from this snapshot.");
        }

        if (hasAfterCallback)
        {
            int? autoHide = EffectiveInteger(owner, "des_i_auto_hide");
            int? forceShow = EffectiveInteger(owner, "i_forceshow");
            diagnostics.Add(autoHide switch
            {
                > 0 => "On collision, afterrec resets the activity counter to 1 for auto-hide; the following GRPSectInvisibleSet call changes the engine visibility counter when its state edge is processed.",
                0 => "On collision, afterrec resets the activity counter to 0; the following state-edge update decrements the engine visibility counter, while a nonzero counter increments it.",
                _ => "des_i_auto_hide is unavailable; the collision branch's activity-counter value cannot be fully derived."
            });
            diagnostics.Add("The callback also forces the activity counter to zero when the global no-sector flag is set, when i_forceshow is consumed, or when capability test 1 succeeds. It then resets i_cptact after processing; live global flags, capability state and collision are not evaluated.");
            if (forceShow is > 0)
                diagnostics.Add("i_forceshow is nonzero in the effective snapshot and is consumed by the callback on its next execution.");
            if (groupValue is > 0)
                diagnostics.Add("GRPSectInvisibleSet updates a counter on the configured group and registered sector members; nonzero increments the counter, zero decrements it, and -1 clears it. The renderer suppresses objects while this counter is nonzero.");
            else if (groupValue == 0)
                diagnostics.Add("Without a configured group, the callback applies GRPSectInvisibleSet to its own object and registered sector members.");
        }

        if (hasLoop)
        {
            AddSectorListBehavior(diagnostics, owner, ownersByKey, "des_ao_virer_les_secteurs",
                "increments the referenced Activator_act owner's i_cptact at AI-instance DWORD slot +130");
            AddSectorListBehavior(diagnostics, owner, ownersByKey, "des_ao_montrer_les_secteurs",
                "sets the referenced Activator_act owner's i_forceshow to 1 at AI-instance DWORD slot +131");
            diagnostics.Add("These list operations are issued only while this activator's BV-pivot test collides. Their eventual visibility effect depends on each referenced activator's own afterrec callback and live sector counter; JadeView does not simulate that runtime scheduling.");
        }

        if (hasCutter)
        {
            float? cutDistance = EffectiveFloat(owner, "des_f_distcut");
            int doorReferenceCount = owner.ObjectReferences.Count(reference =>
                reference.VariableName.Equals("des_ao_portes", StringComparison.Ordinal)
                && reference.TargetKey != 0);
            diagnostics.Add(cutDistance is { } distance && distance != 0
                ? $"The cutter callback uses viewer distance threshold {distance.ToString("0.###", CultureInfo.InvariantCulture)} and configured door references ({doorReferenceCount} decoded); it also consults door control flag 0x200000 and view culling."
                : cutDistance == 0
                    ? $"The cutter's distance-threshold branch is disabled (des_f_distcut=0); it still checks configured door references ({doorReferenceCount}), control flag 0x200000 and view culling."
                    : $"The cutter checks configured door references ({doorReferenceCount}), control flag 0x200000 and view culling; des_f_distcut is unavailable, so distance behavior is unknown.");
        }

        diagnostics.Add("Static Activator_act analysis describes source callbacks and serialized OVA links only. It does not establish owner execution, evaluate live collision/capability/global state, or schedule the referenced afterrec callbacks.");
    }

    private static void AddSectorListBehavior(List<string> diagnostics, CinematicScriptOwner owner,
        IReadOnlyDictionary<uint, CinematicScriptOwner>? ownersByKey, string variableName,
        string activatorOperation)
    {
        if (owner.EffectiveVariable(variableName) == null)
        {
            diagnostics.Add($"{variableName} is not decoded, so its sector-list entries are unknown.");
            return;
        }

        var references = owner.ObjectReferences.Where(reference =>
                reference.VariableName.Equals(variableName, StringComparison.OrdinalIgnoreCase)
                && reference.TargetKey != 0)
            .OrderBy(reference => reference.ArrayIndex ?? -1)
            .ToArray();
        var activatorTargets = references.Where(reference => ownersByKey?.TryGetValue(reference.TargetKey,
                out var target) == true
            && target.CallbackNames.Contains(SectorActivatorAfterCallback, StringComparer.Ordinal))
            .Select(reference => ownersByKey![reference.TargetKey])
            .DistinctBy(target => target.OwnerKey)
            .ToArray();
        string targetSummary = references.Length == 0
            ? "no non-null target references were decoded"
            : activatorTargets.Length == 0
                ? "no referenced target was resolved to an Activator_act owner, so raw targets are not assigned this field meaning"
                : $"{activatorTargets.Length} target(s): {string.Join(", ", activatorTargets.Take(8).Select(target => target.Name))}"
                    + (activatorTargets.Length > 8 ? ", …" : "");
        diagnostics.Add($"{variableName}: {references.Length} decoded non-null object reference(s); {targetSummary}. "
            + (activatorTargets.Length > 0 ? activatorOperation + "." : ""));
    }


}
