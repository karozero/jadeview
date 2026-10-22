using System.Buffers.Binary;
using System.Text;

namespace JadeView;

internal static class PcObjectReferenceListReader
{
    internal static (string Variable, uint Key, int? ArrayIndex)[] Read(ReadOnlySpan<byte> bytes) =>
        ReadBlock(bytes.ToArray()).Where(variable => variable.Type == PcVariableFormat.ObjectReferenceType)
            .SelectMany(variable => ReadValues(variable, variable.ArrayDimensions, 0, variable.Count)
                .Select(value => (variable.Name, value.Key,
                    variable.ArrayDimensions > 0 ? (int?)value.Index : null)))
            .ToArray();

    internal static PcObjectReferenceValue[] ReadOverlayBlocks(IEnumerable<ReadOnlyMemory<byte>> modelBlocks,
        ReadOnlySpan<byte> instanceBytes)
    {
        var modelVariables = new Dictionary<string, ObjectReferenceVariable>(StringComparer.OrdinalIgnoreCase);
        foreach (var modelBlock in modelBlocks)
        {
            foreach (var variable in ReadBlock(modelBlock.ToArray()))
            {
                if (variable.Type == PcVariableFormat.ObjectReferenceType)
                    modelVariables.TryAdd(variable.Name, variable);
            }
        }

        var instanceVariables = ReadBlock(instanceBytes.ToArray())
            .Where(variable => variable.Type == PcVariableFormat.ObjectReferenceType)
            .ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var effectiveReferences = new List<PcObjectReferenceValue>();
        foreach (var modelVariable in modelVariables.Values)
        {
            if (instanceVariables.TryGetValue(modelVariable.Name, out var instanceVariable))
            {
                AppendOverlay(modelVariable, instanceVariable);
            }
            else
            {
                Append(modelVariable, "model", modelVariable.Count);
            }
        }

        return effectiveReferences.ToArray();

        void AppendOverlay(ObjectReferenceVariable modelVariable, ObjectReferenceVariable instanceVariable)
        {
            int copiedCount = Math.Min(modelVariable.Count, instanceVariable.Count);
            Append(instanceVariable, "instance", copiedCount, modelVariable.ArrayDimensions,
                variableName: modelVariable.Name);
            Append(modelVariable, "model", modelVariable.Count - copiedCount, startIndex: copiedCount);
        }

        void Append(ObjectReferenceVariable variable, string source, int count,
            int? dimensionsOverride = null, int startIndex = 0, string? variableName = null)
        {
            foreach (var (key, sourceIndex) in ReadValues(variable,
                dimensionsOverride ?? variable.ArrayDimensions, startIndex, count))
            {
                int? arrayIndex = variable.ArrayDimensions > 0 ? sourceIndex : null;
                effectiveReferences.Add(new(variableName ?? variable.Name, key, source, arrayIndex));
            }
        }
    }

    private static ObjectReferenceVariable[] ReadBlock(byte[] bytes)
    {
        if (bytes.Length == 0) return [];
        var layout = PcVariableStorageReader.ReadLayout(bytes);
        var variables = new ObjectReferenceVariable[layout.DescriptorCount];
        for (int index = 0; index < layout.DescriptorCount; index++)
        {
            int descriptorOffset = PcVariableFormat.BlockLengthSize + index * PcVariableFormat.DescriptorSize;
            uint valueOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[descriptorOffset..]);
            uint packedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(descriptorOffset + sizeof(uint))..]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(descriptorOffset + sizeof(uint) * 2)..]);
            string name = ReadName(bytes, layout.NamesStart + index * PcVariableFormat.NameSize);
            uint countValue = packedLength & 0x3fffffff;
            int arrayDimensions = (int)(packedLength >> 30);
            ulong valuesLength = (ulong)countValue * sizeof(uint);
            ulong headerLength = (ulong)arrayDimensions * sizeof(uint);
            if (countValue > int.MaxValue || valueOffset > layout.StorageLength
                || headerLength + valuesLength > (ulong)layout.StorageLength - valueOffset)
                throw new InvalidDataException($"OVA variable '{name}' exceeds its storage block.");

            int storageOffset = checked(layout.StorageStart + (int)valueOffset);
            variables[index] = new(name, type, (int)countValue, arrayDimensions,
                bytes, storageOffset, checked(layout.StorageStart + layout.StorageLength));
        }
        return variables;
    }

    private static (uint Key, int Index)[] ReadValues(ObjectReferenceVariable variable, int dimensions,
        int startIndex, int count)
    {
        int valueOffset = checked(variable.StorageOffset + dimensions * sizeof(uint)
            + startIndex * sizeof(uint));
        int byteLength = checked(count * sizeof(uint));
        if (valueOffset > variable.StorageEnd || byteLength > variable.StorageEnd - valueOffset)
            throw new InvalidDataException($"OVA variable '{variable.Name}' has no room for its reference array header or values.");
        var values = new (uint Key, int Index)[count];
        for (int index = 0; index < count; index++)
            values[index] = (BinaryPrimitives.ReadUInt32LittleEndian(variable.Bytes.AsSpan(
                valueOffset + index * sizeof(uint))), startIndex + index);
        return values;
    }

    private static string ReadName(ReadOnlySpan<byte> bytes, int offset)
    {
        var name = bytes.Slice(offset, PcVariableFormat.NameSize);
        int terminator = name.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? name : name[..terminator]);
    }

    private sealed record ObjectReferenceVariable(string Name, ushort Type, int Count, int ArrayDimensions,
        byte[] Bytes, int StorageOffset, int StorageEnd);
}

internal sealed record PcObjectReferenceValue(string VariableName, uint TargetKey, string Source,
    int? ArrayIndex);
