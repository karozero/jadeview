using System.Text;
using JadeView;

internal static class CinematicOwnerDefaults
{
    internal sealed record Variable(string Name, uint PackedLength, ushort Type,
        byte[] RawBytes, string Source);
    internal sealed record VariableSnapshot(string Name, ushort Type, uint PackedLength,
        string ValueBytes, string Source);
    internal sealed record Snapshot(string BrainKey, string ModelKey, string ModelVariablesKey,
        string InstanceVariablesKey, VariableSnapshot[] Variables, string[] UnmatchedInstanceNames,
        string Limits);

    private static readonly IReadOnlyDictionary<ushort, int> Strides = new Dictionary<ushort, int>
    {
        [32] = 4,
        [33] = 4,
        [34] = 4,
        [37] = 12,
        [38] = 4,
        [39] = 4,
        [40] = 4,
        [41] = 52,
        [42] = 4,
        [43] = 4,
        [44] = 8,
        [45] = 4,
        [46] = 4,
        [132] = 4
    };

    internal static Snapshot Read(uint brainKey, IReadOnlyList<ArchiveFacialScriptCensus.SourceRecord> closure)
    {
        var brain = Find(brainKey, 'q');
        uint modelKey = ReadUInt32(brain, 0);
        uint instanceVariablesKey = ReadUInt32(brain, 4);
        var model = Find(modelKey, 's');
        var modelVariablesKeys = new HashSet<uint>();
        for (int offset = 0; offset < model.Size; offset += 8)
        {
            var tag = model.Source.AsSpan(model.Offset + offset + 4, 4);
            if (tag.SequenceEqual(".ova"u8))
                modelVariablesKeys.Add(ReadUInt32(model, offset));
        }
        if (modelVariablesKeys.Count != 1)
            throw new InvalidDataException($"Expected one model OVA resource, found {modelVariablesKeys.Count}.");

        var modelVariables = ReadBlock(Find(modelVariablesKeys.Single(), 't'));
        var instanceVariables = ReadBlock(Find(instanceVariablesKey, 't'));
        var merged = modelVariables.Select(variable => variable with { Source = "model" }).ToArray();
        var unmatched = new List<string>();
        foreach (var instanceVariable in instanceVariables)
        {
            int index = Array.FindIndex(merged, variable =>
                variable.Name.Equals(instanceVariable.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                unmatched.Add(instanceVariable.Name);
                continue;
            }

            var defaultVariable = merged[index];
            merged[index] = OverlayVariable(defaultVariable, instanceVariable);
        }

        return new(brainKey.ToString("X8"), modelKey.ToString("X8"),
            modelVariablesKeys.Single().ToString("X8"), instanceVariablesKey.ToString("X8"),
            merged.Select(variable => new VariableSnapshot(variable.Name, variable.Type,
                variable.PackedLength, Convert.ToHexString(variable.RawBytes), variable.Source)).ToArray(), unmatched.ToArray(),
            "Initial serialized OVA blocks only; values are not executed, reference-resolved, or tied to the trigger that starts this owner.");

        ArchiveFacialScriptCensus.SourceRecord Find(uint key, char kind)
        {
            var record = closure.SingleOrDefault(candidate => candidate.Key == key && candidate.Kind == kind);
            return record ?? throw new InvalidDataException($"Missing {kind} resource {key:X8} in owner brain closure.");
        }
    }

    internal static Variable OverlayVariable(Variable modelVariable, Variable instanceVariable)
    {
        if (!Strides.TryGetValue(modelVariable.Type, out int modelStride))
            throw new InvalidDataException($"Unsupported OVA model variable type {modelVariable.Type}.");

        int modelDimensions = checked((int)(modelVariable.PackedLength >> 30));
        int modelElementCount = checked((int)(modelVariable.PackedLength & 0x3FFFFFFF));
        int instanceElementCount = checked((int)(instanceVariable.PackedLength & 0x3FFFFFFF));
        int copiedElementCount = Math.Min(modelElementCount, instanceElementCount);
        int modelValueOffset = checked(modelDimensions * sizeof(uint));
        int copyByteCount = checked(copiedElementCount * modelStride);
        if (modelValueOffset > modelVariable.RawBytes.Length
            || copyByteCount > modelVariable.RawBytes.Length - modelValueOffset
            || modelValueOffset > instanceVariable.RawBytes.Length
            || copyByteCount > instanceVariable.RawBytes.Length - modelValueOffset)
            throw new InvalidDataException($"OVA prefix for variable '{modelVariable.Name}' exceeds its serialized storage.");

        byte[] effectiveBytes = (byte[])modelVariable.RawBytes.Clone();
        instanceVariable.RawBytes.AsSpan(modelValueOffset, copyByteCount)
            .CopyTo(effectiveBytes.AsSpan(modelValueOffset, copyByteCount));
        string source = modelVariable.PackedLength == instanceVariable.PackedLength
            ? "instance" : "instance prefix";
        return modelVariable with { RawBytes = effectiveBytes, Source = source };
    }

    internal static CinematicScriptCallbackSlot[]? ReadCallbackSlots(uint brainKey,
        IReadOnlyList<ArchiveFacialScriptCensus.SourceRecord> closure,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        var brain = Find(brainKey, 'q');
        var model = Find(ReadUInt32(brain, 0), 's');
        CinematicScriptCallbackSlot[]? firstValidSlots = null;
        foreach (var variableRecord in closure.Where(record => record.Kind == 't'
                     && model.References.Contains(record.Key)))
        {
            var slots = PcScriptCallbackSlotReader.Read(
                variableRecord.Source.AsSpan(variableRecord.Offset, variableRecord.Size), functionNames);
            if (slots == null) continue;
            firstValidSlots ??= slots;
            if (slots.Any(slot => slot.FunctionKey != 0)) return slots;
        }
        return firstValidSlots;

        ArchiveFacialScriptCensus.SourceRecord Find(uint key, char kind)
        {
            var record = closure.SingleOrDefault(candidate => candidate.Key == key && candidate.Kind == kind);
            return record ?? throw new InvalidDataException($"Missing {kind} resource {key:X8} in owner brain closure.");
        }
    }

    private static List<Variable> ReadBlock(ArchiveFacialScriptCensus.SourceRecord record)
    {
        ReadOnlySpan<byte> payload = record.Source.AsSpan(record.Offset, record.Size);
        var descriptors = ReadDescriptors(payload);
        int dataStart = checked(12 + descriptors.Count * 42);
        uint dataSize = ReadUInt32(payload, dataStart - 4);
        if (dataSize > payload.Length - dataStart)
            throw new InvalidDataException("OVA variable storage exceeds the retained resource.");
        ReadOnlySpan<byte> data = payload.Slice(dataStart, (int)dataSize);
        var variables = new List<Variable>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            int prefixBytes = descriptor.PackedLength == 1
                ? 0 : checked((int)(descriptor.PackedLength >> 30) * 4);
            int count = checked((int)(descriptor.PackedLength & 0x3FFFFFFF));
            int stride = Strides.TryGetValue(descriptor.Type, out int knownStride)
                ? knownStride : throw new InvalidDataException($"Unsupported OVA variable type {descriptor.Type}.");
            int size = checked(prefixBytes + count * stride);
            if (descriptor.FirstWord > data.Length || size > data.Length - descriptor.FirstWord)
                throw new InvalidDataException($"Variable '{descriptor.Name}' exceeds OVA variable storage.");
            variables.Add(new(descriptor.Name, descriptor.PackedLength,
                descriptor.Type, data.Slice((int)descriptor.FirstWord, size).ToArray(), "model"));
        }
        return variables;
    }

    private static List<(string Name, uint FirstWord, uint PackedLength, ushort Type)> ReadDescriptors(
        ReadOnlySpan<byte> payload)
    {
        uint descriptorBytes = ReadUInt32(payload, 0);
        if (descriptorBytes % 12 != 0)
            throw new InvalidDataException("OVA descriptor byte count is not divisible by 12.");
        int count = checked((int)(descriptorBytes / 12));
        int namesOffset = checked(8 + (int)descriptorBytes);
        int valuesOffset = checked(namesOffset + count * 30);
        bool valuesPresent = ReadUInt32(payload, valuesOffset) != 0;
        int valuePosition = valuesOffset + 4;
        var variables = new List<(string, uint, uint, ushort)>(count);
        for (int index = 0; index < count; index++)
        {
            int descriptorOffset = 4 + index * 12;
            uint firstWord = ReadUInt32(payload, descriptorOffset);
            uint packedLength = ReadUInt32(payload, descriptorOffset + 4);
            ushort type = ReadUInt16(payload, descriptorOffset + 8);
            string name = ReadNullTerminated(payload.Slice(namesOffset + index * 30, 30));
            variables.Add((name, firstWord, packedLength, type));

            if (!valuesPresent) continue;
            int prefixBytes = packedLength == 1 ? 0 : checked((int)(packedLength >> 30) * 4);
            int elementCount = checked((int)(packedLength & 0x3FFFFFFF));
            if (!Strides.TryGetValue(type, out int stride))
                throw new InvalidDataException($"Unsupported OVA variable type {type}.");
            valuePosition = checked(valuePosition + prefixBytes + elementCount * stride);
            if (valuePosition > payload.Length)
                throw new InvalidDataException("OVA serialized values exceed the retained resource.");
        }
        return variables;
    }

    private static string ReadNullTerminated(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }

    private static uint ReadUInt32(ArchiveFacialScriptCensus.SourceRecord record, int offset) =>
        ReadUInt32(record.Source.AsSpan(record.Offset, record.Size), offset);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4)
            throw new InvalidDataException("OVA field lies outside the retained resource.");
        return BitConverter.ToUInt32(bytes.Slice(offset, 4));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 2)
            throw new InvalidDataException("OVA descriptor lies outside the retained resource.");
        return BitConverter.ToUInt16(bytes.Slice(offset, 2));
    }
}
