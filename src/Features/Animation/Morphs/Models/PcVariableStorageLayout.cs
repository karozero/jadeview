namespace JadeView;

internal readonly record struct PcVariableStorageLayout(int DescriptorCount, int NamesStart,
    int StorageStart, int StorageLength);
