namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private void ReadObjectKeyList(Record record)
    {
        if (record.Size % 4 != 0)
            throw new InvalidDataException("Invalid GAO key list.");
        for (int offset = record.Offset; offset < record.Offset + record.Size; offset += 4)
            EnqueueReferenceAt(offset, 'l');
    }

    private void ReadScriptModel(int offset, int end)
    {
        for (int position = offset; position + 8 <= end; position += 8)
        {
            string tag = ReadText(position + 4, 4);
            if (tag == ".ova")
                EnqueueRequest(ReadUInt32(position), 't');
            else if (tag == ".ofc")
                EnqueueRequest(ReadUInt32(position), 'r');
        }
    }

    private void ReadTransformList(Record record)
    {
        if (record.Size == 4)
        {
            EnqueueReferenceAt(record.Offset, 'g');
            return;
        }

        for (int index = 0; index < dataBytes[record.Offset]; index++)
            EnqueueReferenceAt(record.Offset + 4 + index * 4, 'g');
    }

    private void ReadObjectLinks(int offset)
    {
        int position = offset + 4;
        for (int index = 0; index < ReadInt32(offset); index++)
        {
            EnqueueRequest(ReadUInt32(position), 'x');
            int linkCount = ReadInt32(position + 4);
            position += 8;
            for (int linkIndex = 0; linkIndex < linkCount; linkIndex++, position += 9)
                EnqueueReferenceAt(position + 2, 'l');
        }
    }

    private void ReadActionKeys(int offset, int end)
    {
        // Keep discovery stride-wide: a BF may contain normal 8-byte records or
        // the 4-byte references emitted by the engine's separate /BIN path.
        for (int position = offset; position < end; position += PcActionKitFormat.ActionKeySize)
        {
            uint key = ReadUInt32(position);
            if (key == 1)
                position += PcActionKitFormat.ActionKeySize;
            else
                EnqueueRequest(key, 'a');
        }
    }

    private void ReadActionEvents(Record record)
    {
        if (record.Size <= 0 || dataBytes[record.Offset] == 0)
            return;
        for (int index = 0; index < dataBytes[record.Offset]; index++)
        {
            int position = record.Offset + 2 + index * 19;
            EnqueueRequest(ReadUInt32(position), 'i');
            EnqueueRequest(ReadUInt32(position + 8), 'c');
        }
    }
}
