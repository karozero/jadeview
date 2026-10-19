namespace JadeView;

// Shared packed command format used by PC facial message handlers.
public static class PcExpressionCommandDecoder
{
    public static PcExpressionCommand? Decode(int channel, float packed)
    {
        bool invalidChannel = channel < 0;
        bool invalidValue = !float.IsFinite(packed) || packed < 0 || packed >= 2147483648f;
        if (invalidChannel || invalidValue) throw new ArgumentException("Invalid packed PC expression command.");
        int value = (int)packed;
        if (value == 0) return null; // Native consumer leaves this slot unchanged.
        int group = value / 1000;
        float fraction = MathF.Abs(value / 1000f - group);
        float weight = (fraction - .5f) * 10;
        return new(channel, BitConverter.SingleToUInt32Bits(packed), group, weight);
    }
}
