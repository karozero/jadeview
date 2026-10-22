namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string BoundingVolumeActivateInCallback = "BV_Active_Desactive_BV_In";
    private const string BoundingVolumeActivateOutCallback = "BV_Active_Desactive_BV_Out";
    private const string BoundingVolumeCustomBitInCallback = "BV_Active_Desactive_BV_CBits_In";
    private const string BoundingVolumeCustomBitOutCallback = "BV_Active_Desactive_BV_CBits_Out";
    private const string BoundingVolumeActivationInitCallback = "BV_Active_Desactive_BV_AD";

    private static bool HasBoundingVolumeActivationCallbacks(CinematicScriptOwner owner) =>
        owner.CallbackNames.Any(callback => callback.StartsWith("BV_Active_Desactive_", StringComparison.Ordinal));

    private static void AddBoundingVolumeActivationBehavior(List<string> diagnostics,
        CinematicScriptOwner owner)
    {
        bool hasActorEntry = HasCallback(owner, BoundingVolumeActivateInCallback);
        bool hasActorExit = HasCallback(owner, BoundingVolumeActivateOutCallback);
        bool hasCustomBitEntry = HasCallback(owner, BoundingVolumeCustomBitInCallback);
        bool hasCustomBitExit = HasCallback(owner, BoundingVolumeCustomBitOutCallback);
        bool hasInit = HasCallback(owner, BoundingVolumeActivationInitCallback);
        int? detectionType = EffectiveInteger(owner, "i_detection_type");
        int? pivotMode = EffectiveInteger(owner, "i_bv_pivot");
        int? customBitMask = EffectiveInteger(owner, "acteur_avec_CBit");
        bool usesActorRoute = detectionType == 0;
        bool usesCustomBitRoute = detectionType is > 0;

        if (hasInit)
        {
            diagnostics.Add(detectionType switch
            {
                > 0 => "BV_Active_Desactive_BV_AD selects the custom-bit overlap callback route; "
                    + $"it searches volume {DescribeVolume(owner)} using numeric "
                    + (customBitMask is { } mask
                        ? $"flags2 mask 0x{unchecked((uint)mask):X8}."
                        : "acteur_avec_CBit mask, which is unavailable."),
                0 => "BV_Active_Desactive_BV_AD selects the configured-actor collision callback route; "
                    + $"the detection actor is {DescribeObjectReference(owner, "acteur_a_detecter")} and the volume is "
                    + $"{DescribeVolume(owner)}.",
                _ => "BV_Active_Desactive_BV_AD selects its callback route from i_detection_type, but that OVA value "
                    + "is unavailable."
            });
        }

        if (usesActorRoute || detectionType is null && (hasActorEntry || hasActorExit))
        {
            string testDescription = pivotMode switch
            {
                > 0 => "COLBVPivotCollide tests the configured actor against the volume pivot",
                0 => "COLBVObjectObject tests the configured actor against the volume object",
                _ => "the source chooses COLBVPivotCollide or COLBVObjectObject from i_bv_pivot, whose value is unavailable"
            };
            diagnostics.Add($"The actor route uses {testDescription}: actor {DescribeObjectReference(owner, "acteur_a_detecter")}, "
                + $"volume {DescribeVolume(owner)}. Its In/Out callbacks apply opposite "
                + "activation-state values through complementary source branches.");
        }
        else if (hasInit && usesCustomBitRoute && (hasActorEntry || hasActorExit))
        {
            diagnostics.Add("The shared model also contains configured-actor In/Out callbacks, but the effective "
                + "i_detection_type selects the custom-bit route for this owner.");
        }

        if (usesCustomBitRoute || detectionType is null && (hasCustomBitEntry || hasCustomBitExit))
        {
            int? requiredFlags = EffectiveInteger(owner, "acteur_avec_CBit");
            string requiredFlagsDescription = requiredFlags switch
            {
                > 0 => $"requires at least one matching flags2 bit from mask 0x{unchecked((uint)requiredFlags.Value):X8}",
                0 => "uses a zero required mask, which cannot pass the source flags2 test",
                < 0 => $"uses signed mask 0x{unchecked((uint)requiredFlags.Value):X8}; the source tests for any matching flags2 bit",
                _ => "requires an acteur_avec_CBit mask, but its OVA value is unavailable"
            };
            diagnostics.Add("The custom-bit route calls COLBVGetFirstOverlap_C on "
                + $"{DescribeVolume(owner)}; its second argument is the numeric "
                + $"acteur_avec_CBit required-flags mask ({requiredFlagsDescription}), not an object reference. "
                + "The source passes flagMustNotHave=0 and whatToTest=5; Iris tests flags2 and accepts any "
                + "intersection with the required mask. Its In/Out callbacks use complementary overlap branches.");
        }
        else if (hasInit && usesActorRoute && (hasCustomBitEntry || hasCustomBitExit))
        {
            diagnostics.Add("The shared model also contains custom-bit callbacks, but the effective i_detection_type "
                + "selects the configured-actor route for this owner.");
        }

        if ((usesActorRoute || usesCustomBitRoute || detectionType is null)
            && (hasActorEntry || hasActorExit || hasCustomBitEntry || hasCustomBitExit))
        {
            int? activeInside = EffectiveInteger(owner, "active_dans_bv");
            diagnostics.Add(activeInside switch
            {
                > 0 => "active_dans_bv is nonzero: the In callback writes the target's inactive flag as true, "
                    + "and the Out callback writes it as false.",
                0 => "active_dans_bv is zero: the In callback writes the target's inactive flag as false, "
                    + "and the Out callback writes it as true.",
                _ => "active_dans_bv is unavailable, so the In/Out inactive-flag polarity cannot be derived."
            });

            string[] configuredTargets = Enumerable.Range(1, 3)
                .Where(index => EffectiveUnsigned(owner, $"object_a_activer{index}") is > 0 and < uint.MaxValue)
                .Select(index => $"object_a_activer{index}={DescribeObjectReference(owner, $"object_a_activer{index}")}")
                .ToArray();
            diagnostics.Add(configuredTargets.Length == 0
                ? "No non-null object_a_activer1..3 targets were decoded from the effective OVA values."
                : $"Configured activation targets: {string.Join("; ", configuredTargets)}.");

            int? suppressActivation = EffectiveInteger(owner, "ne_fait_pas_l_activation");
            diagnostics.Add(suppressActivation switch
            {
                > 0 => "ne_fait_pas_l_activation is enabled: the callback skips target inactive-flag writes; "
                    + "configured AI reinitialization and optional visibility writes remain separate source operations.",
                0 => "ne_fait_pas_l_activation is disabled: configured targets receive the source inactive-flag writes.",
                _ => "ne_fait_pas_l_activation is unavailable; whether target inactive-flag writes are suppressed is unknown."
            });

            for (int index = 1; index <= 3; index++)
            {
                if (EffectiveInteger(owner, $"ia_reinit{index}") is > 0
                    && EffectiveUnsigned(owner, $"object_a_activer{index}") is > 0 and < uint.MaxValue)
                    diagnostics.Add($"ia_reinit{index} is enabled: this callback reinitializes the AI variables of "
                        + $"{DescribeObjectReference(owner, $"object_a_activer{index}")} on the activation edge.");
            }

            int? mirrorVisibility = EffectiveInteger(owner, "rend_invisible_aussi");
            if (mirrorVisibility is > 0)
                diagnostics.Add("rend_invisible_aussi is enabled: the callback also writes the invisible flag for "
                    + "configured targets; group targets with identity bit 0x800000 receive the matching group operation.");
            else if (mirrorVisibility is null)
                diagnostics.Add("rend_invisible_aussi is unavailable; additional visibility writes cannot be determined.");

            int? onlyActivate = EffectiveInteger(owner, "ne_fait_qu_activer");
            diagnostics.Add(onlyActivate switch
            {
                > 0 => "ne_fait_qu_activer is enabled: the source does not switch to the paired In/Out callback, "
                    + "so this instance performs a one-way activation path.",
                0 => "ne_fait_qu_activer is disabled: the callback may switch to its paired In/Out callback after "
                    + "applying the state change.",
                _ => "ne_fait_qu_activer is unavailable; paired-callback switching cannot be determined."
            });

            if (EffectiveInteger(owner, "se_desactive_apres") is > 0)
                diagnostics.Add("se_desactive_apres is enabled: after applying the target state, the callback clears "
                    + "its active latch and sets its own inactive flag.");
        }

        diagnostics.Add("These findings describe source callback branches and serialized OVA values only. JadeView "
            + "does not run the collision tests, schedule the callback, or evaluate current target/group state.");

        static bool HasCallback(CinematicScriptOwner scriptOwner, string callback) =>
            scriptOwner.CallbackNames.Contains(callback, StringComparer.Ordinal);

        static string DescribeVolume(CinematicScriptOwner scriptOwner) =>
            EffectiveUnsigned(scriptOwner, "o_BV_a_qui") switch
            {
                null => "volume reference unavailable",
                0 => HasCallback(scriptOwner, BoundingVolumeActivationInitCallback)
                    ? "the current owner (BV_AD assigns the current GAO when o_BV_a_qui is null)"
                    : "null volume reference; whether initialization supplies a default is unknown",
                uint.MaxValue => "invalid volume reference",
                _ => DescribeObjectReference(scriptOwner, "o_BV_a_qui")
            };
    }
}
