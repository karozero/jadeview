using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static partial class ActorScriptAudit
{
    internal static void WriteContainerEvents(string archivePath, string container, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var records = ReadRecords(archive.Read(entry), archive.PathName, entry.Id);
        var fixedEntry = archive.Entries.FirstOrDefault(entry => entry.Id == "FF0084DF");
        if (fixedEntry != null && fixedEntry.Id != entry.Id)
            records.InsertRange(0, ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id));
        string folder = Path.Combine(output, entry.Id + "-container-events");
        Directory.CreateDirectory(folder);
        var manifest = new List<object>();
        int failures = 0;
        foreach (var record in records.Where(record => record.Kind == 'i'))
        {
            string owner = $"{record.Container}-{record.Key:X8}-{record.Offset:X8}";
            var clip = new AnimationClip { Key = record.Key, Name = owner, Loaded = true };
            try { clip = AnimationReader.Read(record.Bytes, 0, record.Bytes.Length); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException)
            {
                clip.Error = error.Message;
                failures++;
            }
            clip.Key = record.Key;
            clip.Name = owner;
            AnimationEventAudit.WriteClips(owner, new[] { clip }, folder);
            manifest.Add(new { record.Container, Key = record.Key.ToString("X8"), record.Offset,
                World = record.World.ToString("X8"), record.Bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(record.Bytes)), clip.Error,
                File = owner + "-animation-events.json" });
        }
        File.WriteAllText(Path.Combine(folder, "index.json"), JsonSerializer.Serialize(new
        {
            Archive = archive.PathName, Container = entry.Id, Failures = failures, Records = manifest,
            Limits = "All parsed i records in one container plus fixed resources; no actor-clip membership or event execution inferred."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CONTAINER EVENT AUDIT: {manifest.Count} records, {failures} failures");
    }
}
