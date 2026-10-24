namespace JadeView;

internal static class GameScriptWorldListArchiveReader
{
    private const int MaximumWorldListBytes = 4 * 1024 * 1024;

    internal static GameScriptWorldListResource Read(Archive archive, uint worldListKey)
    {
        Entry[] entries = archive.Entries.Where(entry => entry.Key == worldListKey).Take(2).ToArray();
        if (entries.Length == 0)
            return new(worldListKey, string.Empty, null, "MissingArchiveEntry",
                $"WOL key {worldListKey:X8} does not resolve to an archive entry.");
        if (entries.Length > 1)
            return new(worldListKey, string.Empty, null, "AmbiguousArchiveKey",
                $"WOL key {worldListKey:X8} matches multiple archive entries.");

        Entry entry = entries[0];
        try
        {
            byte[] payload = archive.Read(entry, MaximumWorldListBytes);
            return PcWorldListHeaderReader.TryRead(payload, worldListKey,
                    out GameWorldListDescriptor? descriptor, out _, out string error)
                ? new(worldListKey, entry.Name, descriptor, "Resolved", null)
                : new(worldListKey, entry.Name, null, "InvalidPcWorldList", error);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return new(worldListKey, entry.Name, null, "ReadFailure", exception.Message);
        }
    }
}
