using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string CinematicStartCallback = "Cine_Events_cine_tests_depart";
    private const string CinematicEndCallback = "Cine_Events_Cine_exec_EndOfEvents";
    private const string CinematicGoToEventsCallback = "Cine_Events_Cine_exec_GoToEvents";
    private const string CinematicPlayTrackCallback = "Cine_Events_cine_play_eventtrack";
    private const string MpegPlayerInitCallback = "mpegplayer_init";
    private const string MdiskDroneInitCallback = "Drone_Mdisk_Drone_Mdisk_init";
    private const string DialogueReceptionCallback = "Cine_Textes_CiTxt_reception_messages";
    private const string DialogueInitCallback = "Cine_Textes_CiTxt_init";
    private const string ZoneDetectorInitCallback = "Detect_o_Zone_doz_init";
    private const string CapabilityDetectorInitCallback = "Detect_o_Capa_doc_init";
    private const string BoundingVolumeCapabilitySetCallback = "Detect_o_BV_CapaSet_detect_go";
    private const string SingleFlagDetectorCallback = "Detect_o_SpecialFlags_dosf_tests";
    private const string FourFlagDetectorInitCallback = "Detect_o_4SpecialFlags_D4sf_init";
    private const string SuperSpecialFlagDetectorInitCallback = "Detect_o_SuperSpecialFlags_ssf_init";
    private const string CompanionCinematicActionCallback = "companion_co_action_cine";
    private const string CompanionDialogueCallback = "companion_co_exec_dialogue";
    private const string AutomaticLipSyncInitCallback = "Cine_lipsynch_auto_Lips_init";
    private const string AutomaticLipSyncMessageCallback = "Cine_lipsynch_auto_Lips_lecture_messages";
    private const string AutomaticLipSyncMorphCallback = "Cine_lipsynch_auto_Lips_morphing";
    private const string SimpleHaloInitCallback = "SFX_HaloSimple_init";
    private const string SimpleHaloMoveCallback = "SFX_HaloSimple_move";
    private const string GeneralCameraFirstCallback = "gencam_mario_gcm_first";
    private const string GeneralCameraActivationCallback = "gencam_mario_gcm_exec_activation";
    private const string GeneralCameraNormalCallback = "gencam_mario_gcm_normal";
    private const string SectorActivatorInitCallback = "Activator_act_init";
    private const string SectorActivatorLoopCallback = "Activator_act_loop";
    private const string SectorActivatorAfterCallback = "Activator_act_afterrec";
    private const string SectorActivatorCutterCallback = "Activator_act_cutter";
    private static readonly (int Mask, string Variable, string Description)[] CinematicStartGates =
    [
        (0x001, "des_o_actor_zone1", "zone 1 collision / optional model filter"),
        (0x002, "des_o_actor_zone2", "zone 2 collision relation"),
        (0x004, "des_o_actor_status1", "actor activity state"),
        (0x008, "des_o_actor_capa1", "actor capability mask"),
        (0x010, "des_o_actor_cbit1", "actor custom-bit mask"),
        (0x020, "des_o_acteur_msg", "queued message ID and optional Int1 mask"),
        (0x040, "des_o_actor_visible1", "actor visibility and viewport bounds"),
        (0x080, "des_i_specialFlag_test1", "saved special flag test 1"),
        (0x100, "des_i_specialFlag_test2", "saved special flag test 2"),
        (0x200, "des_o_actor_desstruct", "destructible actor state")
    ];
    private static readonly (int Mask, string Variable, string Description)[] CinematicInterruptionGates =
    [
        (0x001, "des_o_actor_zone_exit1", "configured actor exit-zone collision test"),
        (0x004, "des_o_actor_status_exit1", "actor activity-state exit test"),
        (0x008, "des_o_actor_capa_exit1", "actor capability exit test"),
        (0x080, "des_i_SPFlag_test_exit1", "saved special-flag exit test")
    ];

    internal static string[] Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptOwner>? loadedOwners = null,
        IEnumerable<SceneObject>? worldObjects = null)
    {
        var diagnostics = new List<string>();
        if (HasCinematicFunctionReference(owner, CinematicStartCallback))
        {
            bool startCallbackIsAssigned = owner.CallbackNames.Contains(CinematicStartCallback,
                StringComparer.Ordinal);
            diagnostics.Add("Cine_Events lifecycle: firstinit enters init immediately unless f_first_delay is set; "
                + "init computes enabled tests and transfers to cine_tests_depart. The archive does not show "
                + "whether gameplay instantiated or enabled this owner.");
            if (!startCallbackIsAssigned && owner.ModelFunctionNames.Contains(CinematicStartCallback,
                    StringComparer.Ordinal))
                diagnostics.Add("Cine_Events lifecycle functions are referenced by this script model. The effective "
                    + "owner callback slots are separate data; source references and call flow describe available "
                    + "behavior, not proof that each function is assigned or executed.");
            diagnostics.Add("Iris runtime scheduling: AI_ReinitInstance copies script-model callback slots into "
                + "instance tracks, and AI_RunInstance dispatches those tracks from the world update. This is "
                + "the generic script execution path, not proof that this owner was active in a particular playthrough.");
            diagnostics.Add("Cine_Events flow: cine_tests_depart → Cine_exec_InitialiseCine → "
                + "Cine_exec_GoToEvents → cine_play_eventtrack → Cine_exec_EndOfEvents. "
                + "The start callback evaluates enabled gates, the play callback advances/stops tracks, "
                + "and the end callback restores tracks and completion state.");
            int? independent = EffectiveInteger(owner, "des_i_cine_independante");
            diagnostics.Add(independent is > 0
                ? "This owner is marked independent and bypasses the shared cinematic-priority wait."
                : "This owner uses shared cinematic priority: it waits while another cinematic is active "
                    + "or the board-panel busy bit 0x800 is set.");

            int derivedTestsMask = DeriveStartGateMask(owner);
            var enabledGates = CinematicStartGates.Where(gate => (derivedTestsMask & gate.Mask) != 0)
                .Select(gate => gate.Description).ToArray();
            diagnostics.Add(enabledGates.Length == 0
                ? "No optional cine_tests_depart gates are derived from the effective OVA fields. "
                    + "If this owner is running, its start path proceeds to cinematic-priority arbitration; "
                    + "the serialized data alone cannot prove runtime activation."
                : $"Start gates derived by cine_init (mask 0x{derivedTestsMask:X}): {string.Join("; ", enabledGates)}. "
                    + "All enabled tests must pass; current gameplay state is unavailable.");
            if (CinematicStartGates.Any(gate => !HasEffectiveValue(owner, gate.Variable)))
                diagnostics.Add("Some start-gate OVA fields are unavailable; the derived mask may be incomplete.");
            diagnostics.AddRange(AnalyzeCapabilityGateSnapshot(owner, derivedTestsMask, worldObjects));
            AddSpecialFlagTest(diagnostics, owner, derivedTestsMask, "des_i_specialFlag_test1", 0x80, 1);
            AddSpecialFlagTest(diagnostics, owner, derivedTestsMask, "des_i_specialFlag_test2", 0x100, 2);
            AddCinematicMessageGateBehavior(diagnostics, owner, derivedTestsMask);
            AddCinematicInterruptionBehavior(diagnostics, owner);
            AddInitializationFlagSuppression(diagnostics, owner);

            AddCinematicType(diagnostics, owner);

            if (HasCinematicFunctionReference(owner, CinematicEndCallback))
            {
                int? setFlag = EffectiveInteger(owner, "des_i_SPFlag_associe_vrai");
                int? clearFlag = EffectiveInteger(owner, "des_i_SPFlag_associe_faux");
                if (setFlag is > 0) diagnostics.Add($"Completion callback sets saved special flag {setFlag}.");
                if (clearFlag is > 0) diagnostics.Add($"Completion callback clears saved special flag {clearFlag}.");
            }
        }

        AddEventTrackPlaybackBehavior(diagnostics, owner);
        AddCinematicAudioBehavior(diagnostics, owner);
        if (owner.CallbackNames.Contains(WorldTransitionEventCallback, StringComparer.Ordinal))
            AddWorldTransitionEventBehavior(diagnostics);

        if (owner.CallbackNames.Contains(MpegPlayerInitCallback, StringComparer.Ordinal))
            AddMpegPlayerBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(MdiskDroneInitCallback, StringComparer.Ordinal))
            AddMdiskDroneBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(DialogueReceptionCallback, StringComparer.Ordinal))
            AddDialogueMessageBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(HelperTextInitCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(HelperTextDepartureCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(HelperTextDisplayCallback, StringComparer.Ordinal))
            AddHelperTextBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(ZoneDetectorInitCallback, StringComparer.Ordinal))
            AddZoneDetectorBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(CapabilityDetectorInitCallback, StringComparer.Ordinal))
            AddCapabilityDetectorBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(BoundingVolumeCapabilitySetCallback, StringComparer.Ordinal))
            AddBoundingVolumeCapabilitySetBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(SingleFlagDetectorCallback, StringComparer.Ordinal))
            AddSingleFlagDetectorBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(FourFlagDetectorInitCallback, StringComparer.Ordinal))
            AddFourFlagDetectorBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(SuperSpecialFlagDetectorInitCallback, StringComparer.Ordinal))
            AddSuperSpecialFlagDetectorBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(CompanionCinematicActionCallback, StringComparer.Ordinal))
            AddCompanionCinematicModeBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(AutomaticLipSyncInitCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(AutomaticLipSyncMessageCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(AutomaticLipSyncMorphCallback, StringComparer.Ordinal))
            AddAutomaticLipSyncBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(SimpleHaloInitCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(SimpleHaloMoveCallback, StringComparer.Ordinal))
            AddSimpleHaloBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(GeneralCameraFirstCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(GeneralCameraActivationCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(GeneralCameraNormalCallback, StringComparer.Ordinal))
            AddGeneralCameraBehavior(diagnostics, owner);

        if (owner.CallbackNames.Contains(SectorActivatorInitCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(SectorActivatorLoopCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(SectorActivatorAfterCallback, StringComparer.Ordinal)
            || owner.CallbackNames.Contains(SectorActivatorCutterCallback, StringComparer.Ordinal))
            AddSectorActivatorBehavior(diagnostics, owner, loadedOwners);

        if (HasBoundingVolumeActivationCallbacks(owner))
            AddBoundingVolumeActivationBehavior(diagnostics, owner);

        return diagnostics.ToArray();
    }

    private static void AddGeneralCameraBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool hasActivation = owner.CallbackNames.Contains(GeneralCameraActivationCallback, StringComparer.Ordinal);
        if (owner.CallbackNames.Contains(GeneralCameraFirstCallback, StringComparer.Ordinal))
            diagnostics.Add("gencam_mario_gcm_first stops the current AI track before the camera's other callbacks run.");

        if (hasActivation)
        {
            var requiredFlags = DescribeGeneralCameraFlags(owner, "des_ai_special_flags", inverted: false);
            var forbiddenFlags = DescribeGeneralCameraFlags(owner, "des_ai_special_flags_off", inverted: true);
            diagnostics.Add($"Camera readiness first checks saved-flag slots: {requiredFlags}; then applies the inverse-polarity off-list: {forbiddenFlags}. Nonzero entries above 10000 encode flag-index minus 10000 and invert the source predicate. These gates run before the later capability overrides.");

            diagnostics.Add($"If the saved-flag tests pass, the activation callback rejects readiness while the player camera reports manual control or the camera has capability 0x2. Camera capability 0x4 then forces readiness; otherwise cinematic-target mode rejects it, and the configured actor must pass a BV-pivot collision against this camera. Detected actor: {DescribeObjectReference(owner, "des_o_actor_detected")}.");
            uint? capabilityActorKey = EffectiveUnsigned(owner, "des_o_actor_CapaSwitch");
            diagnostics.Add(capabilityActorKey == 0
                ? "des_o_actor_CapaSwitch is explicitly zero, so the optional 0x100 actor-capability predicate is bypassed."
                : capabilityActorKey is { } configuredKey
                    ? $"The optional capability actor is {DescribeObjectReference(owner, "des_o_actor_CapaSwitch")} and must have capability 0x100 for this camera to become ready."
                    : "des_o_actor_CapaSwitch is unavailable; the optional 0x100 actor-capability predicate cannot be determined.");

            int? fightMode = EffectiveInteger(owner, "des_i_fight");
            if (fightMode is > 0)
                diagnostics.Add("Fight mode additionally requires a resolved current fight actor and compares this camera's AI priority with the shared camera priority. The fight actor is maintained through the game camera/Sally controller links; those live objects and priority values are not part of the serialized OVA snapshot.");
            else if (fightMode == 0)
                diagnostics.Add("des_i_fight=0: the optional fight-actor/priority gate is disabled.");
            else
                diagnostics.Add("des_i_fight is unavailable; the optional fight-actor/priority gate cannot be determined.");
        }

        if (owner.CallbackNames.Contains(GeneralCameraNormalCallback, StringComparer.Ordinal))
            diagnostics.Add("The normal callback contains the ongoing camera/target update and transition logic. JadeView does not execute that gameplay camera controller; the static summary explains readiness gates, not the final camera pose or whether a manager selected it.");
    }

    private static string DescribeGeneralCameraFlags(CinematicScriptOwner owner, string variableName,
        bool inverted)
    {
        var entries = owner.EffectiveIntegerArray(variableName);
        if (entries.Length == 0)
            return $"no decoded entries for {variableName} (empty, absent or unsupported array)";

        var predicates = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Value == 0) continue;
            if (entry.Value < 0)
            {
                if (!inverted)
                    predicates.Add($"{variableName}[{entry.Index}]={entry.Value} fails the on-list test");
                continue;
            }

            bool encodedClear = entry.Value >= 10000;
            int flag = encodedClear ? entry.Value - 10000 : entry.Value;
            if (flag <= 0)
            {
                predicates.Add($"{variableName}[{entry.Index}]={entry.Value} encodes flag zero");
                continue;
            }

            bool requiredSet = (encodedClear == false) != inverted;
            predicates.Add($"flag {flag} {(requiredSet ? "set" : "clear")} ({variableName}[{entry.Index}], {entry.Source})");
        }

        return predicates.Count == 0
            ? $"all {variableName} entries are zero"
            : string.Join("; ", predicates);
    }

    private static void AddAutomaticLipSyncBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        var actorReference = owner.EffectiveObjectReference("des_o_acteur");
        uint? actorKey = EffectiveUnsigned(owner, "des_o_acteur");
        int? actorType = EffectiveInteger(owner, "des_i_type_acteur");
        string actorDescription = actorKey switch
        {
            0 => "uses the script owner itself because des_o_acteur is null",
            1 or 2 => $"targets main-actor slot {actorKey}",
            null when actorReference?.TargetName is { Length: > 0 } actorName
                => $"targets '{actorName}' ({actorReference.TargetKey:X8})",
            null when actorReference != null
                => $"targets {DescribeObjectReference(actorReference)}",
            null => "uses the script owner itself when des_o_acteur is null; the effective actor reference is unavailable in this snapshot",
            _ when actorReference?.TargetName is { Length: > 0 } actorName
                => $"targets '{actorName}' ({actorReference.TargetKey:X8})",
            _ => $"targets object {(actorReference?.TargetKey ?? actorKey.Value):X8}, unresolved in the loaded object map"
        };

        diagnostics.Add($"Automatic lip-sync init {actorDescription}. "
            + (actorType is { } type
                ? DescribeAutomaticLipSyncType(type)
                : "des_i_type_acteur is unavailable, so the source-coded mouth sequence/channel choice cannot be derived."));

        if (owner.CallbackNames.Contains(AutomaticLipSyncMessageCallback, StringComparer.Ordinal))
            diagnostics.Add("The lip-sync message callback consumes queued type 53. A resolvable message Gao replaces the target actor; Int1 supplies progression speed and Vector2.Y selects the mouth form/fraction. It does not inspect dialogue text or audio playback state.");

        if (owner.CallbackNames.Contains(AutomaticLipSyncMorphCallback, StringComparer.Ordinal))
            diagnostics.Add("The morph callback updates the configured mouth channel over AI delta time: it eases the current factor toward the target and advances form progression using the configured speed. With automatic mouth mode enabled, reaching the end of the progression advances through the source-coded 32-entry form sequence; this is not phoneme decoding.");

        diagnostics.Add("These callbacks describe the shared model's available behavior. Their presence and serialized OVA values do not prove that the owner was instantiated, received a message, or ran during this cinematic.");
    }

    private static string DescribeAutomaticLipSyncType(int actorType)
    {
        int? mouthChannel = actorType switch
        {
            1 => 4,
            2 => 3,
            3 => 2,
            4 => 1,
            5 or 6 or 7 => 4,
            _ => null
        };
        int? shapeChannel = actorType switch
        {
            4 => 3,
            5 => 4,
            _ => null
        };
        string shapeDescription = shapeChannel is { } shape
            ? $" and selects head-shape channel {shape}"
            : " and leaves the head-shape channel at its existing/default value";
        return mouthChannel is { } channel
            ? $"des_i_type_acteur={actorType} selects source-coded mouth-channel slot {channel}{shapeDescription}."
            : $"des_i_type_acteur={actorType} uses the source default mouth sequence and leaves the mouth/head-shape channel at its existing/default value.";
    }

    private static void AddCompanionCinematicModeBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        diagnostics.Add("Companion cinematic-mode callback: when dispatched, companion_co_action_cine sets "
            + "co_current_mode=11 and i_interdit_action=15. Model membership alone does not establish that this action ran.");
        if (owner.CallbackNames.Contains(CompanionDialogueCallback, StringComparer.Ordinal))
            diagnostics.Add("The same model includes companion_co_exec_dialogue; Iris returns from that callback "
                + "without processing its dialogue queue while co_current_mode is 11. This gameplay dialogue path "
                + "is separate from cinematic type-30 receiver messages and authored facial tracks.");
    }

    private static void AddZoneDetectorBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool usesZdePivot = (owner.SerializedGaoFlags & 0x400) != 0;
        string collisionRoute = usesZdePivot
            ? "ZDE pivot collision at pivot index 2"
            : "bounding-volume overlap";
        string alternateCollisionRoute = usesZdePivot
            ? "bounding-volume overlap when the bit is clear"
            : "ZDE pivot collision at pivot index 2 when the bit is set";
        diagnostics.Add($"Detect_o_Zone trigger: init clears capability 0x100; serialized GAO flags "
            + $"0x{owner.SerializedGaoFlags:X8} have identity flag 0x400 {(usesZdePivot ? "set" : "clear")}, "
            + $"so init selects {collisionRoute} ({alternateCollisionRoute}) when the configured actor resolves. A runtime identity-flag "
            + "change before initialization would change this route.");
        diagnostics.Add("On the first collision frame it sets owner capability 0x100; when the collision ends it clears that capability.");

        var actorReference = owner.EffectiveObjectReference("des_acteur_detecte1");
        uint? actorKey = EffectiveUnsigned(owner, "des_acteur_detecte1");
        if (actorReference?.TargetName is { Length: > 0 } actorName)
            diagnostics.Add($"Trigger actor is linked to '{actorName}' ({actorReference.TargetKey:X8}).");
        else if (actorReference?.TargetKey is > 0 and <= 2)
            diagnostics.Add($"Trigger actor uses main-actor slot {actorReference.TargetKey}; "
                + "live actor resolution is not available in the archive preview.");
        else if (actorReference != null)
            diagnostics.Add($"Trigger actor reference is {actorReference.TargetKey:X8}; "
                + "the target is unresolved in the loaded object map.");
        else if (actorKey == 0)
            diagnostics.Add("The effective trigger actor reference is null; Iris's BV and ZDE collision predicates return false for a null actor.");
        else
            diagnostics.Add("The effective trigger actor reference is unavailable; the collision target cannot be resolved statically.");

        var flagEffects = new List<string>();
        AddFlagEffect(flagEffects, owner, "des_i_SPFlag_set01", "sets");
        AddFlagEffect(flagEffects, owner, "des_i_SPFlag_reset01", "clears");
        int? invertFlag = EffectiveInteger(owner, "des_i_SPFlag_invert01");
        float? timer = EffectiveFloat(owner, "des_f_minuterie");
        if (invertFlag is > 0)
            flagEffects.Add($"sets raw saved-flag index {invertFlag} on entry");
        if (flagEffects.Count > 0)
            diagnostics.Add($"On trigger entry, configured saved-flag effects: {string.Join("; ", flagEffects)}. "
                + "Current save-flag and inside/outside state are unavailable.");
        else
            diagnostics.Add("No positive saved-flag entry effects are configured by the effective OVA values.");

        var timerEffects = new List<string>();
        int? setFlag = EffectiveInteger(owner, "des_i_SPFlag_set01");
        int? resetFlag = EffectiveInteger(owner, "des_i_SPFlag_reset01");
        if (timer is { } timerDuration && timerDuration != 0)
        {
            if (setFlag is > 0) timerEffects.Add($"clears saved flag {setFlag}");
            if (resetFlag is > 0) timerEffects.Add($"sets saved flag {resetFlag}");
            if (invertFlag is > 0)
            {
                int timedFlag = invertFlag.Value % 10000;
                if (timedFlag > 0)
                    timerEffects.Add($"toggles saved flag {timedFlag} (invert index modulo 10000)");
            }
            string timerBehavior = timerDuration > 0
                ? $"Delayed follow-up after {timerDuration.ToString("R", CultureInfo.InvariantCulture)} seconds"
                : $"The nonzero negative timer {timerDuration.ToString("R", CultureInfo.InvariantCulture)} reaches the source TIMEElapsed check with an already-expired threshold once its start timestamp is nonzero";
            diagnostics.Add($"{timerBehavior}: "
                + (timerEffects.Count > 0 ? string.Join("; ", timerEffects) : "no positive saved-flag operations are configured") + ".");
        }
        else if (timer == 0)
        {
            diagnostics.Add(invertFlag is > 0
                ? $"No delayed follow-up; raw saved-flag index {invertFlag} is cleared on trigger exit, while set/reset flag changes persist."
                : "No delayed follow-up; configured set/reset flag changes persist after trigger exit.");
        }
        else
        {
            diagnostics.Add("The timer field is unavailable or non-finite, so delayed trigger behavior cannot be derived.");
        }
        diagnostics.Add("This is static callback/OVA interpretation, not proof that collision occurred or the trigger is active now.");
    }

    private static void AddCapabilityDetectorBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        var targetReferences = owner.ObjectReferences.Where(reference =>
                reference.VariableName.Equals("des_o_actor_capa", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(reference => (reference.TargetKey, reference.Source, reference.ArrayIndex)).Take(10).ToArray();
        int? capabilityMask = EffectiveInteger(owner, "des_i_capa_test");
        int? testType = EffectiveInteger(owner, "des_i_type_test");
        string testMode = testType switch
        {
            0 => "all linked objects must have the mask",
            1 => "any linked object may have the mask",
            2 => "all linked objects must lack the mask",
            3 => "any linked object may lack the mask",
            null => "the test mode is unavailable",
            _ => "the source's mixed true/false test branch is selected (the source reads two array slots)"
        };

        string targetDescription = targetReferences.Length == 0
            ? "No des_o_actor_capa object references were found in the loaded OVA data."
            : $"Serialized references: {string.Join(", ", targetReferences.Select(reference => $"{reference.TargetName ?? $"{reference.TargetKey:X8}"}{FormatArrayIndex(reference.ArrayIndex)} [{reference.Source}]"))}.";
        diagnostics.Add($"Detect_o_Capa checks capability mask {DescribeCapabilityMask(capabilityMask)}; {testMode}. {targetDescription}");
        diagnostics.Add("The detector changes its own capability 0x100 according to that test. The source loops through up to ten des_o_actor_capa slots; the inspector applies the engine's model-default/instance-prefix overlay and shows each effective slot's source. Target capabilities and callback execution are not resolved.");
        if (EffectiveInteger(owner, "des_i_SFlag") is { } savedFlag && savedFlag != 0)
            diagnostics.Add($"des_i_SFlag={savedFlag} configures an additional global-flag side effect in the source branches; its polarity depends on the selected test branch and is not summarized here.");
        diagnostics.Add("This is static callback/OVA analysis; current target capabilities and callback execution are not evaluated.");
    }

    private static string DescribeCapabilityMask(int? mask) => mask is { } value
        ? $"0x{unchecked((uint)value):X8}"
        : "unavailable";

    private static string FormatArrayIndex(int? index) => index is { } value ? $"[{value}]" : "";

    private static void AddBoundingVolumeCapabilitySetBehavior(List<string> diagnostics,
        CinematicScriptOwner owner)
    {
        var actorReference = GetEffectiveObjectReference(owner, "o_perso");
        uint? actorKey = EffectiveUnsigned(owner, "o_perso");
        string actorDescription = actorKey switch
        {
            0 => "a null actor reference",
            1 or 2 => $"main-actor slot {actorKey}",
            null when actorReference?.TargetName is { Length: > 0 } actorName
                => $"'{actorName}' ({actorReference.TargetKey:X8})",
            null when actorReference != null
                => DescribeObjectReference(actorReference),
            null => "an unavailable actor reference",
            _ when actorReference?.TargetName is { Length: > 0 } actorName
                => $"'{actorName}' ({actorReference.TargetKey:X8})",
            _ => $"object {(actorReference?.TargetKey ?? actorKey.Value):X8}, unresolved in the loaded object map"
        };

        var capabilityTarget = GetEffectiveObjectReference(owner, "o_perso_capa");
        string capabilityTargetDescription = EffectiveUnsigned(owner, "o_perso_capa") switch
        {
            0 => "the collision actor (o_perso_capa resolves null)",
            1 or 2 => $"main-actor slot {EffectiveUnsigned(owner, "o_perso_capa")}",
            _ when capabilityTarget?.TargetName is { Length: > 0 } targetName
                => $"'{targetName}' ({capabilityTarget.TargetKey:X8})",
            _ when capabilityTarget != null
                => DescribeObjectReference(capabilityTarget),
            _ => "the resolved collision actor if o_perso_capa is null, otherwise an unresolved target"
        };

        int? capability = EffectiveInteger(owner, "i_capa");
        int? clearInstead = EffectiveInteger(owner, "des_capa_a_faux");
        int? skipInactive = EffectiveInteger(owner, "des_i_test_si_actif");
        int? savedFlag = EffectiveInteger(owner, "des_i_met_SF");

        diagnostics.Add($"Detect_o_BV_CapaSet tests collision between this owner and {actorDescription} using the engine BV-pivot collision callback.");
        diagnostics.Add($"On collision it {(clearInstead is > 0 ? "clears" : "sets")} capability mask {DescribeCapabilityMask(capability)} on {capabilityTargetDescription}.");
        if (clearInstead is null)
            diagnostics.Add("des_capa_a_faux is unavailable; the source's capability add/remove branch cannot be determined.");
        if (skipInactive is > 0)
            diagnostics.Add("des_i_test_si_actif enables an additional guard: the BV test is skipped while the collision actor has control mask 0x400000.");
        if (savedFlag is > 0)
            diagnostics.Add($"On collision it also sets saved special flag {savedFlag}.");
        else if (savedFlag is null)
            diagnostics.Add("des_i_met_SF is unavailable; an optional saved-flag write cannot be determined.");
        diagnostics.Add("The source callback only applies these effects on a detected collision; it does not define a corresponding exit reset. Geometry overlap, live actor/capability state and callback execution are not evaluated.");
    }

    private static CinematicScriptObjectReference? GetEffectiveObjectReference(CinematicScriptOwner owner,
        string variableName) => owner.EffectiveObjectReference(variableName);

    private static void AddFlagEffect(List<string> effects, CinematicScriptOwner owner,
        string variableName, string operation)
    {
        int? flag = EffectiveInteger(owner, variableName);
        if (flag is > 0) effects.Add($"{operation} saved flag {flag}");
    }

    private static float? EffectiveFloat(CinematicScriptOwner owner, string name)
    {
        var variable = owner.EffectiveVariable(name);
        return variable != null && float.TryParse(variable.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : null;
    }

    private static void AddFourFlagDetectorBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        var flagTests = new List<string>();
        bool hasInvalidFlagEncoding = false;
        bool hasMissingFlagSlot = false;
        foreach (string variableName in new[] { "SF_1", "SF_2", "SF_3", "SF_4" })
        {
            int? encodedFlag = EffectiveInteger(owner, variableName);
            if (encodedFlag is null)
            {
                hasMissingFlagSlot = true;
                diagnostics.Add($"{variableName} is unavailable; the complete four-flag condition cannot be derived.");
                continue;
            }
            if (encodedFlag == 0) continue;
            if (encodedFlag < 0)
            {
                hasInvalidFlagEncoding = true;
                diagnostics.Add($"{variableName}={encodedFlag} is nonzero but outside the positive flag encoding; Iris does not treat it as an unused slot.");
                continue;
            }

            bool requireClear = encodedFlag > 10000;
            int flagIndex = requireClear ? encodedFlag.Value - 10000 : encodedFlag.Value;
            flagTests.Add($"saved flag {flagIndex} {(requireClear ? "clear" : "set")}");
        }

        diagnostics.Add(flagTests.Count == 0 && !hasInvalidFlagEncoding && !hasMissingFlagSlot
            ? "All four special-flag slots are zero; Iris's four-flag test passes without flag predicates."
            : flagTests.Count > 0
                ? $"Configured special-flag predicates (all must pass): {string.Join("; ", flagTests)}. Values above 10000 encode a required-clear flag by subtracting 10000."
                : "No valid special-flag predicates could be derived from the configured values.");

        int? onlyTrue = EffectiveInteger(owner, "des_i_only_vrai");
        int? setResult = EffectiveInteger(owner, "SF_result_a_1");
        int? clearResult = EffectiveInteger(owner, "SF_result_a_0");
        string trueEffects = FormatFlagEffects(setResult, clearResult, set: true);
        string falseEffects = FormatFlagEffects(setResult, clearResult, set: false);
        diagnostics.Add("When predicates pass, D4sf_go and D4sf_wait set the configured success result, "
            + $"clear the configured failure result, and enable capability 0x100{FormatSuffix(trueEffects)}.");
        diagnostics.Add(onlyTrue is null
            ? "des_i_only_vrai is unavailable; the polling false-branch behavior cannot be derived."
            : onlyTrue != 0
                ? "des_i_only_vrai is nonzero: D4sf_go leaves prior results/capability unchanged when predicates fail; D4sf_wait also has no false-branch reset."
                : "des_i_only_vrai is zero: D4sf_go reverses configured results and disables capability 0x100 when predicates fail; D4sf_wait does not perform that false-branch reset.");

        float? interval = EffectiveFloat(owner, "f_every");
        if (interval == -1)
        {
            diagnostics.Add("f_every=-1 selects D4sf_wait: initialization registers this owner in GST_Global.ao_optim_dialogue "
                + "and sets owner control flags 0x600000; the shared GST_Global loop clears those flags during its 60-second maintenance pass.");
        }
        else if (interval is > 0)
        {
            diagnostics.Add($"f_every={interval.Value.ToString("R", CultureInfo.InvariantCulture)} seconds selects the polling D4sf_go branch; "
                + "initialization backdates the first check, then the callback checks at the configured interval.");
        }
        else if (interval == 0)
        {
            diagnostics.Add("f_every=0 selects D4sf_go with a zero-second interval; initialization starts the check immediately, "
                + "but the repeated evaluation cadence depends on the AI scheduler.");
        }
        else
        {
            diagnostics.Add("f_every is unavailable or non-finite; the detector scheduling mode cannot be derived.");
        }
        diagnostics.Add("This is static callback/OVA analysis only; current save flags, owner activation, and callback scheduling are not evaluated.");
    }

    private static void AddSingleFlagDetectorBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        var predicates = new List<string>();
        AddFlagPredicate(predicates, owner, "des_i_specialFlag_vrai1", requireSet: true);
        AddFlagPredicate(predicates, owner, "des_i_specialFlag_vrai2", requireSet: true);
        AddFlagPredicate(predicates, owner, "des_i_specialFlag_faux1", requireSet: false);
        AddFlagPredicate(predicates, owner, "des_i_specialFlag_faux2", requireSet: false);

        int? requiredMatches = EffectiveInteger(owner, "des_i_nbre_conditions");
        diagnostics.Add(predicates.Count == 0
            ? "Detect_o_SpecialFlags has no positive saved-flag predicates in the effective OVA snapshot."
            : $"Detect_o_SpecialFlags counts passing predicates ({string.Join("; ", predicates)}); "
                + (requiredMatches is { } count
                    ? $"capability 0x100 is cleared when at least {count} pass, and set while fewer pass."
                    : "the required passing-count threshold des_i_nbre_conditions is unavailable."));

        float? interval = EffectiveFloat(owner, "f_frequence_test");
        diagnostics.Add(interval is { } seconds
            ? $"The detector polls saved flags on its source interval f_frequence_test={seconds.ToString("R", CultureInfo.InvariantCulture)} seconds."
            : "The detector's f_frequence_test interval is unavailable; its polling cadence cannot be derived.");
        diagnostics.Add("This is static callback/OVA analysis; current saved flags, detector activation and capability state are not evaluated.");
    }

    private static void AddFlagPredicate(List<string> predicates, CinematicScriptOwner owner,
        string variableName, bool requireSet)
    {
        int? flag = EffectiveInteger(owner, variableName);
        if (flag is > 0)
            predicates.Add($"{variableName}={flag} requires the flag {(requireSet ? "set" : "clear")}");
    }

    private static string FormatFlagEffects(int? setResult, int? clearResult, bool set)
    {
        var effects = new List<string>();
        if (setResult is > 0) effects.Add($"{(set ? "sets" : "clears")} saved flag {setResult}");
        if (clearResult is > 0) effects.Add($"{(set ? "clears" : "sets")} saved flag {clearResult}");
        return string.Join("; ", effects);
    }

    private static string FormatSuffix(string value) => value.Length == 0 ? "" : $" ({value})";

    private static int DeriveStartGateMask(CinematicScriptOwner owner)
    {
        int mask = 0;
        foreach (var gate in CinematicStartGates)
        {
            if (gate.Mask >= 0x80
                ? EffectiveInteger(owner, gate.Variable) is not 0 and not null
                : HasObjectReference(owner, gate.Variable)
                    || gate.Variable == "des_o_actor_zone1"
                        && EffectiveInteger(owner, "des_i_actor_type1") is not 0 and not null)
                mask |= gate.Mask;
        }
        return mask;
    }

    private static bool HasObjectReference(CinematicScriptOwner owner, string name) =>
        EffectiveUnsigned(owner, name) is > 0 and < uint.MaxValue;

    private static bool HasEffectiveValue(CinematicScriptOwner owner, string name) =>
        owner.EffectiveVariable(name) != null;

    private static uint? EffectiveUnsigned(CinematicScriptOwner owner, string name)
    {
        var variable = owner.EffectiveVariable(name);
        if (variable == null) return null;
        string value = variable.Value;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint hexadecimal) ? hexadecimal : null;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint decimalValue)
            ? decimalValue : null;
    }

    private static void AddInitializationFlagSuppression(List<string> diagnostics, CinematicScriptOwner owner)
    {
        int? ripleyMode = EffectiveInteger(owner, "des_i_ripley");
        int? flagMustBeSet = EffectiveInteger(owner, "des_i_SPFlag_associe_vrai");
        int? flagMustBeClear = EffectiveInteger(owner, "des_i_SPFlag_associe_faux");
        if (ripleyMode is > 0 || flagMustBeSet is not > 0 && flagMustBeClear is not > 0) return;

        string conditions = string.Join(" and ", new[]
        {
            DescribeAssociatedFlag(flagMustBeSet, mustBeSet: true),
            DescribeAssociatedFlag(flagMustBeClear, mustBeSet: false)
        }.Where(condition => condition != null));
        diagnostics.Add("Initialization suppression: Cine_Events destroys this owner if all configured "
            + $"associated save-flag conditions are already satisfied ({conditions}); "
            + (ripleyMode == 0
                ? "des_i_ripley=0 enables this check."
                : "the effective des_i_ripley mode is unavailable, so the check may be bypassed.")
            + " Current save flags are unavailable.");
    }

    private static string? DescribeAssociatedFlag(int? flag, bool mustBeSet)
    {
        if (flag is not > 0) return null;
        bool inverted = flag > 10000;
        int index = inverted ? flag.Value - 10000 : flag.Value;
        bool actualMustBeSet = inverted ? !mustBeSet : mustBeSet;
        return $"flag {index} {(actualMustBeSet ? "set" : "clear")}";
    }

    private static void AddCinematicType(List<string> diagnostics, CinematicScriptOwner owner)
    {
        int? type = EffectiveInteger(owner, "des_i_cine_type");
        if (type is null) return;

        var effects = new List<string>();
        if ((type.Value & 0x1) != 0) effects.Add("freeze gameplay input and send message 47 on completion");
        if ((type.Value & 0x2) != 0) effects.Add("send camera transition message 60 on completion");
        if ((type.Value & 0x4) != 0) effects.Add("hide the board panel during playback, then restore it");
        if ((type.Value & 0x8) != 0) effects.Add("apply the alternate input-freeze mode");
        int knownFlags = 0xF;
        int unknownFlags = type.Value & ~knownFlags;
        if (unknownFlags != 0) effects.Add($"unclassified type bits 0x{unknownFlags:X}");
        diagnostics.Add(effects.Count == 0
            ? "cine_type=0: no recognized camera/input/board-panel effects are requested."
            : $"cine_type=0x{type.Value:X}: {string.Join("; ", effects)}.");
    }

    private static void AddMpegPlayerBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        var switchReference = owner.EffectiveObjectReference("des_o_Interrupteur");
        string switchDescription = DescribeObjectReference(switchReference);
        int? switchFlag = EffectiveInteger(owner, "des_i_sf_on_off");
        int? materialIndex = EffectiveInteger(owner, "des_i_SubMat");
        int? materialLevel = EffectiveInteger(owner, "des_i_SubMatLevel");
        int? stopAtEnd = EffectiveInteger(owner, "des_i_StopAtEnd");
        int? modifierFlag = EffectiveInteger(owner, "des_i_sf_modifier_vrai");
        int? hereFlag = EffectiveInteger(owner, "des_i_sf_jesuisla");
        float? hereDuration = EffectiveFloat(owner, "des_f_duree_here");

        diagnostics.Add($"MPEG-player callback checks capability 0x100 on {switchDescription}; "
            + "a set capability starts playback, while the callback loop clears the request after consuming it.");
        diagnostics.Add(switchFlag is > 0
            ? $"Saved flag {switchFlag} is an alternate playback request; when set, initialization selects the playing material state."
            : switchFlag == 0
                ? "des_i_sf_on_off=0: no saved-flag playback request is configured."
                : "des_i_sf_on_off is unavailable; an alternate saved-flag playback request cannot be determined.");

        string material = materialIndex is not null && materialLevel is not null
            ? $"procedural material sub-index {materialIndex}, level {materialLevel}"
            : "the configured procedural material sub-index/level (OVA incomplete)";
        diagnostics.Add($"Playback changes this object's visibility and parameter 10 on {material}; source values 1 and 2 select the stopped and playing states.");
        diagnostics.Add(stopAtEnd is null
            ? "des_i_StopAtEnd is unavailable; end-of-video material behavior cannot be derived."
            : stopAtEnd != 0
                ? "des_i_StopAtEnd is enabled: initialization also writes procedural parameter 1 when entering the playback branch."
                : "des_i_StopAtEnd is disabled: the initialization path does not force procedural parameter 1 at the end.");

        if (modifierFlag is > 0)
            diagnostics.Add($"Saved flag {modifierFlag} selects the modifier branch: the callback stops its current track, selects the stopped material state, and enables the configured material layer.");
        if (hereFlag is > 0)
            diagnostics.Add(hereDuration is > 0
                ? $"Saved flag {hereFlag} selects the delayed presence branch; after {hereDuration.Value.ToString("R", CultureInfo.InvariantCulture)} seconds it changes the material layer and stops the configured sound modifier."
                : $"Saved flag {hereFlag} selects a delayed presence branch, but its delay is unavailable or nonpositive.");

        diagnostics.Add("The loop also consumes capability 0x2000 and this object's capability 0x2 as stop/toggle requests, and starts or stops configured sound modifiers. Live capabilities, save flags, Bink frames, and playback state are not evaluated.");
    }

    private static string DescribeObjectReference(CinematicScriptObjectReference? reference)
    {
        if (reference == null) return "reference unavailable";
        if (!string.IsNullOrWhiteSpace(reference.TargetName))
            return $"'{reference.TargetName}' ({reference.TargetKey:X8})";
        return reference.TargetKey switch
        {
            0 => "null object reference",
            1 or 2 => $"main-actor slot {reference.TargetKey}",
            uint.MaxValue => "invalid object reference",
            _ => $"object {reference.TargetKey:X8} (unresolved in the loaded object map)"
        };
    }

    private static string DescribeObjectReference(CinematicScriptOwner owner, string variableName)
    {
        var reference = owner.EffectiveObjectReference(variableName);
        if (reference != null) return DescribeObjectReference(reference);

        return EffectiveUnsigned(owner, variableName) switch
        {
            null => "reference unavailable",
            0 => "null object reference",
            1 or 2 => $"main-actor slot {EffectiveUnsigned(owner, variableName)}",
            uint.MaxValue => "invalid object reference",
            uint key => $"object {key:X8} (unresolved in the loaded object map)"
        };
    }

    private static void AddDialogueMessageBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        int? defaultPriority = EffectiveInteger(owner, "des_i_priorite_par_defaut");
        string priorityDescription = defaultPriority switch
        {
            0 => "5000 (source initialization default for a zero value)",
            int value => value.ToString(CultureInfo.InvariantCulture),
            _ => "5000 if the unavailable field is zero"
        };
        diagnostics.Add(owner.CallbackNames.Contains(DialogueInitCallback, StringComparer.Ordinal)
            ? $"Cinematic dialogue receiver initializes its line cursor to -1, uses default priority {priorityDescription}, then switches to message reception."
            : "Cinematic dialogue receiver callback is present; its initialization callback is not mapped on this owner.");
        diagnostics.Add("The receiver scans queued message ID 30, substitutes its default priority when Int2 is zero, and selects the highest-priority eligible message. Int1=200 advances the current line; other nonnegative Int1 values select line (Int1 mod 100). Display arbitration and current audio/companion state are runtime-only.");

        CinematicDialogueLine[] configuredLines = (owner.DialogueLines ?? [])
            .Where(line => line.TextBankKey != 0 || line.SoundId is > 0).ToArray();
        diagnostics.Add(configuredLines.Length == 0
            ? "No populated dialogue text or associated-sound slots were decoded from the owner’s OVA arrays."
            : $"Decoded {configuredLines.Length} populated dialogue slots from model/instance OVA arrays, including text-bank references and available associated-audio metadata.");

        var defaultSpeaker = owner.EffectiveObjectReference("des_ai_perso_localisation_par");
        diagnostics.Add($"For the selected line, the receiver routes the associated sound parameter and per-line near/far/volume values to the shared text/audio manager. Speaker position uses message GAO2 when set, otherwise the configured des_ai_perso_localisation_par reference ({DescribeObjectReference(defaultSpeaker)}); zero line-volume defaults to 0.8. This callback selects dialogue/audio metadata but does not itself drive facial morphs.");
    }

    private static CinematicScriptVariable? EffectiveVariable(CinematicScriptOwner owner, string name) =>
        owner.EffectiveVariable(name);

    private static void AddSpecialFlagTest(List<string> diagnostics, CinematicScriptOwner owner,
        int testsMask, string variableName, int requiredMask, int testNumber)
    {
        int? flag = EffectiveInteger(owner, variableName);
        if (flag is null) return;

        bool testEnabled = (testsMask & requiredMask) != 0;
        if (flag <= 0)
        {
            if (testEnabled)
                diagnostics.Add($"Start test {testNumber} is enabled, but {variableName}={flag} makes this flag test fail in the source callback.");
            return;
        }

        string flagCondition = flag > 10000
            ? $"saved special flag {flag - 10000} must be clear"
            : $"saved special flag {flag} must be set";
        if (!testEnabled)
        {
            diagnostics.Add($"Start test {testNumber} is configured ({flagCondition}) but disabled: "
                + $"i_tests_a_reussir=0x{testsMask:X} does not contain mask 0x{requiredMask:X}.");
            return;
        }

        diagnostics.Add($"Start test {testNumber} is active: {flagCondition}; current save state is unavailable.");
    }

    private static void AddCinematicInterruptionBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        int interruptionMask = CinematicInterruptionGates
            .Where(gate => gate.Mask == 0x80
                ? EffectiveInteger(owner, gate.Variable) is not 0 and not null
                : HasObjectReference(owner, gate.Variable))
            .Sum(gate => gate.Mask);
        var configuredGates = CinematicInterruptionGates
            .Where(gate => (interruptionMask & gate.Mask) != 0)
            .Select(gate => gate.Description).ToArray();

        diagnostics.Add(configuredGates.Length == 0
            ? "No cinematic interruption tests are derived from the effective exit-gate OVA fields."
            : $"Interruption tests derived by cine_init (mask 0x{interruptionMask:X}): "
                + $"{string.Join("; ", configuredGates)}. Iris keeps the cinematic active until every configured "
                + "exit test passes; the callback then clears active-owner bit 0x4000, which sends playback "
                + "through its stop-all and completion path.");

        if ((interruptionMask & 0x001) != 0 && !HasEffectiveValue(owner, "des_o_actor_detec_exit1"))
            diagnostics.Add("The exit-zone test also reads des_o_actor_detec_exit1, which is unavailable in the loaded OVA snapshot.");
        if (CinematicInterruptionGates.Any(gate => !HasEffectiveValue(owner, gate.Variable)))
            diagnostics.Add("Some cinematic exit-gate OVA fields are unavailable; the derived interruption mask may be incomplete.");

        float? duration = EffectiveFloat(owner, "des_f_duree_totale");
        diagnostics.Add(duration switch
        {
            0 => "des_f_duree_totale=0 selects Iris's large sentinel timeout; this field does not provide a practical duration stop.",
            { } seconds => $"des_f_duree_totale={seconds.ToString("G", CultureInfo.InvariantCulture)} is used as the event-track timeout; playback stops when its elapsed timer expires.",
            _ => "des_f_duree_totale is unavailable; the event-track timeout cannot be derived from this OVA snapshot."
        });
    }

    private static void AddEventTrackPlaybackBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool startsAllTracks = HasCinematicFunctionReference(owner, CinematicGoToEventsCallback);
        bool advancesPlayback = HasCinematicFunctionReference(owner, CinematicPlayTrackCallback);
        bool endsPlayback = HasCinematicFunctionReference(owner, CinematicEndCallback);

        if (startsAllTracks)
            diagnostics.Add("GoToEvents marks this owner as playing and calls TRAPlayAll on its runtime EVE list; "
                + "that starts every embedded track together, not a track inferred from the scene name.");
        if (advancesPlayback)
        {
            int? playbackRate = EffectiveInteger(owner, "i_enLecture");
            diagnostics.Add(playbackRate is { } rate
                ? $"The play callback advances its script timecode by frame delta × i_enLecture ({rate}); "
                    + "the serialized rate is a snapshot, not live playback state."
                : "The play callback advances its script timecode by frame delta × i_enLecture; "
                    + "that variable is unavailable in the loaded OVA snapshot.");
            diagnostics.Add("On timeout or when the cinematic active-owner bit 0x4000 clears, the play callback "
                + "calls TRAStopAll; it enters the end callback after TRAIsStopAll reports every owner track stopped.");
        }
        if (endsPlayback)
            diagnostics.Add("EndOfEvents calls TRAReinitAll on the owner's runtime EVE list, then clears i_playing "
                + "and applies configured completion flags. This resets track cursors; it does not prove the owner "
                + "was activated in the game.");
    }

    private static int? EffectiveInteger(CinematicScriptOwner owner, string name)
    {
        var variable = owner.EffectiveVariable(name);
        if (variable == null) return null;
        if (variable.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(variable.Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int hexadecimal) ? hexadecimal : null;
        return int.TryParse(variable.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : null;
    }

    private static bool HasCinematicFunctionReference(CinematicScriptOwner owner, string functionName) =>
        owner.CallbackNames.Contains(functionName, StringComparer.Ordinal)
        || owner.ModelFunctionNames.Contains(functionName, StringComparer.Ordinal);
}
