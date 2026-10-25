namespace JadeView;

/// <summary>Reads saved Universe flags from an explicitly selected Iris PC slot file.</summary>
internal static class PcAiGlobalSavedFlagSnapshotReader
{
    private const int MaximumUniverseOvaBytes = 16 * 1024 * 1024;
    private const ushort SaveVariableFlag = 0x10;

    internal static PcAiSavedFlagSnapshot Read(Archive archive, string savePath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (string.IsNullOrWhiteSpace(savePath))
            return Failure("No save-slot path was selected.");

        ArchiveStartupScriptInventory startup = ArchiveStartupScriptReader.Read(archive);
        if (startup.Status != "Resolved")
            return Failure($"The opened BF's global AI startup chain is {startup.Status}.");

        var candidates = new List<(PcAiGlobalVariableDescriptor[] Descriptors,
            PcAiGlobalSaveLayout Layout)>();
        foreach (ArchiveScriptResourceReference resource in startup.ModelResources
                     .Where(resource => resource.TypeTag.Equals(".ova", StringComparison.OrdinalIgnoreCase)
                         && resource.Status == "Resolved"))
        {
            Entry[] entries = archive.Entries.Where(entry => entry.Key == resource.Key).Take(2).ToArray();
            if (entries.Length != 1) continue;

            byte[] payload;
            try
            {
                payload = archive.Read(entries[0], MaximumUniverseOvaBytes);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                continue;
            }

            if (!PcAiGlobalVariableCatalogReader.TryRead(payload,
                    out PcAiGlobalVariableDescriptor[] descriptors, out _))
                continue;
            PcAiGlobalVariableDescriptor[] savedFlagDescriptors = descriptors
                .Where(descriptor => descriptor.Name.Equals("Flag_speciaux_sauve",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (savedFlagDescriptors.Length != 1
                || (savedFlagDescriptors[0].Flags & SaveVariableFlag) == 0)
                continue;

            candidates.Add((descriptors, PcAiGlobalSaveLayoutReader.Read(descriptors)));
        }

        if (candidates.Count != 1)
            return Failure(candidates.Count == 0
                ? "The global startup model has no uniquely saved Flag_speciaux_sauve OVA field."
                : "The global startup model contains ambiguous saved flag bitsets.");

        (PcAiGlobalVariableDescriptor[] universeVariables, PcAiGlobalSaveLayout saveLayout) = candidates[0];
        if (saveLayout.Status != "Resolved" || saveLayout.BufferLength is not { } bufferLength)
            return Failure(saveLayout.Diagnostic ?? "The Universe save-buffer layout is unresolved.");

        PcAiGlobalVariableDescriptor flagDescriptor = universeVariables.Single(descriptor =>
            descriptor.Name.Equals("Flag_speciaux_sauve", StringComparison.OrdinalIgnoreCase));
        int flagCapacity = checked((int)((flagDescriptor.PackedArrayCount & 0x3FFFFFFF) * 32));
        if (flagDescriptor.Type is not (0x21 or 0x22) || flagCapacity <= 0)
            return Failure("Flag_speciaux_sauve is not a bounded integer bitset.");

        try
        {
            using var stream = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < bufferLength)
                return Failure($"Selected slot is {stream.Length} bytes; the opened BF requires at least {bufferLength} bytes for its AI save block.");

            byte[] saveBuffer = new byte[bufferLength];
            stream.ReadExactly(saveBuffer);
            PcAiSavedFieldLayout flagField = saveLayout.Fields.Single(field =>
                field.Name.Equals("Flag_speciaux_sauve", StringComparison.OrdinalIgnoreCase));
            var flags = new Dictionary<int, bool>(flagCapacity);
            for (int flagIndex = 0; flagIndex < flagCapacity; flagIndex++)
            {
                bool? value = PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(
                    saveLayout, saveBuffer, flagField.Name, flagIndex);
                if (value is not { } knownValue)
                    return Failure($"Could not decode saved flag {flagIndex} from the selected slot.");
                flags.Add(flagIndex, knownValue);
            }

            return new("Loaded", Path.GetFullPath(savePath), bufferLength,
                stream.Length, flags,
                "Read only the initial Iris AI save block at slot-file offset 0. Iris slots contain photo data after it; the save format has no verified BF-version fingerprint, so select a slot from a matching game/archive version.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Failure($"Could not read selected save slot: {exception.Message}", savePath);
        }

        PcAiSavedFlagSnapshot Failure(string diagnostic, string? selectedPath = null) =>
            new("Unavailable", selectedPath, null, null,
                new Dictionary<int, bool>(), diagnostic);
    }
}

internal sealed record PcAiSavedFlagSnapshot(string Status, string? Path,
    int? ExpectedAiBufferLength, long? SlotFileLength,
    IReadOnlyDictionary<int, bool> Flags, string Diagnostic);
