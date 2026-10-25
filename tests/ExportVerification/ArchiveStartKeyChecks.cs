using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class ArchiveStartKeyChecks
{
    internal static void Verify()
    {
        string archivePath = Path.Combine(Path.GetTempPath(), $"jadeview-start-key-{Guid.NewGuid():N}.bf");
        try
        {
            WriteFixture(archivePath, 0x71003FF9);
            var archive = new Archive(archivePath);

            Require(archive.StartKey == 0x71003FF9,
                "BIG startup key should be read from the serialized header field.");
            Require(archive.StartEntry?.Name == "univers.oin",
                "BIG startup entry should resolve after the FAT and names tables have loaded.");

            WriteFixture(archivePath, 0x71004000);
            archive = new Archive(archivePath);
            Require(archive.StartKey == 0x71004000 && archive.StartEntry == null,
                "A missing startup key should remain visible without inventing an archive entry.");

            WriteStartupChainFixture(archivePath);
            ArchiveStartupScriptInventory inventory = ArchiveStartupScriptReader.Read(new Archive(archivePath));
            Require(inventory.Status == "Resolved"
                && inventory.InstanceName == "univers.oin"
                && inventory.ModelName == "univers.omd"
                && inventory.InstanceVariablesName == "univers.ova"
                && inventory.ModelResources.Any(resource => resource.TypeTag == ".ofc"
                    && resource.Name == "univ_init.ofc" && resource.Status == "Resolved"),
                "Global AI inventory should follow BIG startKey to instance, model, variables and typed function links.");
            Console.WriteLine("PASS: BIG startKey field and nullable startup-entry resolution");
            Console.WriteLine("PASS: archive global-AI startup resource chain resolves without executing scripts");
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    private static void WriteFixture(string path, uint startKey)
    {
        WriteArchive(path, startKey,
        [
            new(0x71003FF9, "univers.oin", []),
        ]);
    }

    internal static void WriteStartupChainFixture(string path) => WriteStartupChainFixture(path, []);

    internal static void WriteStartupChainFixture(string path, byte[] modelVariablesPayload)
    {
        byte[] instancePayload = new byte[sizeof(uint) * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(instancePayload, 0x71003FF3);
        BinaryPrimitives.WriteUInt32LittleEndian(instancePayload.AsSpan(sizeof(uint)), 0x71003FF8);

        using var modelStream = new MemoryStream();
        using (var modelWriter = new BinaryWriter(modelStream, Encoding.ASCII, leaveOpen: true))
        {
            WriteModelReference(modelWriter, 0x71003FF2, ".ova");
            WriteModelReference(modelWriter, 0x71003FF1, ".ofc");
        }

        WriteArchive(path, 0x71003FF9,
        [
            new(0x71003FF9, "univers.oin", instancePayload),
            new(0x71003FF3, "univers.omd", modelStream.ToArray()),
            new(0x71003FF8, "univers.ova", []),
            new(0x71003FF2, "univars.ova", modelVariablesPayload),
            new(0x71003FF1, "univ_init.ofc", []),
        ]);
    }

    private static void WriteModelReference(BinaryWriter writer, uint key, string typeTag)
    {
        writer.Write(key);
        WriteFixedString(writer, typeTag, 4);
    }

    internal static void WriteArchive(string path, uint startKey, (uint Key, string Name, byte[] Payload)[] entries)
    {
        int capacity = entries.Length;
        const int fatOffset = 68;
        int namesOffset = fatOffset + capacity * 8;
        int directoriesOffset = namesOffset + capacity * 84;
        int resourceOffset = directoriesOffset + capacity * 84;
        int[] entryOffsets = new int[capacity];
        int nextOffset = resourceOffset;
        for (int index = 0; index < entries.Length; index++)
        {
            entryOffsets[index] = nextOffset;
            nextOffset += sizeof(int) + entries[index].Payload.Length;
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x00474942u);
        writer.Write(0x22);
        writer.Write(entries.Length);
        stream.Position = 32;
        writer.Write(capacity);
        writer.Write(1);
        stream.Position = 40;
        writer.Write(startKey);
        stream.Position = 44;
        writer.Write(entries.Length);
        stream.Position = 52;
        writer.Write(fatOffset);

        stream.Position = fatOffset;
        for (int index = 0; index < entries.Length; index++)
        {
            writer.Write(entryOffsets[index]);
            writer.Write(entries[index].Key);
        }

        for (int index = 0; index < entries.Length; index++)
        {
            stream.Position = namesOffset + index * 84L + 12;
            writer.Write(0);
            stream.Position += 4;
            WriteFixedString(writer, entries[index].Name, 64);

            stream.Position = entryOffsets[index];
            writer.Write(entries[index].Payload.Length);
            writer.Write(entries[index].Payload);
        }

        stream.Position = directoriesOffset + 16;
        writer.Write(-1);
        WriteFixedString(writer, "ROOT", 64);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteCount)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        if (encoded.Length > byteCount) throw new ArgumentOutOfRangeException(nameof(value));
        writer.Write(encoded);
        writer.Write(new byte[byteCount - encoded.Length]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
