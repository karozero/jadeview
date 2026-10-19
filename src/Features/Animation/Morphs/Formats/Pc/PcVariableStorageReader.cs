namespace JadeView;

internal static class PcVariableStorageReader
{
    internal static PcVariableStorageLayout ReadLayout(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < PcVariableFormat.MinimumPayloadSize)
            throw new InvalidDataException("Truncated OVA header.");
        int descriptorBytes = BitConverter.ToInt32(bytes);
        bool validDescriptors = descriptorBytes >= 0 && descriptorBytes % PcVariableFormat.DescriptorSize == 0
            && descriptorBytes <= bytes.Length - PcVariableFormat.MinimumPayloadSize;
        if (!validDescriptors) throw new InvalidDataException("Invalid OVA descriptor block.");

        int descriptorCount = descriptorBytes / PcVariableFormat.DescriptorSize;
        int namesHeader = PcVariableFormat.BlockLengthSize + descriptorBytes;
        int namesLength = BitConverter.ToInt32(bytes[namesHeader..]);
        bool validNames = namesLength == (long)descriptorCount * PcVariableFormat.NameSize
            && namesLength <= bytes.Length - namesHeader - PcVariableFormat.BlockLengthSize * 2;
        if (!validNames) throw new InvalidDataException("Invalid OVA name block.");

        int storageHeader = namesHeader + PcVariableFormat.BlockLengthSize + namesLength;
        int storageLength = BitConverter.ToInt32(bytes[storageHeader..]);
        int storageStart = storageHeader + PcVariableFormat.BlockLengthSize;
        bool validStorage = storageLength >= 0 && storageLength <= bytes.Length - storageStart;
        if (!validStorage) throw new InvalidDataException("Invalid OVA storage block.");
        return new(descriptorCount, namesHeader + PcVariableFormat.BlockLengthSize, storageStart, storageLength);
    }
}
