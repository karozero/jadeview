namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private void ReadModifiers(ref int position, int end)
    {
        for (int modifierIndex = 0; modifierIndex < 1024; modifierIndex++)
        {
            uint type = ReadUInt32(position);
            position += 4;
            if (type == uint.MaxValue)
                return;
            position += 4;
            int start = position;
            ReadModifierReferences(type, position);
            int size = PcTextureLoader.ModifierSize(dataBytes, position, end, type);
            currentRecord?.Modifiers.Add((type, start, size));
            position = checked(start + size);
            if (position > end)
                throw new InvalidDataException("Modifier outside GAO.");
        }

        throw new InvalidDataException("Modifier dependency budget exceeded.");
    }

    private void ReadModifierReferences(uint type, int position)
    {
        switch (type)
        {
            case 3:
                bool hasDirectObjects = (ReadUInt32(position + 24) & 0x10) == 0;
                if (!hasDirectObjects)
                    return;
                EnqueueReferenceAt(position + 4, 'l');
                EnqueueReferenceAt(position + 8, 'l');
                EnqueueReferenceAt(position + 20, 'l');
                break;
            case 7:
                int version = ReadInt32(position);
                uint extra = version == 28 ? ReadUInt32(position + 12) : uint.MaxValue;
                if (extra == uint.MaxValue)
                    EnqueueReferenceAt(position + (version == 28 ? 16 : 12), 'l');
                break;
            case 8:
                bool hasShape = (ReadUInt32(position + 4) & 0x800) != 0 && ReadUInt32(position) > 272;
                if (hasShape)
                    EnqueueRequest(ReadUInt32(position + 12), 'u');
                break;
            case 9:
                int textureOffset = position + 12 + ReadInt32(position + 8) * 8;
                EnqueueRequest(ReadUInt32(textureOffset), 'm');
                break;
        }
    }
}
