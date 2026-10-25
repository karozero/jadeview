using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class PcAiGlobalSavedFlagSnapshotChecks
{
    internal static void Verify()
    {
        string archivePath = Path.Combine(Path.GetTempPath(), $"jadeview-save-schema-{Guid.NewGuid():N}.bf");
        string savePath = Path.Combine(Path.GetTempPath(), $"jadeview-save-slot-{Guid.NewGuid():N}.sav");
        try
        {
            ArchiveStartKeyChecks.WriteStartupChainFixture(archivePath, BuildUniverseVariables());
            var archive = new Archive(archivePath);
            byte[] save = new byte[12 + 812];
            BinaryPrimitives.WriteUInt32LittleEndian(save, 2);
            BinaryPrimitives.WriteUInt32LittleEndian(save.AsSpan(4), 0x80000001);
            BinaryPrimitives.WriteUInt32LittleEndian(save.AsSpan(8), 1);
            File.WriteAllBytes(savePath, save);

            PcAiSavedFlagSnapshot snapshot = PcAiGlobalSavedFlagSnapshotReader.Read(archive, savePath);
            Check(snapshot.Status == "Loaded" && snapshot.ExpectedAiBufferLength == 12
                    && snapshot.SlotFileLength == save.Length
                    && snapshot.Flags.Count == 64
                    && snapshot.Flags[0] && snapshot.Flags[31] && snapshot.Flags[32]
                    && !snapshot.Flags[1] && Path.GetFullPath(savePath) == snapshot.Path,
                "An explicitly selected slot should decode the saved AI prefix using the opened BF's ordered Universe schema.");

            File.WriteAllBytes(savePath, save[..11]);
            PcAiSavedFlagSnapshot truncated = PcAiGlobalSavedFlagSnapshotReader.Read(archive, savePath);
            Check(truncated.Status == "Unavailable"
                    && truncated.Diagnostic.Contains("requires at least 12 bytes", StringComparison.Ordinal),
                $"A short slot must be rejected (status={truncated.Status}, diagnostic='{truncated.Diagnostic}', "
                + $"flagCount={truncated.Flags.Count}).");

            PcAiSavedFlagSnapshot missingPath = PcAiGlobalSavedFlagSnapshotReader.Read(archive, " ");
            Check(missingPath.Status == "Unavailable" && missingPath.Flags.Count == 0,
                "An absent explicit slot path must leave all saved flags unknown.");

            Console.WriteLine("PASS: explicit retail slot reader follows the BF Universe schema and rejects truncated or missing inputs");
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (File.Exists(savePath)) File.Delete(savePath);
        }
    }

    private static byte[] BuildUniverseVariables()
    {
        const string variableName = "Flag_speciaux_sauve";
        byte[] payload = new byte[4 + 12 + 4 + 30];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 12);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (1u << 30) | 2u);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), 0x21);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), 0x10);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), 30);
        Encoding.ASCII.GetBytes(variableName).CopyTo(payload, 20);
        return payload;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
