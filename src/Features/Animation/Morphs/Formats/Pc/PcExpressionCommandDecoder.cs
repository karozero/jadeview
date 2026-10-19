namespace JadeView;

// Shared packed command format used by PC facial message handlers.
public static class PcExpressionCommandDecoder
{
    private const float IntegerLimitExclusive = 2147483648f;
    private const double NativeIntegerBias = 6755399441055744.0; // 0x4338000000000000 at PC 00BA4420.
    private const int GroupScale = 1000;
    private const float WeightMidpoint = .5f;
    private const float WeightScale = 10;
    public static PcExpressionCommand? Decode(int channel, float packed)
    {
        bool invalidChannel = channel < 0;
        bool invalidValue = !float.IsFinite(packed) || packed < 0 || packed >= IntegerLimitExclusive;
        if (invalidChannel || invalidValue) throw new ArgumentException("Invalid packed PC expression command.");
        // PC 0040CF80 adds the double bias and returns its low word.
        // Emulate its normal round-to-nearest/even mode, rather than truncating.
        long roundedBits = BitConverter.DoubleToInt64Bits((double)packed + NativeIntegerBias);
        int value = unchecked((int)roundedBits);
        if (value == 0) return null; // Native consumer leaves this slot unchanged.
        int group = value / GroupScale;
        float fraction = MathF.Abs(value / (float)GroupScale - group);
        float weight = (fraction - WeightMidpoint) * WeightScale;
        return new(channel, BitConverter.SingleToUInt32Bits(packed), group, weight);
    }
}
