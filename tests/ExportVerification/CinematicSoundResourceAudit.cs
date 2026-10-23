using JadeView;

internal static class CinematicSoundResourceAudit
{
    internal static void Write(string archivePath, uint key)
    {
        var archive = new Archive(archivePath);
        Entry[] matches = archive.Entries.Where(entry => entry.Key == key).ToArray();
        if (matches.Length == 0)
        {
            Console.WriteLine($"SOUND RESOURCE: {key:X8} missing");
            return;
        }

        foreach (Entry entry in matches)
        {
            byte[] bytes = archive.Read(entry);
            string header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64)));
            string asciiHeader = new(bytes.Take(Math.Min(bytes.Length, 16))
                .Select(value => value is >= 32 and <= 126 ? (char)value : '.').ToArray());
            Console.WriteLine($"SOUND RESOURCE: {entry.Key:X8} {entry.Folder}/{entry.Name}; "
                + $"size={bytes.Length}; ascii={asciiHeader}; header={header}");
        }
    }
}
