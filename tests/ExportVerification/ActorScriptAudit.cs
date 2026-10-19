using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static class ActorScriptAudit
{
    private sealed record SourceRecord(string Container, uint Key, char Kind, int Offset, byte[] Bytes,
        uint World, string Name, uint[] References, uint? Brain);

    internal static void Write(Archive archive, byte[] sceneBytes, SceneActor actor, string output, string container = "FF0057FD")
    {
        // Research-only access keeps the internal replay model out of the public API.
        var records = ReadRecords(sceneBytes, archive.PathName, container);
        var fixedEntry = archive.Entries.FirstOrDefault(entry => entry.Id == "FF0084DF");
        if (fixedEntry != null)
            records.InsertRange(0, ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id));
        var root = records.LastOrDefault(record => record.Key == actor.Root.GaoKey);
        if (root == null || root.Brain == null)
        {
            Console.WriteLine($"SCRIPT AUDIT: {actor.Root.Name}: no parsed brain reference");
            return;
        }

        var resources = records.Where(record => record.Container == "FF0084DF" || record.World == 0 || record.World == root.World)
            .GroupBy(record => record.Key).ToDictionary(group => group.Key, group => group.Last());
        var pending = new Queue<uint>();
        pending.Enqueue(root.Brain.Value);
        var visited = new HashSet<uint>();
        var closure = new List<SourceRecord>();
        var missing = new List<string>();
        while (pending.TryDequeue(out uint key))
        {
            if (key is 0 or uint.MaxValue || !visited.Add(key)) continue;
            if (!resources.TryGetValue(key, out var record))
            {
                missing.Add(key.ToString("X8"));
                continue;
            }
            if (record.Kind is not ('q' or 's' or 't' or 'r')) continue;
            closure.Add(record);
            foreach (uint reference in record.References) pending.Enqueue(reference);
        }

        var report = new
        {
            Actor = actor.Root.Name, Archive = archive.PathName, RootKey = root.Key.ToString("X8"),
            BrainKey = root.Brain.Value.ToString("X8"), Missing = missing,
            Limits = "Parsed PC brain dependency closure only; script words are not executed or attributed to facial controllers.",
            Records = closure.Select(record => new
            {
                record.Container, Key = record.Key.ToString("X8"), Kind = record.Kind.ToString(), record.Offset,
                Sha256 = Convert.ToHexString(SHA256.HashData(record.Bytes)),
                World = record.World.ToString("X8"), record.Name,
                References = record.References.Select(key => key.ToString("X8")).ToArray(),
                Bytes = Convert.ToHexString(record.Bytes)
            }).ToArray()
        };
        string path = Path.Combine(output, SceneExporter.SafeFileName(actor.Root.Name) + "-scripts.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCRIPT AUDIT: {actor.Root.Name}: {closure.Count} brain/script resources, {missing.Count} unresolved references");
    }

    private static List<SourceRecord> ReadRecords(byte[] bytes, string archivePath, string container)
    {
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcSceneLoader")!;
        var read = loader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        object replay = read.Invoke(null, [bytes, archivePath])!;
        string? error = (string?)Field(replay, "Error");
        if (error != null) throw new InvalidDataException(error);
        var records = new List<SourceRecord>();
        foreach (object record in (IEnumerable)Field(replay, "Records")!)
        {
            int offset = (int)Field(record, "Offset")!;
            int size = (int)Field(record, "Size")!;
            int brainOffset = (int)Field(record, "BrainFieldOffset")!;
            var references = ((IEnumerable)Field(record, "References")!).Cast<object>()
                .Select(request => (uint)request.GetType().GetProperty("Key")!.GetValue(request)!).ToArray();
            records.Add(new(container, (uint)Field(record, "Key")!, (char)Field(record, "Kind")!, offset,
                bytes.AsSpan(offset, size).ToArray(), (uint)Field(record, "LoadWorldKey")!,
                (string)Field(record, "Name")!, references,
                brainOffset >= 0 ? BitConverter.ToUInt32(bytes, brainOffset) : null));
        }
        return records;
    }

    private static object? Field(object instance, string name) =>
        instance.GetType().GetField(name)!.GetValue(instance);
}
