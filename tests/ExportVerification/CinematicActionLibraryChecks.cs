using JadeView;

internal static class CinematicActionLibraryChecks
{
    internal static void Verify()
    {
        byte[] identicalPayloads = new byte[22];
        WriteUInt32(identicalPayloads, 0, 0x300);
        WriteUInt32(identicalPayloads, 4, 0xdeadbeef); // ignored second field
        WriteUInt32(identicalPayloads, 8, 0x301);
        WriteUInt32(identicalPayloads, 12, 0xdeadbeef); // ignored second field
        var actionOwner = new PcSceneLoader.Record(0x100, 'l', 0, 0)
        {
            LoadWorldKey = 0x10,
            Skeleton = 0x200
        };
        var actionTable = new PcSceneLoader.Record(0x200, 'b', 0, 16)
        {
            LoadWorldKey = 0x10
        };
        var localAction = new PcSceneLoader.Record(0x300, 'a', 16, 2)
        {
            LoadWorldKey = 0x20
        };
        var fixedAction = new PcSceneLoader.Record(0x300, 'a', 18, 2)
        {
            LoadWorldKey = 0x30
        };
        var nextAction = new PcSceneLoader.Record(0x301, 'a', 20, 2)
        {
            LoadWorldKey = 0x10
        };
        var resourceOrigins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [actionOwner] = identicalPayloads,
            [actionTable] = identicalPayloads,
            [localAction] = identicalPayloads,
            [fixedAction] = identicalPayloads,
            [nextAction] = identicalPayloads
        };
        var document = new SceneDocument();
        CinematicActionLibraryReader.Load([actionOwner, actionTable, localAction, fixedAction, nextAction],
            resourceOrigins, document);

        Check(document.CinematicActionKitsByOwner.TryGetValue((0x10u, 0x100u), out var actionKit)
                && actionKit.ContainsKey(0) && actionKit.ContainsKey(1)
                && !actionKit.ContainsKey(2) && !actionKit.ContainsKey(3),
            "8-byte action-kit entries should resolve resources and ignore their second fields");

        byte[] conflictingPayloads = identicalPayloads.ToArray();
        conflictingPayloads[19] = 1;
        var conflictingOrigins = resourceOrigins.ToDictionary(pair => pair.Key,
            pair => pair.Key == fixedAction ? conflictingPayloads : identicalPayloads);
        var unresolvedDocument = new SceneDocument();
        CinematicActionLibraryReader.Load([actionOwner, actionTable, localAction, fixedAction, nextAction],
            conflictingOrigins, unresolvedDocument);

        Check(unresolvedDocument.CinematicActionKitsByOwner.TryGetValue((0x10u, 0x100u),
                  out var unresolvedKit)
                && !unresolvedKit.ContainsKey(0) && unresolvedKit.ContainsKey(1),
            "Action kits should keep genuinely conflicting duplicate action resources unresolved");

        byte[] speedModeBytes = new byte[24];
        WriteUInt32(speedModeBytes, 0, 0x300);
        WriteUInt32(speedModeBytes, 4, 0x301);
        WriteUInt32(speedModeBytes, 8, 0x302);
        WriteUInt32(speedModeBytes, 12, 0x303);
        var speedOwner = new PcSceneLoader.Record(0x101, 'l', 0, 0)
        {
            LoadWorldKey = 0x10,
            Skeleton = 0x201
        };
        var speedTable = new PcSceneLoader.Record(0x201, 'b', 0, 16)
        {
            LoadWorldKey = 0x10
        };
        var speedActions = Enumerable.Range(0, 4).Select(index =>
            new PcSceneLoader.Record((uint)(0x300 + index), 'a', 16 + index * 2, 2)
            {
                LoadWorldKey = 0x10
            }).ToArray();
        var speedOrigins = speedActions.Append(speedOwner).Append(speedTable)
            .ToDictionary(record => record, _ => speedModeBytes);
        var speedModeDocument = new SceneDocument();
        CinematicActionLibraryReader.Load(speedOrigins.Keys, speedOrigins, speedModeDocument);
        Check(speedModeDocument.CinematicActionKitsByOwner.TryGetValue((0x10u, 0x101u),
                out var speedModeKit)
              && speedModeKit.Count == 4 && speedModeKit.ContainsKey(3),
            "Four-byte action kits should be selected when they resolve more source action resources");
        Console.WriteLine("PASS: standard and /BIN action-kit layouts, resource links and conflicting cross-world payloads");
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, sizeof(uint)), value);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
