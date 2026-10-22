using JadeView;

internal static class CinematicActionLibraryChecks
{
    internal static void Verify()
    {
        byte[] identicalPayloads = new byte[14];
        WriteUInt32(identicalPayloads, 0, 0x300);
        identicalPayloads[4] = 0;
        identicalPayloads[5] = 0;
        identicalPayloads[8] = 0;
        identicalPayloads[9] = 0;
        identicalPayloads[12] = 0;
        identicalPayloads[13] = 0;
        var actionOwner = new PcSceneLoader.Record(0x100, 'l', 0, 0)
        {
            LoadWorldKey = 0x10,
            Skeleton = 0x200
        };
        var actionTable = new PcSceneLoader.Record(0x200, 'b', 0, 4)
        {
            LoadWorldKey = 0x10
        };
        var localAction = new PcSceneLoader.Record(0x300, 'a', 4, 2)
        {
            LoadWorldKey = 0x20
        };
        var fixedAction = new PcSceneLoader.Record(0x300, 'a', 8, 2)
        {
            LoadWorldKey = 0x30
        };
        var resourceOrigins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [actionOwner] = identicalPayloads,
            [actionTable] = identicalPayloads,
            [localAction] = identicalPayloads,
            [fixedAction] = identicalPayloads
        };
        var document = new SceneDocument();
        CinematicActionLibraryReader.Load([actionOwner, actionTable, localAction, fixedAction],
            resourceOrigins, document);

        Check(document.CinematicActionKitsByOwner.TryGetValue((0x10u, 0x100u), out var actionKit)
                && actionKit.ContainsKey(0),
            "Action kits should resolve byte-identical action resources reused across local and FIX worlds");

        byte[] conflictingPayloads = identicalPayloads.ToArray();
        conflictingPayloads[8] = 1;
        var conflictingOrigins = resourceOrigins.ToDictionary(pair => pair.Key,
            pair => pair.Key == fixedAction ? conflictingPayloads : identicalPayloads);
        var unresolvedDocument = new SceneDocument();
        CinematicActionLibraryReader.Load([actionOwner, actionTable, localAction, fixedAction],
            conflictingOrigins, unresolvedDocument);

        Check(unresolvedDocument.CinematicActionKitsByOwner.TryGetValue((0x10u, 0x100u),
                  out var unresolvedKit)
                && unresolvedKit.Count == 0,
            "Action kits should keep genuinely conflicting duplicate action resources unresolved");
        Console.WriteLine("PASS: action-kit resource links resolve identical cross-world payloads and preserve conflicting duplicates");
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, sizeof(uint)), value);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
