namespace JadeView;

/// <summary>
/// Resolves only explicitly referenced model OVA blocks in model world scope.
/// Iris ai_common.c:1374 writes each OVA into the same model-vars pointer. FIFO reference
/// resolution (loading.c:257-277,336-375) makes the final ordered assignment effective,
/// including repeated references and zero callback tables. Incomplete evidence stays unknown.
/// </summary>
internal static class PcScriptCallbackSlotResolver
{
    private const char VariableResourceKind = 't';

    internal static CinematicScriptCallbackSlot[]? Resolve(PcSceneLoader.Record model,
        PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        CinematicScriptCallbackSlot[]? effectiveSlots = null;
        foreach (uint variableKey in model.References
                     .Where(reference => reference.Kind == VariableResourceKind)
                     .Select(reference => reference.Key))
        {
            PcSceneLoader.Record? record = resources.Resolve(variableKey, model.LoadWorldKey);
            if (record?.Kind != VariableResourceKind
                || !origins.TryGetValue(record, out byte[]? bytes)
                || record.Offset < 0 || record.Size < 0
                || record.Offset > bytes.Length - record.Size)
                return null;

            CinematicScriptCallbackSlot[]? slots = PcScriptCallbackSlotReader.Read(
                bytes.AsSpan(record.Offset, record.Size), functionNames);
            if (slots == null) return null;
            effectiveSlots = slots;
        }
        return effectiveSlots;
    }
}
