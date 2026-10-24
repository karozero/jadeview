using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JadeView;

internal static class ScriptOwnerResourceFixture
{
    internal static void Write(string archivePath, string container, uint ownerKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] bytes = archive.Read(entry);
        var replay = PcSceneLoader.Read(bytes, archivePath, entry.Key);
        if (!replay.Complete) throw new InvalidDataException(replay.Error);
        var origins = replay.Records.ToDictionary(record => record, _ => bytes);
        var fixture = Build(replay.Records, origins, ownerKey);
        string directory = Path.Combine(output, $"{entry.Id}-{ownerKey:X8}-script-owner-fixture");
        Directory.CreateDirectory(directory);
        foreach (var resource in fixture.Resources.Where(resource => resource.Payload != null))
            File.WriteAllBytes(Path.Combine(directory, resource.File!), resource.Payload!);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Container = entry.Id, ContainerKey = entry.Key.ToString("X8"),
            ContainerSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Scope = "Bounded resource evidence from this decoded container only; no native loading, callback execution, or world-state mutation.",
            Fixture = fixture
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCRIPT OWNER FIXTURE: {directory}; resources={fixture.Resources.Count}; "
            + $"unresolved={fixture.Resources.Count(resource => resource.Status != "Resolved")}");
    }

    internal static Fixture Build(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, uint ownerKey)
    {
        var index = new PcSceneResourceIndex(records);
        var resources = new List<Resource>();
        var links = new List<Link>();
        var diagnostics = new List<string>();
        PcSceneLoader.Record? owner = Add(ownerKey, 0, 'l', "owner", null);
        if (owner == null) return Finish(null);
        if (!TryPayload(owner, out var ownerBytes)
            || owner.BrainFieldOffset < owner.Offset
            || owner.BrainFieldOffset - owner.Offset > ownerBytes.Length - sizeof(uint))
        {
            diagnostics.Add("GAO brain-key field is outside the bounded owner payload.");
            return Finish(null);
        }
        uint brainKey = BinaryPrimitives.ReadUInt32LittleEndian(ownerBytes.Span[(owner.BrainFieldOffset - owner.Offset)..]);
        var brain = Add(brainKey, owner.LoadWorldKey, 'q', "instance", null);
        if (brain == null || !TryPayload(brain, out var brainBytes)) return Finish(null);
        if (brainBytes.Length < sizeof(uint) * 2)
        {
            diagnostics.Add("AIC payload has no complete model/instance-variable key pair.");
            return Finish(null);
        }
        uint modelKey = BinaryPrimitives.ReadUInt32LittleEndian(brainBytes.Span);
        uint instanceKey = BinaryPrimitives.ReadUInt32LittleEndian(brainBytes.Span[sizeof(uint)..]);
        Add(instanceKey, owner.LoadWorldKey, 't', "instance-variables", null);
        var model = Add(modelKey, owner.LoadWorldKey, 's', "model", null);
        if (model == null || !TryPayload(model, out var modelBytes)) return Finish(null);
        if (modelBytes.Length % 8 != 0)
        {
            diagnostics.Add("AIM payload is not a complete sequence of eight-byte key/tag entries.");
            return Finish(null);
        }
        for (int offset = 0; offset < modelBytes.Length; offset += 8)
        {
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(modelBytes.Span[offset..]);
            string tag = System.Text.Encoding.ASCII.GetString(modelBytes.Span.Slice(offset + 4, 4));
            char kind = tag switch { ".ova" => 't', ".ofc" => 'r', _ => '?' };
            Add(key, model.LoadWorldKey, kind, $"model-entry:{tag}", offset / 8);
        }
        var callbacks = PcScriptCallbackSlotResolver.Resolve(model, index, origins,
            new Dictionary<uint, string>());
        if (callbacks == null) diagnostics.Add("Effective callback slots are unresolved.");
        return Finish(callbacks?.Select(slot => slot.FunctionKey.ToString("X8")).ToArray());

        Fixture Finish(string[]? callbacks) => new(ownerKey.ToString("X8"), resources, links, callbacks, diagnostics);

        PcSceneLoader.Record? Add(uint key, uint world, char kind, string role, int? order)
        {
            PcSceneResourceResolution resolved = index.ResolveDetailed(key, world);
            var record = resolved.Record;
            string status = key is 0 or uint.MaxValue ? "NullReference"
                : kind == '?' ? "UnsupportedTag"
                : record == null ? "Unresolved"
                : record.Kind != kind ? "WrongKind"
                : !TryPayload(record, out _) ? "InvalidPayloadRange" : "Resolved";
            string? file = status == "Resolved"
                ? $"{record!.LoadWorldKey:X8}-{record.Key:X8}-{record.Kind}-{record.Offset:X8}.bin" : null;
            byte[]? payload = status == "Resolved" && TryPayload(record!, out var memory) ? memory.ToArray() : null;
            resources.Add(new(key.ToString("X8"), world.ToString("X8"), record?.LoadWorldKey.ToString("X8"),
                kind.ToString(), record?.Kind.ToString(), record?.Name, record?.Offset, record?.Size,
                status, resolved.Failure, file, payload == null ? null : Convert.ToHexString(SHA256.HashData(payload)))
                {
                    Payload = payload,
                    OvaSummary = record?.Kind == 't' && payload != null ? ScriptOvaFixtureSummary.Read(payload) : null
                });
            links.Add(new(role, order, key.ToString("X8"), world.ToString("X8"), status, file));
            return status == "Resolved" ? record : null;
        }

        bool TryPayload(PcSceneLoader.Record record, out ReadOnlyMemory<byte> payload)
        {
            payload = default;
            if (!origins.TryGetValue(record, out var source) || record.Offset < 0 || record.Size < 0
                || record.Offset > source.Length - record.Size) return false;
            payload = source.AsMemory(record.Offset, record.Size);
            return true;
        }
    }

    internal sealed record Fixture(string OwnerKey, List<Resource> Resources, List<Link> Links,
        string[]? ExpectedCallbackKeys, List<string> Diagnostics);
    internal sealed record Link(string Role, int? EntryIndex, string Key, string PreferredWorld,
        string Status, string? File);
    internal sealed record Resource(string Key, string PreferredWorld, string? LoadWorld, string ExpectedKind,
        string? Kind, string? Name, int? Offset, int? Size, string Status, string? ResolutionDiagnostic,
        string? File, string? Sha256)
    {
        [JsonIgnore] public byte[]? Payload { get; init; }
        public ScriptOvaFixtureSummary.Summary? OvaSummary { get; init; }
    }
}
