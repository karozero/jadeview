namespace JadeView;

internal sealed record CinematicSoundCommandBinding(string FunctionName, int? ModifierId,
    string Status, string[] SoundKeys, string[] MatchingOvaSlots);

internal static class CinematicSoundCommandResolver
{
    internal static CinematicSoundCommandBinding? Resolve(CinematicLiteralCall call, uint worldKey,
        SceneDocument document, IEnumerable<CinematicScriptOwner>? scriptOwners = null,
        IReadOnlyList<Entry>? archiveEntries = null)
    {
        string functionName = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        if (IsSoundBankRequest(functionName))
            return ResolveSoundBankRequest(call, functionName, worldKey, document, archiveEntries);
        if (!IsSoundModifierCommand(functionName)) return null;
        if (call.Arguments.Length == 0)
            return new(functionName, null, "modifier ID argument is missing", [], []);

        int modifierId = unchecked((int)call.Arguments[0]);
        string[] matchingOvaSlots = FindMatchingOvaSlots(call.Target, worldKey, modifierId,
            scriptOwners ?? document.CinematicScriptOwners.Values);
        if (modifierId == -10)
            return new(functionName, modifierId, "ignored -10 sentinel", [], matchingOvaSlots);
        if (modifierId < -1)
            return new(functionName, modifierId, "unknown negative modifier sentinel", [], matchingOvaSlots);

        WorldData? targetWorld = document.Worlds.FirstOrDefault(world => world.Key == worldKey);
        SceneObject[] targetObjects = targetWorld?.Objects
            .Where(sceneObject => sceneObject.GaoKey == call.Target).ToArray() ?? [];
        if (targetObjects.Length == 0)
            return new(functionName, modifierId, "target GAO is not loaded in this world", [], matchingOvaSlots);

        SceneModifier[] attachedModifiers = targetObjects.SelectMany(sceneObject => sceneObject.Modifiers)
            .Where(modifier => modifier.Type == 8 && modifier.Data.Length >= 12).ToArray();
        if (modifierId == -1)
        {
            CinematicSoundModifier[] allResolved = attachedModifiers
                .Select(modifier => CinematicSoundModifier.Read(modifier, document))
                .OfType<CinematicSoundModifier>().Distinct().ToArray();
            string aggregateStatus = allResolved.Length > 0
                ? $"all sound modifiers on target ({allResolved.Length} decoded)"
                : "all sound modifiers requested, but none resolved to an audio asset";
            return new(functionName, modifierId, aggregateStatus,
                allResolved.Select(modifier => modifier.SoundKey.ToString("X8")).Distinct().ToArray(),
                matchingOvaSlots);
        }

        SceneModifier[] matchingModifiers = attachedModifiers
            .Where(modifier => Preview.I32(modifier.Data, 8) == modifierId).ToArray();
        if (matchingModifiers.Length == 0)
            return new(functionName, modifierId, "no matching type-8 modifier on target", [], matchingOvaSlots);
        if (matchingModifiers.Length > 1)
            return new(functionName, modifierId,
                $"ambiguous: {matchingModifiers.Length} type-8 modifiers share this ID", [], matchingOvaSlots);

        CinematicSoundModifier? resolved = CinematicSoundModifier.Read(matchingModifiers[0], document);
        return resolved is null
            ? new(functionName, modifierId, "matching modifier sound resource could not be resolved", [], matchingOvaSlots)
            : new(functionName, modifierId, "resolved target modifier",
                [resolved.SoundKey.ToString("X8")], matchingOvaSlots);
    }

    internal static string Describe(CinematicLiteralCall call, uint worldKey, SceneDocument document,
        IEnumerable<CinematicScriptOwner>? scriptOwners = null)
    {
        CinematicSoundCommandBinding? binding = Resolve(call, worldKey, document, scriptOwners);
        if (binding is null) return "";

        string modifier = IsSoundBankRequest(binding.FunctionName)
            ? binding.ModifierId is int soundSlot ? $"sound-bank slot {soundSlot}" : "sound-bank slot ?"
            : binding.ModifierId switch
            {
                -1 => "all modifiers",
                { } value => $"modifier {value}",
                _ => "modifier ?"
            };
        string sound = binding.SoundKeys.Length == 0
            ? binding.Status
            : $"sound {string.Join(", ", binding.SoundKeys)} ({binding.Status})";
        string ovaSlots = binding.MatchingOvaSlots.Length == 0
            ? ""
            : $"; matches owner OVA slot(s): {string.Join(", ", binding.MatchingOvaSlots)}";
        return $"{modifier} → {sound}{ovaSlots}. Static asset link only; this does not prove playback.";
    }

    private static CinematicSoundCommandBinding ResolveSoundBankRequest(CinematicLiteralCall call,
        string functionName, uint worldKey, SceneDocument document, IReadOnlyList<Entry>? archiveEntries)
    {
        int expectedArguments = functionName == "SNDRequestPlay" ? 2 : 4;
        if (call.Arguments.Length != expectedArguments)
            return new(functionName, null,
                $"expected {expectedArguments} arguments, found {call.Arguments.Length}", [], []);

        int soundSlot = unchecked((int)call.Arguments[0]);
        WorldData? targetWorld = document.Worlds.FirstOrDefault(world => world.Key == worldKey);
        SceneObject[] targets = targetWorld?.Objects.Where(sceneObject => sceneObject.GaoKey == call.Target).ToArray() ?? [];
        bool resolvedFromOtherWorld = false;
        if (targets.Length == 0)
        {
            targets = document.Worlds.SelectMany(world => world.Objects)
                .Where(sceneObject => sceneObject.GaoKey == call.Target).ToArray();
            resolvedFromOtherWorld = targets.Length > 0;
        }
        if (targets.Length == 0)
            return new(functionName, soundSlot, "target GAO is not loaded in any world", [], []);
        if (targets.Length > 1)
        {
            uint[] candidateBankKeys = targets.Select(target => target.SoundBankKey).Distinct().ToArray();
            if (!resolvedFromOtherWorld || candidateBankKeys.Length != 1)
                return new(functionName, soundSlot,
                    $"ambiguous target GAO ({targets.Length} objects across loaded worlds)", [], []);
        }

        uint bankKey = targets[0].SoundBankKey;
        if (bankKey == uint.MaxValue || !document.CinematicSoundBanks.TryGetValue(bankKey, out uint[]? sounds))
            return new(functionName, soundSlot, resolvedFromOtherWorld
                ? "matching GAO was found in another loaded world, but its sound bank is missing or undecoded"
                : "target sound bank is missing or undecoded", [], []);
        if (soundSlot < 0 || soundSlot >= sounds.Length)
            return new(functionName, soundSlot, $"sound-bank slot is outside the decoded bank ({sounds.Length} entries)", [], []);

        uint soundKey = sounds[soundSlot];
        if (soundKey is 0 or uint.MaxValue)
            return new(functionName, soundSlot, "sound-bank slot is empty", [], []);
        if (archiveEntries != null && !archiveEntries.Any(entry => entry.Key == soundKey))
            return new(functionName, soundSlot,
                $"sound-bank slot references missing archive resource {soundKey:X8}", [], []);
        string bindingStatus = functionName == "SNDRequestPlay"
            ? "resolved local sound-bank slot" : "resolved local sound-bank slot; random frequency range is not reconstructed";
        if (resolvedFromOtherWorld)
            bindingStatus += " via matching GAO in another loaded world";
        return new(functionName, soundSlot, bindingStatus, [soundKey.ToString("X8")], []);
    }

    private static string[] FindMatchingOvaSlots(uint targetKey, uint worldKey, int commandModifierId,
        IEnumerable<CinematicScriptOwner> scriptOwners)
    {
        if (commandModifierId < 0) return [];
        return scriptOwners.Where(owner => owner.WorldKey == worldKey && owner.OwnerKey == targetKey)
            .SelectMany(owner => owner.Variables
                .Where(variable => variable.Name.StartsWith("des_i_SndMdf_", StringComparison.OrdinalIgnoreCase))
                .Select(variable => variable.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(variableName => (Name: variableName, Variable: owner.EffectiveVariable(variableName)))
                .Where(entry => entry.Variable != null
                    && TryReadInteger(entry.Variable.Value, out int configuredId)
                    && configuredId == commandModifierId)
                .Select(entry => CinematicScriptAudioBindingResolver.DescribeRole(entry.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryReadInteger(string value, out int result)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out result);
        return int.TryParse(value, out result);
    }

    private static bool IsSoundModifierCommand(string functionName) => functionName is
        "SNDPlayModifier" or "SNDPauseModifier" or "SNDStopModifier"
        or "SNDSetFlagModifier" or "SNDResetFlagModifier";

    private static bool IsSoundBankRequest(string functionName) => functionName is
        "SNDRequestPlay" or "SNDRequestPlayRandomFreq";
}
