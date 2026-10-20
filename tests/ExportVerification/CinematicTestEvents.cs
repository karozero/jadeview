using JadeView;

internal static class CinematicTestEvents
{
    internal static AnimationKey Message(float time, uint target, int id, System.Numerics.Vector3 first,
        System.Numerics.Vector3 second, uint integer1, uint integer2, float scalar2 = 0, float scalar1 = 0,
        uint object1 = 0, uint object2 = 0)
    {
        uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        uint[] values = [target, 0, (uint)id, object1, object2, Bits(first.X), Bits(first.Y), Bits(first.Z), 0,
            Bits(second.X), Bits(second.Y), Bits(second.Z), 0, Bits(scalar2), Bits(scalar1), integer1, integer2, 0, 0];
        uint[] tags = [0x01000028, 0x02000806, 0x01000021, 0x01000028, 0x01000028,
            0x01000022, 0x01000022, 0x01000022, 0x03001770, 0x01000022, 0x01000022,
            0x01000022, 0x03001770, 0x01000022, 0x01000022, 0x01000021, 0x01000021, 0x03001f78, 0x1f000000];
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++) { writer.Write(values[index]); writer.Write(tags[index]); }
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, buffer.ToArray())] };
    }

    internal static AnimationKey VectorCall(float time, uint target, int selector, System.Numerics.Vector3 value)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(8);
        writer.Write(target); writer.Write(0x01000028u);
        writer.Write(0u); writer.Write(0x02000806u);
        foreach (float component in new[] { value.X, value.Y, value.Z })
        {
            writer.Write(component); writer.Write(0x01000022u);
        }
        writer.Write(0u); writer.Write(0x03801770u);
        writer.Write(0u); writer.Write(0x03190000u | (uint)selector);
        writer.Write(0u); writer.Write(0x1f000000u);
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, buffer.ToArray())] };
    }

    internal static AnimationKey Call(float time, uint target, int selector, params uint[] arguments)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(arguments.Length + 4);
        writer.Write(target); writer.Write(0x01000028u);
        writer.Write(0u); writer.Write(0x02000806u);
        foreach (uint argument in arguments) { writer.Write(argument); writer.Write(0x01000021u); }
        writer.Write(0u); writer.Write(0x03000000u | (uint)selector);
        writer.Write(0u); writer.Write(0x1f000000u);
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, buffer.ToArray())] };
    }
}
