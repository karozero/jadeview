namespace JadeView;

internal static class CinematicSoundBankReader
{
    private static readonly HashSet<uint> SoundAssetTypes =
    [
        0x6461772E, // .wad
        0x6D61772E, // .wam
        0x6161772E, // .waa
        0x646D732E, // .smd
        0x6361772E, // .wac
        0x7661772E  // .wav
    ];

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        PcSceneLoader.Record[] soundRecords = records.Where(record => record.Kind == 'u').ToArray();
        uint[] referencedKeys = document.Worlds.SelectMany(world => world.Objects)
            .SelectMany(sceneObject => ReferencedSoundBankKeys(sceneObject))
            .Where(key => key is not (0 or uint.MaxValue)).Distinct().ToArray();
        var referencedKeySet = referencedKeys.ToHashSet();

        foreach (var record in soundRecords.Where(record => referencedKeySet.Contains(record.Key)))
        {
            byte[] bytes = origins[record];
            int tagOffset = record.Offset - 8;
            if (tagOffset < 0 || tagOffset > bytes.Length - 8)
            {
                document.Diagnostics.Add($"Sound bank {record.Key:X8} has an invalid source range.");
                continue;
            }

            if (record.Size < 0 || record.Offset > bytes.Length - record.Size)
            {
                document.Diagnostics.Add($"Sound bank {record.Key:X8} has an invalid source range.");
                continue;
            }

            bool isLanguageMetabank = bytes.AsSpan(tagOffset, 4).SequenceEqual(".snk"u8);
            if (isLanguageMetabank)
            {
                if (TryReadLanguageMetabank(record, bytes, out uint[] childKeys))
                {
                    document.CinematicSoundBanks[record.Key] = childKeys;
                    continue;
                }

                document.Diagnostics.Add($"Language sound bank {record.Key:X8} has an invalid .snk entry table.");
                continue;
            }

            if (record.Size % 8 != 0)
            {
                document.Diagnostics.Add($"Sound bank {record.Key:X8} has an invalid 8-byte entry list "
                    + $"(size {record.Size}).");
                continue;
            }

            int count = record.Size / 8;
            uint[] soundKeys = Enumerable.Range(0, count)
                .Select(index => Preview.U32(bytes, record.Offset + index * 8)).ToArray();
            uint? invalidType = Enumerable.Range(0, count)
                .Select(index => Preview.U32(bytes, record.Offset + index * 8 + 4))
                .Where(type => !SoundAssetTypes.Contains(type))
                .Select(type => (uint?)type).FirstOrDefault();
            if (invalidType.HasValue)
            {
                document.Diagnostics.Add($"Sound bank {record.Key:X8} contains unsupported group type "
                    + $"{invalidType.Value:X8}; it was not interpreted as a sound-slot table.");
                continue;
            }
            document.CinematicSoundBanks[record.Key] = soundKeys;
        }

        var availableKeys = soundRecords.Select(record => record.Key).ToHashSet();
        foreach (uint bankKey in referencedKeys.Where(key => !document.CinematicSoundBanks.ContainsKey(key)))
        {
            if (availableKeys.Contains(bankKey)) continue;
            document.Diagnostics.Add($"Sound bank {bankKey:X8}: the referenced record is not present "
                + "in the loaded PC resource set.");
        }
    }

    private static bool TryReadLanguageMetabank(PcSceneLoader.Record record, byte[] bytes, out uint[] childKeys)
    {
        childKeys = [];
        if (record.Size < 8) return false;

        int headerSize = Preview.I32(bytes, record.Offset - 4);
        if (headerSize < 0 || headerSize > record.Size - 8) return false;

        int listOffset = record.Offset + headerSize;
        int recordEnd = record.Offset + record.Size;
        if (listOffset < record.Offset || listOffset > recordEnd - 8) return false;

        int listSize = Preview.I32(bytes, listOffset);
        int count = Preview.I32(bytes, listOffset + 4);
        bool validList = count is >= 0 and <= 65535
            && listSize == 4 + count * 8
            && headerSize + 4 + listSize <= record.Size
            && listOffset + 4 + listSize <= recordEnd;
        if (!validList) return false;

        childKeys = Enumerable.Range(0, count)
            .Select(index => Preview.U32(bytes, listOffset + 8 + index * 8)).ToArray();
        return true;
    }

    private static IEnumerable<uint> ReferencedSoundBankKeys(SceneObject sceneObject)
    {
        yield return sceneObject.SoundBankKey;
        foreach (SceneModifier modifier in sceneObject.Modifiers)
        {
            byte[] data = modifier.Data;
            if (modifier.Type == 8 && data.Length >= 16 && (Preview.U32(data, 4) & 0x800) != 0)
                yield return Preview.U32(data, 12);
        }
    }
}
