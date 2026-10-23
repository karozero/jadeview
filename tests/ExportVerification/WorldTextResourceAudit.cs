using System.Buffers.Binary;
using System.Text.Json;
using JadeView;

internal static class WorldTextResourceAudit
{
    private static readonly int[] SourceFieldOffsets = [160, 164, 168, 172, 204, 208, 212, 216, 220];

    internal static void Write(string archivePath, string container, string outputPath)
    {
        var archive = new Archive(archivePath);
        Entry entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] bytes = archive.Read(entry);
        PcSceneLoader.Result parsed = PcSceneLoader.Read(bytes, archivePath);
        if (!parsed.Complete)
            throw new InvalidDataException($"Could not parse {container}: {parsed.Error}");

        var worlds = parsed.Worlds.Select(world => new
        {
            Key = world.Key.ToString("X8"),
            world.Name,
            world.Offset,
            world.Size,
            Fields = SourceFieldOffsets
                .Where(fieldOffset => fieldOffset <= world.Size - sizeof(uint))
                .Select(fieldOffset => new
                {
                    Offset = fieldOffset,
                    Value = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(world.Offset + fieldOffset, sizeof(uint))).ToString("X8"),
                    CurrentLoaderReference = world.References.Any(reference =>
                        reference.Key == BinaryPrimitives.ReadUInt32LittleEndian(
                            bytes.AsSpan(world.Offset + fieldOffset, sizeof(uint)))),
                    ArchiveEntry = archive.Entries.FirstOrDefault(candidate =>
                        candidate.Key == BinaryPrimitives.ReadUInt32LittleEndian(
                            bytes.AsSpan(world.Offset + fieldOffset, sizeof(uint))))?.Name
                }).ToArray(),
            RawHeader = Convert.ToHexString(bytes.AsSpan(world.Offset,
                Math.Min(world.Size, 240)))
        }).ToArray();

        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            Container = entry.Id,
            parsed.Complete,
            Worlds = worlds,
            SourceInterpretation = new Dictionary<int, string>
            {
                [204] = "Jaded PC path: first world grid key after the 44-byte PC placeholder block",
                [208] = "Jaded PC path: second world grid key",
                [212] = "Jaded PC path: world game-object group key",
                [216] = "Jaded PC path: networks group key",
                [220] = "Jaded PC path: WorldText/TXG group key"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
