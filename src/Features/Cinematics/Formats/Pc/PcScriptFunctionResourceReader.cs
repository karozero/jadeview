namespace JadeView;

/// <summary>
/// Resolves only .ofc references explicitly serialized by a script model,
/// using that model's world scope. It decodes known serialized layouts but
/// never treats the function resource as executable code.
/// </summary>
internal static class PcScriptFunctionResourceReader
{
    internal static CinematicScriptFunctionResource[] Read(PcSceneLoader.Record model,
        PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        return model.References
            .Where(reference => reference.Kind == 'r')
            .Select(reference => reference.Key)
            .Distinct()
            .OrderBy(key => key)
            .Select(ReadFunction)
            .ToArray();

        CinematicScriptFunctionResource ReadFunction(uint functionKey)
        {
            PcSceneResourceResolution resolution = resources.ResolveDetailed(functionKey, model.LoadWorldKey);
            PcSceneLoader.Record? functionRecord = resolution.Record;
            if (functionRecord == null)
            {
                return new(functionKey, model.LoadWorldKey, $"{functionKey:X8}", 0,
                    resolution.Failure == "resource was not found" ? "MissingResource" : "AmbiguousResource",
                    null, resolution.Failure);
            }

            if (functionRecord.Kind != 'r')
            {
                return new(functionKey, functionRecord.LoadWorldKey, functionRecord.Name, functionRecord.Size,
                    "WrongResourceKind", null,
                    $"Expected .ofc resource kind 'r', found '{functionRecord.Kind}'.");
            }

            if (!origins.TryGetValue(functionRecord, out byte[]? origin)
                || functionRecord.Offset < 0 || functionRecord.Size < 0
                || functionRecord.Offset > origin.Length - functionRecord.Size)
            {
                return new(functionKey, functionRecord.LoadWorldKey, functionRecord.Name,
                    functionRecord.Size, "UnavailablePayload", null,
                    "The resolved resource has no bounded source payload in the loaded archive scope.");
            }

            ReadOnlySpan<byte> payload = origin.AsSpan(functionRecord.Offset, functionRecord.Size);
            if (PcSerializedAiFunctionReader.TryRead(payload,
                    out PcSerializedAiFunction? serializedFunction, out _))
            {
                return new(functionKey, functionRecord.LoadWorldKey, functionRecord.Name,
                    functionRecord.Size, "SerializedBody", null, null)
                {
                    SerializedFunction = serializedFunction
                };
            }

            if (!PcCompiledScriptFunctionReader.TryRead(payload, out var metadata, out string error))
            {
                return new(functionKey, functionRecord.LoadWorldKey, functionRecord.Name,
                    functionRecord.Size, "UnsupportedLayout", null, error);
            }

            return new(functionKey, functionRecord.LoadWorldKey, functionRecord.Name,
                functionRecord.Size, "CompactMetadataCandidate", metadata, null);
        }
    }
}
