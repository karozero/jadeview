namespace JadeView;

/// <summary>Evaluates Iris's source-confirmed packed-color interpolation helper.</summary>
internal static class CinematicPackedColorBlend
{
    private static readonly uint[] BlendMasks =
    [
        0xFEFEFEFEu, 0xFCFCFCFCu, 0xF8F8F8F8u, 0xF0F0F0F0u,
        0xE0E0E0E0u, 0xC0C0C0C0u, 0x80808080u
    ];

    internal static uint Evaluate(uint start, uint end, float factor)
    {
        if (!float.IsFinite(factor)) throw new ArgumentOutOfRangeException(nameof(factor));
        factor = Math.Clamp(factor, 0f, 1f);
        if (factor <= 0.02f) return start;
        if (factor >= 0.98000002f) return end;

        // Iris selects seven progressively finer packed-color masks from the
        // IEEE-754 mantissa, then adds the selected channel contributions.
        int selectorBits = BitConverter.SingleToInt32Bits(factor + 32768f + 16384f);
        uint blended = 0;
        for (int bitIndex = 7; bitIndex >= 1; bitIndex--)
        {
            int shift = 8 - bitIndex;
            uint mask = BlendMasks[shift - 1];
            uint selected = (selectorBits & (1 << bitIndex)) != 0 ? end : start;
            blended = unchecked(blended + ((selected & mask) >> shift));
        }
        return blended;
    }
}
