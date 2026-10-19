namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private void ReadVariables(int offset, int end)
    {
        if (offset == end)
            return;
        int descriptorBytes = ReadInt32(offset);
        int descriptorCount = descriptorBytes / 12;
        var descriptors = new VariableDescriptor[descriptorCount];
        for (int index = 0; index < descriptorCount; index++)
        {
            int descriptorOffset = offset + 4 + index * 12;
            descriptors[index] = new VariableDescriptor(ReadUInt32(descriptorOffset + 4), ReadUInt16(descriptorOffset + 8));
        }

        int position = checked(offset + 4 + descriptorBytes);
        position = checked(position + 4 + descriptorCount * 30);
        int valuesPresent = ReadInt32(position);
        position += 4;
        if (valuesPresent != 0)
        {
            foreach (var descriptor in descriptors)
                SkipVariableValue(ref position, descriptor, end);
        }

        ReadVariableCodeReferences(ref position, end);
        if (position != end)
            throw new InvalidDataException($"OVA layout mismatch at {offset:X8}: {position:X8}/{end:X8}.");
    }

    private void SkipVariableValue(ref int position, VariableDescriptor descriptor, int end)
    {
        uint elementCount = descriptor.PackedLength;
        if (elementCount != 1)
            position += checked((int)(elementCount >> 30) * 4);
        elementCount &= 0x3fffffff;
        int stride = descriptor.Type switch
        {
            37 => 12,
            41 => 52,
            44 => 8,
            32 or 33 or 34 or 38 or 39 or 40 or 42 or 43 or 45 or 46 or 132 => 4,
            _ => 0
        };
        if (descriptor.Type == 40)
        {
            bool fitsPayload = position <= end && elementCount <= (uint)((end - position) / 4);
            if (!fitsPayload)
                throw new InvalidDataException("OVA GAO references outside payload.");
            for (uint itemIndex = 0; itemIndex < elementCount; itemIndex++)
                currentRecord?.GaoReferenceOffsets.Add(checked(position + (int)itemIndex * 4));
        }

        position = checked(position + (int)elementCount * stride);
    }

    private void ReadVariableCodeReferences(ref int position, int end)
    {
        for (int index = 0; index < 5; index++, position += 4)
            EnqueueRequest(ReadUInt32(position), 'r');
        if (position >= end)
            return;
        int referenceCount = ReadInt32(position);
        position += 4;
        for (int index = 0; index < referenceCount; index++, position += 4)
            EnqueueRequest(ReadUInt32(position), 'r');
    }
}
