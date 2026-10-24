using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptFlagFlowAnalyzer
{
    private const string CinematicStartCallback = "Cine_Events_cine_tests_depart";
    private const string CinematicInitCallback = "Cine_Events_cine_firstinit";
    private const string CinematicEndCallback = "Cine_Events_Cine_exec_EndOfEvents";
    private const string ZoneInitCallback = "Detect_o_Zone_doz_init";
    private const string BoundingVolumeCapabilitySetCallback = "Detect_o_BV_CapaSet_detect_go";
    private const string SingleFlagDetectorCallback = "Detect_o_SpecialFlags_dosf_tests";
    private const string FourFlagWaitCallback = "Detect_o_4SpecialFlags_D4sf_wait";
    private const string FourFlagGoCallback = "Detect_o_4SpecialFlags_D4sf_go";
    private const string SuperSpecialFlagCallbackPrefix = "Detect_o_SuperSpecialFlags_ssf_";
    private const string HelperTextInitCallback = "helper_text_hlptxt_init";
    private const string HelperTextDepartureCallback = "helper_text_hlptxt_tests_depart";
    private const string HelperTextFinishCallback = "helper_text_hlptxt_exec_fin_ou_abort";
    private const string HelperTextWaitCallback = "helper_text_hlptxt_tests_wait";
    private const string GeneralCameraActivationCallback = "gencam_mario_gcm_exec_activation";

    internal static CinematicScriptFlagAccess[] GetAccesses(CinematicScriptOwner owner)
    {
        var accesses = new List<CinematicScriptFlagAccess>();
        bool hasCallback(string callback) => owner.CallbackNames.Contains(callback, StringComparer.Ordinal);

        if (hasCallback(CinematicStartCallback))
        {
            AddEncodedRead(accesses, owner, "des_i_specialFlag_test1", "start gate test 1");
            AddEncodedRead(accesses, owner, "des_i_specialFlag_test2", "start gate test 2");
        }

        if (hasCallback(CinematicInitCallback) && EffectiveInteger(owner, "des_i_ripley") == 0)
        {
            AddEncodedRead(accesses, owner, "des_i_SPFlag_associe_vrai", "replay suppression condition");
            AddEncodedRead(accesses, owner, "des_i_SPFlag_associe_faux", "replay suppression condition");
        }

        if (hasCallback(CinematicEndCallback))
        {
            AddPositiveWrite(accesses, owner, "des_i_SPFlag_associe_vrai", "sets on cinematic completion");
            AddPositiveWrite(accesses, owner, "des_i_SPFlag_associe_faux", "clears on cinematic completion");
        }

        if (hasCallback(ZoneInitCallback))
        {
            AddZoneDetectorFlagAccesses(accesses, owner);
        }

        if (hasCallback(BoundingVolumeCapabilitySetCallback))
            AddPositiveWrite(accesses, owner, "des_i_met_SF", "sets when the BV-pivot collision is detected");

        if (hasCallback(SingleFlagDetectorCallback))
        {
                AddDirectRead(accesses, owner, "des_i_specialFlag_vrai1", "requires set", requiredSet: true);
                AddDirectRead(accesses, owner, "des_i_specialFlag_vrai2", "requires set", requiredSet: true);
                AddDirectRead(accesses, owner, "des_i_specialFlag_faux1", "requires clear", requiredSet: false);
                AddDirectRead(accesses, owner, "des_i_specialFlag_faux2", "requires clear", requiredSet: false);
        }

        if (hasCallback(FourFlagWaitCallback) || hasCallback(FourFlagGoCallback))
        {
            foreach (string variableName in new[] { "SF_1", "SF_2", "SF_3", "SF_4" })
                AddEncodedRead(accesses, owner, variableName, "four-flag condition");
            AddPositiveWrite(accesses, owner, "SF_result_a_1", "condition result flag");
            AddPositiveWrite(accesses, owner, "SF_result_a_0", "condition result flag");
        }

        if (owner.CallbackNames.Any(callback => callback.StartsWith(SuperSpecialFlagCallbackPrefix,
                StringComparison.Ordinal)))
            AddSuperSpecialFlagAccesses(accesses, owner);

        if (hasCallback(HelperTextInitCallback))
            AddHelperTextInitializationFlagAccesses(accesses, owner);
        if (hasCallback(HelperTextDepartureCallback))
        {
            AddEncodedRead(accesses, owner, "des_i_specialFlag_test1", "helper-text readiness predicate");
            AddEncodedRead(accesses, owner, "des_i_specialFlag_test2", "helper-text readiness predicate");
        }
        if (hasCallback(HelperTextFinishCallback) || hasCallback(HelperTextWaitCallback))
        {
            AddPositiveWrite(accesses, owner, "des_i_SPFlag_associe_vrai",
                "conditional helper-text completion/replay-state update");
            AddPositiveWrite(accesses, owner, "des_i_SPFlag_associe_faux",
                "conditional helper-text completion/replay-state update");
        }

        if (hasCallback(GeneralCameraActivationCallback))
        {
            AddCameraFlagReads(accesses, owner, "des_ai_special_flags", inverted: false);
            AddCameraFlagReads(accesses, owner, "des_ai_special_flags_off", inverted: true);
        }

        AddSourceGlobalFlagAccesses(accesses, owner);

        return accesses.DistinctBy(access => (access.Flag, access.Access, access.Description,
            access.RequiredSet, access.WrittenSet)).ToArray();
    }

    private static void AddSuperSpecialFlagAccesses(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner)
    {
        int? mode = EffectiveInteger(owner, "Mode_ET");
        for (int groupIndex = 1; groupIndex <= 3; groupIndex++)
        {
            string flagArrayName = $"SF_Et_{groupIndex}";
            string stateArrayName = $"Et_{groupIndex}_etatSF";
            var flags = owner.EffectiveIntegerArray(flagArrayName);
            if (flags.FirstOrDefault(entry => entry.Index == 0)?.Value is not > 0) continue;
            var states = owner.EffectiveIntegerArray(stateArrayName);
            foreach (var flagEntry in flags)
            {
                if (flagEntry.Value <= 0) continue;
                var stateEntry = states.FirstOrDefault(entry => entry.Index == flagEntry.Index);
                string combination = mode switch
                {
                    { } configuredMode when configuredMode != 0 => $"group {groupIndex} AND slot",
                    0 => $"group {groupIndex} OR slot",
                    _ => $"group {groupIndex} slot"
                };
                string predicate = stateEntry == null
                    ? "required polarity unavailable"
                    : $"requires {(stateEntry.Value == 0 ? "clear" : "set")}";
                accesses.Add(new(flagEntry.Value, "reads", $"{flagArrayName}[{flagEntry.Index}]",
                    $"super-special flag condition, {combination}, {predicate}",
                    RequiredSet: stateEntry == null ? null : stateEntry.Value != 0));
            }
        }

        AddPositiveWrite(accesses, owner, "SF_result_a_1",
            "conditional success-result flag (set on success, cleared by polling test on failure)");
        AddPositiveWrite(accesses, owner, "SF_result_a_0",
            "conditional failure-result flag (cleared on success, set by polling test on failure)");
    }

    private static void AddHelperTextInitializationFlagAccesses(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner)
    {
        if (EffectiveInteger(owner, "des_i_ripley") != 0) return;
        AddEncodedRead(accesses, owner, "des_i_SPFlag_associe_vrai", "initial replay-suppression check");
        AddEncodedRead(accesses, owner, "des_i_SPFlag_associe_faux", "initial replay-suppression check");
    }

    private static void AddCameraFlagReads(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner, string variableName, bool inverted)
    {
        foreach (var entry in owner.EffectiveIntegerArray(variableName))
        {
            if (entry.Value <= 0) continue;

            bool encodedClear = entry.Value >= 10000;
            int flag = encodedClear ? entry.Value - 10000 : entry.Value;
            if (flag <= 0) continue;
            bool requiredSet = (encodedClear == false) != inverted;
            string variableSlot = $"{variableName}[{entry.Index}]";
            accesses.Add(new(flag, "reads", variableSlot,
                $"camera activation predicate, requires {(requiredSet ? "set" : "clear")}",
                RequiredSet: requiredSet));
        }
    }

    internal static CinematicScriptFlagLink[] FindRelatedOwners(CinematicScriptOwner selected,
        IEnumerable<CinematicScriptOwner> candidates,
        IEnumerable<CinematicScriptEventTrack>? eventTracks = null)
    {
        var graph = BuildLinkGraph(candidates, eventTracks);
        return graph.GetValueOrDefault(selected.Id, []);
    }

    internal static CinematicScriptFlagLink[] PotentialWritersForFlag(
        IEnumerable<CinematicScriptFlagLink> links, int flag) => links
        .Where(link => link.Flag == flag
            && link.Relation == CinematicScriptFlagLinkRelation.RelatedOwnerWrites)
        .DistinctBy(link => link.RelatedOwner.Id)
        .OrderBy(link => link.RelatedOwner.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(link => link.RelatedOwner.OwnerKey)
        .ToArray();

    internal static Dictionary<CinematicScriptOwnerId, CinematicScriptFlagLink[]> BuildLinkGraph(
        IEnumerable<CinematicScriptOwner> owners,
        IEnumerable<CinematicScriptEventTrack>? eventTracks = null)
    {
        var ownerArray = owners.ToArray();
        var accessesByFlag = new Dictionary<int, (List<OwnerAccess> Readers, List<OwnerAccess> Writers)>();
        foreach (var owner in ownerArray)
        foreach (var access in GetAccesses(owner))
        {
            AddAccess(owner, access);
        }

        var ownersById = ownerArray.ToDictionary(owner => owner.Id);
        foreach (var track in eventTracks ?? [])
        {
            if (!ownersById.TryGetValue(track.OwnerId, out var owner)) continue;
            foreach (var (time, call) in track.Calls)
            {
                string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                bool setsFlag = function == "ARRGlobalIntFlagSet";
                bool clearsFlag = function == "ARRGlobalIntFlagClear";
                bool testsFlag = function == "ARRGlobalIntFlagTest";
                if ((!setsFlag && !clearsFlag && !testsFlag) || call.Arguments.Length == 0
                    || call.Arguments[0] is 0 or > int.MaxValue) continue;
                int flag = (int)call.Arguments[0];
                string access = testsFlag ? "reads" : "writes";
                string effect = setsFlag ? "sets" : clearsFlag ? "clears" : "tests";
                AddAccess(owner, new(flag, access, function,
                    $"{track.LinkName} {track.EventKey:X8} at {time.ToString("G", CultureInfo.InvariantCulture)}s "
                    + $"{effect} this global flag",
                    WrittenSet: setsFlag ? true : clearsFlag ? false : null));
            }
        }

        var linksByOwner = ownerArray.ToDictionary(owner => owner.Id,
            _ => new List<CinematicScriptFlagLink>());
        foreach (var (flag, endpoints) in accessesByFlag)
        foreach (var writer in endpoints.Writers)
        foreach (var reader in endpoints.Readers)
        {
            if (writer.Owner.Id == reader.Owner.Id) continue;

            string stateRelation = DescribeStateRelation(writer.Access.WrittenSet, reader.Access.RequiredSet);

            linksByOwner[writer.Owner.Id].Add(new(reader.Owner, flag,
                CinematicScriptFlagLinkRelation.ThisOwnerWrites,
                $"this owner writes {writer.Access.VariableName} ({writer.Access.Description}); related owner reads {reader.Access.VariableName} ({reader.Access.Description}); {stateRelation}"));
            linksByOwner[reader.Owner.Id].Add(new(writer.Owner, flag,
                CinematicScriptFlagLinkRelation.RelatedOwnerWrites,
                $"related owner writes {writer.Access.VariableName} ({writer.Access.Description}); this owner reads {reader.Access.VariableName} ({reader.Access.Description}); {stateRelation}"));
        }

        return linksByOwner.ToDictionary(pair => pair.Key,
            pair => pair.Value.DistinctBy(link => (link.RelatedOwner.Id, link.Flag, link.Relation, link.Direction))
                .OrderBy(link => link.RelatedOwner.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(link => link.Flag)
                .ToArray());

        void AddAccess(CinematicScriptOwner owner, CinematicScriptFlagAccess access)
        {
            if (!accessesByFlag.TryGetValue(access.Flag, out var endpoints))
            {
                endpoints = ([], []);
                accessesByFlag.Add(access.Flag, endpoints);
            }

            var ownerAccess = new OwnerAccess(owner, access);
            if (access.Access == "reads") endpoints.Readers.Add(ownerAccess);
            else endpoints.Writers.Add(ownerAccess);
        }
    }

    private static string DescribeStateRelation(bool? writtenSet, bool? requiredSet) =>
        (writtenSet, requiredSet) switch
        {
            (true, true) or (false, false) => "configured write matches the reader predicate; runtime order is unknown",
            (true, false) or (false, true) => "configured write opposes the reader predicate; other writes or branches may apply",
            _ => "configured state compatibility is unknown"
        };

    private static void AddEncodedRead(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner, string variableName, string description)
    {
        int? encodedFlag = EffectiveInteger(owner, variableName);
        if (encodedFlag is not > 0) return;

        bool requireClear = encodedFlag >= 10000;
        int flag = requireClear ? encodedFlag.Value - 10000 : encodedFlag.Value;
        if (flag > 0)
            accesses.Add(new(flag, "reads", variableName,
                $"{description}, requires {(requireClear ? "clear" : "set")}",
                RequiredSet: !requireClear));
    }

    private static void AddPositiveWrite(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner, string variableName, string description)
    {
        int? flag = EffectiveInteger(owner, variableName);
        if (flag is > 0)
            accesses.Add(new(flag.Value, "writes", variableName, description));
    }

    private static void AddDirectRead(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner, string variableName, string description, bool? requiredSet = null)
    {
        int? flag = EffectiveInteger(owner, variableName);
        if (flag is > 0)
            accesses.Add(new(flag.Value, "reads", variableName, description, RequiredSet: requiredSet));
    }

    private static void AddZoneDetectorFlagAccesses(List<CinematicScriptFlagAccess> accesses,
        CinematicScriptOwner owner)
    {
        float? timer = EffectiveFloat(owner, "des_f_minuterie");
        AddZoneFlagWrite("des_i_SPFlag_set01", "sets saved flag on collision entry", true);
        AddZoneFlagWrite("des_i_SPFlag_reset01", "clears saved flag on collision entry", false);
        int? encodedInvertFlag = EffectiveInteger(owner, "des_i_SPFlag_invert01");
        if (encodedInvertFlag is not > 0) return;

        // Iris uses the raw serialized index for the entry set and (when there is
        // no timer) the exit clear. Only the timed follow-up normalizes modulo 10000.
        AddZoneFlagWrite("des_i_SPFlag_invert01", "sets raw saved-flag index on collision entry", true);
        if (timer == 0)
        {
            AddWrite(encodedInvertFlag.Value, "des_i_SPFlag_invert01",
                "clears raw saved-flag index on collision exit", writtenSet: false);
        }
        else if (timer is { } duration && duration != 0)
        {
            int timedFlag = encodedInvertFlag.Value % 10000;
            if (timedFlag > 0)
                accesses.Add(new(timedFlag, "writes", "des_i_SPFlag_invert01",
                    "toggles saved flag after the nonzero timer expires (flag index modulo 10000)"));
        }

        void AddZoneFlagWrite(string variableName, string description, bool writtenSet)
        {
            int? flag = EffectiveInteger(owner, variableName);
            if (flag is not > 0) return;
            AddWrite(flag.Value, variableName, description, writtenSet);
            if (timer is not { } duration || duration == 0) return;

            bool delayedValue = variableName == "des_i_SPFlag_set01" ? false
                : variableName == "des_i_SPFlag_reset01" ? true : writtenSet;
            if (variableName == "des_i_SPFlag_set01" || variableName == "des_i_SPFlag_reset01")
                AddWrite(flag.Value, variableName,
                    $"reverses to {(delayedValue ? "set" : "clear")} after the nonzero timer expires", delayedValue);
        }

        void AddWrite(int flag, string variableName, string description, bool writtenSet) =>
            accesses.Add(new(flag, "writes", variableName, description, WrittenSet: writtenSet));
    }

    private static float? EffectiveFloat(CinematicScriptOwner owner, string name)
    {
        var variable = owner.EffectiveVariable(name);
        return variable != null && float.TryParse(variable.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : null;
    }

    private static int? EffectiveInteger(CinematicScriptOwner owner, string name)
    {
        var variable = owner.EffectiveVariable(name);
        return variable != null && int.TryParse(variable.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private sealed record OwnerAccess(CinematicScriptOwner Owner, CinematicScriptFlagAccess Access);
}

internal sealed record CinematicScriptFlagAccess(int Flag, string Access, string VariableName, string Description,
    bool? RequiredSet = null, bool? WrittenSet = null);
internal enum CinematicScriptFlagLinkRelation
{
    ThisOwnerWrites,
    RelatedOwnerWrites
}

internal sealed record CinematicScriptFlagLink(CinematicScriptOwner RelatedOwner, int Flag,
    CinematicScriptFlagLinkRelation Relation, string Direction);
