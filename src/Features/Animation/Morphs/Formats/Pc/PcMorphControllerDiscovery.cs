namespace JadeView;

internal static class PcMorphControllerDiscovery
{
    private const string LeaderVariable = "des_o_leader";
    private const string FriendVariable = "des_i_friend";
    private const int SelfReference = 1;
    internal static PcMorphControllerBinding? Discover(PcSceneLoader.Record root,
        Func<uint, PcSceneLoader.Record?> find, Func<PcSceneLoader.Record, byte[]> origin)
    {
        if (root.BrainFieldOffset < 0) return null;
        uint brainKey = BitConverter.ToUInt32(origin(root), root.BrainFieldOffset);
        var brain = find(brainKey);
        if (brain is not { Kind: 'q', Size: >= 8 }) return null;
        var brainBytes = origin(brain).AsSpan(brain.Offset, brain.Size);
        var model = find(BitConverter.ToUInt32(brainBytes));
        if (model is not { Kind: 's' }) return null;
        var codeKeys = model.References.Where(reference => reference.Kind == 'r').Select(reference => reference.Key).ToHashSet();
        return PcMorphControllerSequences.Identify(codeKeys) switch
        {
            PcMorphControllerFamily.FactorAware => PcMorphControllerSequences.FactorAware,
            PcMorphControllerFamily.SharedHead => ReadSharedHead(model, BitConverter.ToUInt32(brainBytes[4..]), find, origin),
            _ => null
        };
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
