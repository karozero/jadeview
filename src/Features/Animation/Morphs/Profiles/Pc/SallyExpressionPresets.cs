namespace JadeView;

// PC 005ADBF0 local kit table. Only settled poses; controller transitions and
// dialogue scheduling are not synthesized. Names/emotions are not established.
public static class SallyExpressionPresets
{
    public static IReadOnlyList<int> Kits { get; } = Array.AsReadOnly(new[] { 2, 3, 4, 5 });

    public static SallyExpressionCommand[] Commands(int kit)
    {
        float[] packed = kit switch
        {
            2 => [1500, 2540, 10600, 31600, 600],
            3 or 5 => [1520, 2530, 11600, 34550, 52515],
            4 => [1550, 2560, 16600, 35574, 31600],
            _ => throw new ArgumentOutOfRangeException(nameof(kit), "Unqualified PC expression kit.")
        };
        return packed.Select((value, channel) => Decode(channel, value)!).ToArray();
    }

    public static SallyExpressionCommand? Decode(int channel, float packed)
    {
        var command = PcExpressionCommandDecoder.Decode(channel, packed);
        return command == null ? null : new(command.Channel, command.PackedBits, command.Group, command.Weight);
    }

    public static float[] SettledWeights(int kit, int groupCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCount);
        var weights = new float[groupCount];
        foreach (var command in Commands(kit))
        {
            if (command.Group >= groupCount) throw new ArgumentException("Preset group outside the source modifier.");
            weights[command.Group] += command.Weight;
        }
        return weights;
    }
}
