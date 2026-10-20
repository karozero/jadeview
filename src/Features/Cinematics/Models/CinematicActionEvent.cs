namespace JadeView;

internal sealed record CinematicActionEvent(float Time, uint Target, int Selector,
    int Action, uint KitOwner = 0, int LastAction = 0, int? Frequency = null, int StartFrame = 0,
    bool Force = false, bool Release = false, bool FrequencyOnly = false)
{
    internal static CinematicActionEvent? Decode(AnimationKey key, uint trackTarget, AnimationOpaquePayload payload)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length < 4) return null;
        bool hasContext = CinematicWordPairList.IsLiteral(rows[0], 0x28)
            && CinematicWordPairList.HasSelector(rows[1], 2, 2054);
        if (!hasContext) return null;
        uint target = rows[0].Value == 0 ? trackTarget : rows[0].Value;
        if (CinematicFacialEvent.TryDecodeMessage(payload, out var message, allowLiteralFlags: true)
            && BitConverter.ToUInt32(message, 48) == 47)
            return new(key.Time, target, 0x3391, BitConverter.ToInt32(message, 40),
                Release: BitConverter.ToInt32(message, 44) == 0);

        bool hasTerminator = CinematicWordPairList.IsEnd(rows[^1]);
        if (!hasTerminator) return null;
        if (rows.Length == 5 && CinematicWordPairList.IsLiteral(rows[2], 0x21)
            && CinematicWordPairList.HasSelector(rows[3], 3, 0x3391))
            return new(key.Time, target, 0x3391, unchecked((int)rows[2].Value));
        bool hasTwoIntegers = rows.Length == 6 && CinematicWordPairList.IsLiteral(rows[2], 0x21)
            && CinematicWordPairList.IsLiteral(rows[3], 0x21);
        if (hasTwoIntegers && rows[2].Value == 0 && CinematicWordPairList.HasSelector(rows[4], 3, 0x32c9))
            return new(key.Time, target, 0x32c9, -1, Frequency: unchecked((int)rows[3].Value), FrequencyOnly: true);
        if (hasTwoIntegers && CinematicWordPairList.HasSelector(rows[4], 3, 0x339c))
            return new(key.Time, target, 0x3391, unchecked((int)rows[2].Value),
                Frequency: unchecked((int)rows[3].Value), Force: true);
        if (hasTwoIntegers && CinematicWordPairList.HasSelector(rows[4], 3, 0x339b))
            return new(key.Time, target, 0x3391, unchecked((int)rows[2].Value),
                StartFrame: (ushort)rows[3].Value, Force: true);
        bool isMerge = rows.Length == 7 && CinematicWordPairList.IsLiteral(rows[2], 0x28)
            && CinematicWordPairList.IsLiteral(rows[3], 0x21) && CinematicWordPairList.IsLiteral(rows[4], 0x21)
            && (CinematicWordPairList.HasSelector(rows[5], 3, 0x3398)
                || CinematicWordPairList.HasSelector(rows[5], 3, 0x3399));
        if (!isMerge) return null;
        return new(key.Time, target, (int)(rows[5].Tag & 0xffff),
            unchecked((int)rows[3].Value), rows[2].Value, unchecked((int)rows[4].Value));
    }
}
