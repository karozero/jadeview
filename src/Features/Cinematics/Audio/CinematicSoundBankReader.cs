namespace JadeView;

internal static class CinematicSoundBankReader
{
    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        foreach (var record in records.Where(record => record.Kind == 'u'))
        {
            byte[] bytes = origins[record];
            if (record.Offset < 8 || !bytes.AsSpan(record.Offset - 8, 4).SequenceEqual(".snk"u8)) continue;
            int headerSize = Preview.I32(bytes, record.Offset - 4);
            if (headerSize < 0 || headerSize > record.Size - 8) continue;
            int listOffset = record.Offset + headerSize;
            int listSize = Preview.I32(bytes, listOffset);
            int count = Preview.I32(bytes, listOffset + 4);
            bool valid = count is >= 0 and <= 65535 && listSize == 4 + count * 8
                && headerSize + 4 + listSize <= record.Size;
            if (!valid) continue; // Variable-size sound-modifier banks need a separate reader.
            document.CinematicSoundBanks[record.Key] = Enumerable.Range(0, count)
                .Select(index => Preview.U32(bytes, listOffset + 8 + index * 8)).ToArray();
        }
    }
}
