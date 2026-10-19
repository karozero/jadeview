namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private void ReadResource(Record record)
    {
        int offset = record.Offset;
        int end = offset + record.Size;
        void Reference(int relativeOffset, char kind, bool keep = true) => EnqueueReferenceAt(offset + relativeOffset, kind, keep);
        switch (record.Kind)
        {
            case 'z':
                ReadWorldHeader(record);
                break;
            case 'n':
            case 'j':
                ReadObjectKeyList(record);
                break;
            case 'q':
                Reference(0, 's');
                Reference(4, 't');
                break;
            case 's':
                ReadScriptModel(offset, end);
                break;
            case 'k':
                Reference(0, 'j');
                break;
            case 'd':
                Reference(0, 'e');
                break;
            case 'f':
                ReadTransformList(record);
                break;
            case 'g':
                if (dataBytes[offset + 4] is 1 or 2 or 3 or 5)
                    Reference(0, 'h');
                break;
            case 'o':
                Reference(0, 'p');
                break;
            case 'v':
                for (int index = 0; index < ReadInt32(offset); index++)
                    Reference(4 + index * 4, 'x');
                break;
            case 'x':
                Reference(0, 'l');
                break;
            case 'w':
                ReadObjectLinks(offset);
                break;
            case 'b':
                ReadActionKeys(offset, end);
                break;
            case 'a':
                ReadActionEvents(record);
                break;
            case 't':
                ReadVariables(offset, end);
                break;
            case 'i':
                ReadAnimation(offset, end);
                break;
            case 'l':
                ReadObject(record);
                break;
            case 'm':
                ReadGraphicReferences(offset);
                break;
            case 'c':
            case 'e':
            case 'h':
            case 'p':
            case 'r':
                break; // Framed leaves have no dependency callbacks.
            default:
                throw new InvalidDataException($"Unsupported FF0 handler {record.Kind}.");
        }
    }

    private void ReadWorldHeader(Record record)
    {
        int offset = record.Offset;
        bool isWorld = ReadText(offset, 4) == ".wow" && ReadUInt32(offset + 4) == 3 && record.Size >= 180;
        if (!isWorld)
            throw new InvalidDataException("Invalid WOW.");
        record.Name = ReadText(offset + 16, 60);
        EnqueueReferenceAt(offset + 160, 'o');
        EnqueueReferenceAt(offset + 164, 'o');
        record.Group = ReadUInt32(offset + 168);
        EnqueueReferenceAt(offset + 168, 'n', false);
        EnqueueReferenceAt(offset + 172, 'v');
        result.Worlds.Add(record);
    }

    private void ReadGraphicReferences(int offset)
    {
        int type = ReadInt32(offset);
        switch (type)
        {
            case 4:
                for (int index = 0; index < ReadInt32(offset + 4); index++)
                    EnqueueReferenceAt(offset + 8 + index * 4, 'm');
                break;
            case 8:
                for (int index = 0; index < Math.Min(6, (int)dataBytes[offset + 4]); index++)
                    EnqueueReferenceAt(offset + 11 + index * 4, 'm');
                break;
            case 2:
                EnqueueReferenceAt(offset + 28, 'l');
                break;
            case 0 or 1 or 3 or 5 or 6 or 10 or 11:
                break;
            default:
                throw new InvalidDataException($"Unsupported graphic type {type} at {offset:X8}.");
        }
    }
}
