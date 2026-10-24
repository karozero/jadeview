namespace JadeView;

/// <summary>
/// Resolves bounded OVA storage without combining separate model resources.
/// Model references replace one destination in source queue order; instance storage
/// has its own destination and owner-world scope (Iris ai_common.c:1366-1408).
/// </summary>
internal static class PcScriptModelVariableResourceResolver
{
    internal static ReadOnlyMemory<byte>? ResolveModel(PcSceneLoader.Record model,
        PcSceneResourceIndex resources, IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        ReadOnlyMemory<byte>? effective = null;
        foreach (var reference in model.References.Where(reference => reference.Kind == 't'))
        {
            ReadOnlyMemory<byte>? payload = ResolveInstance(reference.Key, model.LoadWorldKey, resources, origins);
            if (payload is not { } bytes || !PcScriptCallbackSlotReader.HasCompleteCallbackTable(bytes.Span))
                return null;
            effective = payload;
        }
        return effective;
    }

    internal static ReadOnlyMemory<byte>? ResolveInstance(uint key, uint ownerWorldKey,
        PcSceneResourceIndex resources, IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        if (key is 0 or uint.MaxValue) return null;
        PcSceneLoader.Record? record = resources.Resolve(key, ownerWorldKey);
        if (record?.Kind != 't' || !origins.TryGetValue(record, out byte[]? bytes)
            || record.Offset < 0 || record.Size < 0 || record.Offset > bytes.Length - record.Size)
            return null;
        ReadOnlyMemory<byte> payload = bytes.AsMemory(record.Offset, record.Size);
        try
        {
            PcVariableStorageReader.ReadLayout(payload.Span);
            return payload;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
