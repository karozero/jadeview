using System.Buffers.Binary;

namespace JadeView;
/// <summary>Removes references to GAOs which no longer have a primary world owner.</summary>
internal static class PcSceneReferences
{
    internal static byte[] Remove(byte[] frame, PcSceneLoader.Record record, ISet<uint> removedKeys)
    {
        var removedFields = FindRemovedFields(frame, record, removedKeys);
        if (removedFields.Count == 0)
            return frame;
        if (record.Kind == 'i')
            return RemoveAnimationChannels(frame, record, removedKeys, removedFields.Count);
        if (record.Kind is 'n' or 'j')
            return RemoveListEntries(frame, removedFields);
        // FFFFFFFF is the native null key for extensions and deferred OVA values.
        foreach (int field in removedFields)
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(field, 4), uint.MaxValue);
        return frame;
    }

    private static HashSet<int> FindRemovedFields(byte[] frame, PcSceneLoader.Record record, ISet<uint> removedKeys)
    {
        var fields = record.GaoReferenceOffsets.Distinct().Select(offset => offset - record.FrameRange.Offset).ToArray();
        foreach (int field in fields)
        {
            bool isInsideFrame = field >= 4 && field <= frame.Length - 4;
            if (!isInsideFrame)
                throw new InvalidDataException($"Invalid GAO reference field in {record.Key:X8}.");
        }

        return fields.Where(field => removedKeys.Contains(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(field, 4)))).ToHashSet();
    }

    private static byte[] RemoveAnimationChannels(byte[] frame, PcSceneLoader.Record record, ISet<uint> removedKeys, int removedFieldCount)
    {
        // Drop whole playback tracks instead of introducing null channel targets.
        int removedCount = record.AnimationChannels.Count(channel => removedKeys.Contains(channel.Target));
        int channelCount = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));
        bool matchesChannelLayout = record.AnimationChannels.Count == channelCount && removedCount == removedFieldCount;
        if (!matchesChannelLayout)
            throw new InvalidDataException($"Inconsistent animation channels in {record.Key:X8}.");
        using var stream = new MemoryStream();
        stream.Write(frame.AsSpan(0, 8));
        foreach (var channel in record.AnimationChannels.Where(channel => !removedKeys.Contains(channel.Target)))
            stream.Write(frame.AsSpan(channel.Range.Offset - record.FrameRange.Offset, channel.Range.Size));
        byte[] result = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2), checked((ushort)(channelCount - removedCount)));
        BinaryPrimitives.WriteInt32LittleEndian(result, result.Length - 4);
        return result;
    }

    private static byte[] RemoveListEntries(byte[] frame, HashSet<int> removedFields)
    {
        byte[] result = new byte[checked(frame.Length - removedFields.Count * 4)];
        int cursor = 4;
        for (int field = 4; field < frame.Length; field += 4)
        {
            if (removedFields.Contains(field))
                continue;
            frame.AsSpan(field, 4).CopyTo(result.AsSpan(cursor));
            cursor += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(result, result.Length - 4);
        return result;
    }
}
