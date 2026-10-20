namespace JadeView;

internal static class PcMorphControllerDiscovery
{
    private const string LeaderVariable = "des_o_leader";
    private const string FriendVariable = "des_i_friend";
    private const int SelfReference = 1;
    internal static PcMorphControllerBinding? Discover(PcSceneLoader.Record root,
        Func<uint, PcSceneLoader.Record?> find, Func<PcSceneLoader.Record, byte[]> origin,
        string ownerName, out string? cinematicDiagnostic)
    {
        cinematicDiagnostic = null;
        if (root.BrainFieldOffset < 0)
        {
            return null;
        }
        uint brainKey = BitConverter.ToUInt32(origin(root), root.BrainFieldOffset);
        var brain = find(brainKey);
        if (brain is not { Kind: 'q', Size: >= 8 })
        {
            return null;
        }
        var brainBytes = origin(brain).AsSpan(brain.Offset, brain.Size);
        var model = find(BitConverter.ToUInt32(brainBytes));
        if (model is not { Kind: 's' })
        {
            return null;
        }
        var codeKeys = model.References.Select(reference => reference.Key).ToHashSet();
        var cinematicController = ReadCinematicMouthController(model, BitConverter.ToUInt32(brainBytes[4..]),
            codeKeys, find, origin, out cinematicDiagnostic);
        if (cinematicController != null) return cinematicController;
        return PcMorphControllerSequences.Identify(codeKeys) switch
        {
            PcMorphControllerFamily.FactorAware => PcMorphControllerSequences.FactorAware,
            PcMorphControllerFamily.SharedHead => ReadSharedHead(model, BitConverter.ToUInt32(brainBytes[4..]), find, origin),
            _ => null
        };
    }

    private static PcMorphControllerBinding? ReadCinematicMouthController(PcSceneLoader.Record model,
        uint instanceKey, IReadOnlySet<uint> codeKeys, Func<uint, PcSceneLoader.Record?> find,
        Func<PcSceneLoader.Record, byte[]> origin, out string? diagnostic)
    {
        diagnostic = null;
        bool hasCinematicMouthCode = codeKeys.Contains(PcMorphControllerSequences.CinematicMouthInitializerKey)
            || codeKeys.Contains(PcMorphControllerSequences.CinematicMouthUpdateKey)
            || codeKeys.Contains(PcMorphControllerSequences.CinematicMouthMessageKey);
        if (!hasCinematicMouthCode) return null;
        if (!codeKeys.Contains(PcMorphControllerSequences.CinematicMouthUpdateKey))
        {
            diagnostic = "Lip-sync controller references were incomplete; the qualified morph-update function is missing.";
            return null;
        }
        var defaultsKey = model.References.Where(reference => reference.Kind == 't')
            .Select(reference => reference.Key).Distinct().ToArray();
        if (defaultsKey.Length != 1 || find(defaultsKey[0]) is not { Kind: 't' } defaults
            || find(instanceKey) is not { Kind: 't' } instance)
        {
            diagnostic = "Lip-sync controller has no unambiguous defaults/instance OVA.";
            return null;
        }

        try
        {
            var modelBytes = origin(defaults).AsSpan(defaults.Offset, defaults.Size);
            var instanceBytes = origin(instance).AsSpan(instance.Offset, instance.Size);
            int actorType = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "des_i_type_acteur", PcVariableFormat.IntegerType);
            if (actorType != 2)
            {
                diagnostic = $"Lip-sync controller actor type {actorType} is not yet supported.";
                return null;
            }

            int module = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "i_shape_canal_tete", PcVariableFormat.IntegerType);
            int automatic = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "i_expr_bouche_auto", PcVariableFormat.IntegerType);
            int sequenceIndex = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "i_expr_bouche_blabla_id", PcVariableFormat.IntegerType);
            float factor = PcFloatArrayVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "f_expr_factor_target", 3);
            float speed = PcFloatArrayVariableReader.ReadOverlay(modelBytes, instanceBytes,
                "f_expr_speed", 3);
            if (module is < 0 or > byte.MaxValue || sequenceIndex is < 0 or >= 32)
            {
                diagnostic = "Lip-sync controller module or sequence index is outside the supported range.";
                return null;
            }

            bool validAutoSettings = float.IsFinite(factor) && factor >= 0 && factor <= 1
                && float.IsFinite(speed) && speed > 0 && speed <= FaceMorphMouthCycleSettings.MaximumSpeed;
            bool autoStart = automatic != 0 && validAutoSettings;
            float defaultFactor = float.IsFinite(factor) && factor is >= 0 and <= 1
                ? factor : FaceMorphMouthCycleSettings.DefaultFactor;
            float defaultSpeed = float.IsFinite(speed) && speed > 0 && speed <= FaceMorphMouthCycleSettings.MaximumSpeed
                ? speed : FaceMorphMouthCycleSettings.DefaultSpeed;
            diagnostic = $"Lip-sync type 2 bound to module {module}, channel 3; auto={automatic != 0}, factor={factor}, speed={speed}, sequence index={sequenceIndex}.";
            return new(PcMorphControllerSequences.CinematicMouthInitializerKey,
                PcMorphControllerSequences.CinematicMouthUpdateKey,
                PcMorphControllerSequences.CinematicType2,
                PcMorphTransitionCompletion.ProgressionThreshold,
                (byte)module, 3, autoStart, defaultFactor, defaultSpeed, sequenceIndex, MatchMorphData: true);
        }
        catch (InvalidDataException error)
        {
            diagnostic = $"Lip-sync controller defaults could not be read: {error.Message}";
            return null;
        }
    }

    private static PcMorphControllerBinding? ReadSharedHead(PcSceneLoader.Record model, uint instanceKey,
        Func<uint, PcSceneLoader.Record?> find, Func<PcSceneLoader.Record, byte[]> origin)
    {
        var defaultKeys = model.References.Where(reference => reference.Kind == 't').Select(reference => reference.Key).Distinct().ToArray();
        if (defaultKeys.Length != 1) return null;
        var defaults = find(defaultKeys[0]);
        var instance = find(instanceKey);
        if (defaults is not { Kind: 't' } || instance is not { Kind: 't' }) return null;
        try
        {
            var modelBytes = origin(defaults).AsSpan(defaults.Offset, defaults.Size);
            var instanceBytes = origin(instance).AsSpan(instance.Offset, instance.Size);
            int leader = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes, LeaderVariable, PcVariableFormat.ObjectReferenceType);
            if (!HasKnownLeader(leader, find)) return null;
            int friend = PcScalarVariableReader.ReadOverlay(modelBytes, instanceBytes, FriendVariable, PcVariableFormat.IntegerType);
            return PcMorphControllerSequences.SharedHead(friend == 1);
        }
        catch (InvalidDataException) { return null; }
    }

    private static bool HasKnownLeader(int reference, Func<uint, PcSceneLoader.Record?> find)
    {
        // Qualify the initializer's leader gate only for self or a loaded GAO.
        if (reference == SelfReference) return true;
        if (reference is 0 or -1) return false;
        return find(unchecked((uint)reference)) is { Kind: 'l' };
    }
}
