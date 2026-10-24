namespace JadeView;

/// <summary>
/// Resolves only explicitly referenced model OVA blocks in model world scope.
/// Iris ai_common.c:1374 writes each OVA into the same model-vars pointer. FIFO reference
/// resolution (loading.c:257-277,336-375) makes the final ordered assignment effective,
/// including repeated references and zero callback tables. Incomplete evidence stays unknown.
/// </summary>
internal static class PcScriptCallbackSlotResolver
{
    internal static CinematicScriptCallbackSlot[]? Resolve(PcSceneLoader.Record model,
        PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        ReadOnlyMemory<byte>? payload = PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins);
        return payload is { } bytes ? PcScriptCallbackSlotReader.Read(bytes.Span, functionNames) : null;
    }
}
