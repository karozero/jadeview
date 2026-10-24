namespace JadeView;

internal sealed partial class PcSceneLoader
{
    private void ReadObject(Record record)
    {
        int end = record.Offset + record.Size;
        if (ReadText(record.Offset, 4) != ".gao")
            throw new InvalidDataException($"Expected GAO {record.Key:X8} at {record.Offset:X8}.");
        record.Flags = ReadUInt32(record.Offset + 8);
        record.SerializedStateFlags = ReadUInt32(record.Offset + 12);
        ReadOnlySpan<byte> gaoRecord = dataBytes.AsSpan(record.Offset, record.Size);
        if (!PcObjectBoundsReader.TryRead(gaoRecord, record.Flags, out record.Bounds, out string boundsError))
            record.BoundsError = boundsError;
        int position = record.Offset + PcObjectBoundsReader.SerializedOffset
            + ((record.Flags & PcObjectBounds.OrientedBoundsIdentityFlag) != 0
                ? PcObjectBoundsReader.OrientedPayloadSize
                : PcObjectBoundsReader.AxisAlignedPayloadSize);
        ReadBaseObject(record, ref position);
        ReadObjectExtensions(record, ref position, end);
        ReadObjectCollisionReferences(record, ref position);
        record.NameFieldOffset = position;
        int nameLength = ReadInt32(position);
        position += 4;
        record.Name = ReadText(position, nameLength);
        position += nameLength;
        if (position != end)
            throw new InvalidDataException($"GAO layout mismatch {record.Key:X8} at {record.Offset:X8}: {position:X8}/{end:X8}.");
    }

    private void ReadBaseObject(Record record, ref int position)
    {
        uint flags = record.Flags;
        if ((flags & 0x1000) == 0)
            return;
        if ((flags & 0x4000) != 0)
            ReadObjectAppearance(record, ref position);
        if ((flags & 0x400000) != 0)
        {
            record.Parent = ReadUInt32(position);
            EnqueueReferenceAt(position, 'l');
            EnqueueReferenceAt(position, 'l');
            position += 72;
        }

        if ((flags & 2) != 0)
        {
            record.Events = ReadUInt32(position);
            record.Shape = ReadUInt32(position + 4);
            record.Actions = ReadUInt32(position + 8);
            record.Skeleton = ReadUInt32(position + 12);
            EnqueueRequest(record.Skeleton, 'b');
            position += 16;
        }

        ReadObjectMultipartData(flags, ref position);
    }

    private void ReadObjectCollisionReferences(Record record, ref int position)
    {
        // Instance and map are independent references, serialized before the GAO name.
        // Either instance identity bit selects the same single instance field.
        if ((record.Flags & 0x600) != 0)
            record.CollisionInstance = ReadUInt32(position);
        ReadOptionalReference(ref position, record.Flags, 0x600, 'd');
        if ((record.Flags & 0x100) != 0)
            record.CollisionMap = ReadUInt32(position);
        ReadOptionalReference(ref position, record.Flags, 0x100, 'f');
    }

    private void ReadObjectAppearance(Record record, ref int position)
    {
        record.Geometry = ReadUInt32(position);
        record.Material = ReadUInt32(position + 4);
        EnqueueRequest(record.Geometry, 'm');
        EnqueueRequest(record.Material, 'm');
        position += 16;
        int colorCount = ReadInt32(position);
        position = checked(position + 4 + colorCount * 4);
    }

    private void ReadObjectMultipartData(uint flags, ref int position)
    {
        if ((flags & 0x200000) == 0)
            return;
        int count = ReadInt32(position);
        position += 4;
        bool hasObjectKeys = (flags & 0x1000000) != 0;
        if (!hasObjectKeys)
        {
            position = checked(position + count * 68);
            return;
        }

        for (int index = 0; index < count; index++, position += 8)
            EnqueueReferenceAt(position, 'l');
    }

    private void ReadObjectExtensions(Record record, ref int position, int end)
    {
        uint flags = record.Flags;
        if ((flags & 0x2000) == 0)
            return;
        record.Prefab = ReadUInt32(position);
        position += 4;
        if ((flags & 0x800000) != 0)
            EnqueueRequest(record.Prefab, 'k');
        uint hasModifiers = ReadUInt32(position);
        record.InitialCapabilities = (ushort)ReadUInt16(position + 12);
        position += 18;
        if ((flags & 0x20) != 0)
        {
            record.BrainFieldOffset = position;
            EnqueueRequest(ReadUInt32(position), 'q');
            position += 4;
        }

        if ((flags & 0x2000000) != 0) record.SceneEvents = ReadUInt32(position);
        ReadOptionalReference(ref position, flags, 0x2000000, 'i');
        if ((flags & 0x8000000) != 0)
        {
            record.SoundBank = ReadUInt32(position);
            EnqueueRequest(record.SoundBank, 'u');
            position += 4;
        }
        ReadOptionalReference(ref position, flags, 0x40000, 'w');
        if ((flags & 0x10) != 0)
            record.LightKey = ReadUInt32(position);
        ReadOptionalReference(ref position, flags, 0x10, 'm');
        if ((flags & 0x40) != 0)
            position += 92;
        if (hasModifiers != 0)
            ReadModifiers(ref position, end);
    }

    private void ReadOptionalReference(ref int position, uint flags, uint mask, char kind)
    {
        if ((flags & mask) == 0)
            return;
        EnqueueRequest(ReadUInt32(position), kind);
        position += 4;
    }
}
