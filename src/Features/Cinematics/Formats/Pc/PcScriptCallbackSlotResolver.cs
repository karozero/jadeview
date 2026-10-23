namespace JadeView;

/// <summary>
/// Resolves only explicitly referenced model OVA blocks in model world scope.
/// Iris ai_common.c:1374 writes each OVA into the same model-vars pointer; until loader
/// overwrite order is reproduced, conflicting candidates remain unknown rather than
/// treating OVA blocks as overlays or preferring the first nonempty callback table.
/// </summary>
internal static class PcScriptCallbackSlotResolver
{
    private const char VariableResourceKind = 't';

    internal static CinematicScriptCallbackSlot[]? Resolve(PcSceneLoader.Record model,
        PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        CinematicScriptCallbackSlot[]? agreedSlots = null;
        foreach (uint variableKey in model.References
                     .Where(reference => reference.Kind == VariableResourceKind)
                     .Select(reference => reference.Key).Distinct())
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
            if (agreedSlots != null && !agreedSlots.Select(slot => slot.FunctionKey)
                    .SequenceEqual(slots.Select(slot => slot.FunctionKey)))
                return null;
            agreedSlots ??= slots;
        }
        return agreedSlots;
    }
}
