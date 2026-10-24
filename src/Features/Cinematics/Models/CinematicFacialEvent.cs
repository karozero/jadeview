using System.Buffers.Binary;

namespace JadeView;

public sealed record CinematicFacialEvent(float Time, uint ActorTargetKey, bool? AutomaticMouth, float MouthFactor,
    float MouthSpeed, CinematicMorphCommand?[] Commands, bool BlinkRequested = false,
    int? TargetModuleSelector = null)
{
    private const int MessageTypeOffset = 0x30;
    private const int MouthCommandOffset = 0x18;
    private const int MouthSpeedOffset = 0x28;

    public static CinematicFacialEvent? Decode(AnimationKey key)
    {
        foreach (var payload in key.OpaquePayloads)
        {
            if (!TryDecodeMessage(payload, out var message, allowLiteralFlags: true)
                || ReadWord(message, MessageTypeOffset) != 53)
                continue;

            var decoded = Decode(key.Time, ReadWord(message, 0),
                BitConverter.UInt32BitsToSingle(ReadWord(message, 0x08)),
                BitConverter.UInt32BitsToSingle(ReadWord(message, 0x0C)),
                BitConverter.UInt32BitsToSingle(ReadWord(message, MouthCommandOffset)),
                BitConverter.UInt32BitsToSingle(ReadWord(message, 0x20)),
                unchecked((int)ReadWord(message, MouthSpeedOffset)));
            if (decoded != null) return decoded;
        }
        return null;
    }

    internal static CinematicFacialEvent? Decode(CinematicTimelineMessage message)
    {
        if (message.Type != 53) return null;
        return Decode(message.Time, message.Gao, message.Vector1.X, message.Vector1.Y,
            message.Vector2.Y, message.Float1, message.Int1);
    }

    internal static CinematicFacialEvent? Decode(CinematicTimelineMessage message,
        CinematicFacialMessageHandler handler)
    {
        if (message.Type != 53 || handler == CinematicFacialMessageHandler.Unknown
            || handler == CinematicFacialMessageHandler.Sally && message.Int2 == -1)
            return null;

        CinematicMorphCommand?[] commands = handler switch
        {
            CinematicFacialMessageHandler.AutomaticLipSync => [],
            CinematicFacialMessageHandler.Companion =>
            [
                ReadCommand(0, message.Vector1.X, message.Float2),
                ReadCommand(1, message.Vector1.Y, message.Float2),
                ReadCommand(4, message.Vector2.Y, message.Int1)
            ],
            CinematicFacialMessageHandler.Sally =>
            [
                ReadCommand(0, message.Vector1.X, message.Float2),
                ReadCommand(1, message.Vector1.Y, message.Float2),
                ReadCommand(2, message.Vector1.Z, message.Float1),
                ReadCommand(3, message.Vector2.X, message.Int1),
                ReadCommand(4, message.Vector2.Y, message.Int1)
            ],
            _ => []
        };

        float mouthCommand = message.Vector2.Y;
        float mouthSpeed = message.Int1;
        float residual = CommandResidual(mouthCommand);
        bool? automaticMouth = mouthCommand == 0 ? null : residual < 0;
        float mouthFactor = Math.Clamp((MathF.Abs(residual) - .5f) * 10f, 0, 1);
        if (automaticMouth == true && !IsSupportedSpeed(mouthSpeed)) return null;

        bool blinkRequested = handler == CinematicFacialMessageHandler.Sally && message.Int2 != 0;
        int? targetModuleSelector = handler is CinematicFacialMessageHandler.Sally
            or CinematicFacialMessageHandler.Companion ? 0 : null;
        return new(message.Time, message.Gao, automaticMouth, mouthFactor, mouthSpeed, commands,
            blinkRequested, targetModuleSelector);
    }

    private static CinematicFacialEvent? Decode(float time, uint actorTargetKey,
        float firstChannelCommand, float secondChannelCommand, float mouthCommand,
        float sharedSpeed, float mouthSpeed)
    {
        float residual = CommandResidual(mouthCommand);
        bool? automaticMouth = mouthCommand == 0 ? null : residual < 0;
        float mouthFactor = Math.Clamp((MathF.Abs(residual) - .5f) * 10f, 0, 1);
        if (automaticMouth == true && !IsSupportedSpeed(mouthSpeed)) return null;

        var commands = new CinematicMorphCommand?[]
        {
            ReadCommand(0, firstChannelCommand, sharedSpeed),
            ReadCommand(1, secondChannelCommand, sharedSpeed)
        };
        return new(time, actorTargetKey, automaticMouth, mouthFactor, mouthSpeed, commands);
    }

    private static CinematicMorphCommand? ReadCommand(int channel, float packed, float speed)
    {
        if (packed == 0 || !float.IsFinite(packed) || packed < int.MinValue || packed >= 2147483648f
            || !IsSupportedSpeed(speed)) return null;

        int value = (int)Math.Round(packed, MidpointRounding.ToEven);
        int group = Math.Sign(value) * (int)(Math.Abs((long)value) / 1000);
        float residual = (float)value / 1000f - group;
        float factor = Math.Clamp((MathF.Abs(residual) - .5f) * 10f, 0, 1);
        return new(channel, group, factor, speed);
    }

    private static float CommandResidual(float command)
    {
        if (!float.IsFinite(command) || command < int.MinValue || command >= 2147483648f) return 0;
        int value = (int)Math.Round(command, MidpointRounding.ToEven);
        int form = Math.Sign(value) * (int)(Math.Abs((long)value) / 1000);
        return (float)value / 1000f - form;
    }

    private static bool IsSupportedSpeed(float speed) =>
        float.IsFinite(speed) && speed > 0 && speed <= 30;

    internal static bool TryDecodeMessage(AnimationOpaquePayload payload, out byte[] message,
        bool allowLiteralFlags = false)
    {
        message = [];
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length != 19) return false;

        int[] literalRows = [0, 2, 3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16];
        uint[] literalTags = [0x01000028, 0x01000021, 0x01000028, 0x01000028,
            0x01000022, 0x01000022, 0x01000022, 0x01000022, 0x01000022,
            0x01000022, 0x01000022, 0x01000022, 0x01000021, 0x01000021];
        for (int index = 0; index < literalRows.Length; index++)
        {
            uint tag = rows[literalRows[index]].Tag;
            if (allowLiteralFlags) tag &= 0xff00ffff;
            if (tag != literalTags[index]) return false;
        }
        if (!CinematicWordPairList.HasSelector(rows[1], 2, 2054)
            || !CinematicWordPairList.HasSelector(rows[8], 3, 6000)
            || !CinematicWordPairList.HasSelector(rows[12], 3, 6000)
            || !CinematicWordPairList.HasSelector(rows[17], 3, 8056)
            || !CinematicWordPairList.IsEnd(rows[18])) return false;

        int[] messageRows = [3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16, 2];
        message = new byte[messageRows.Length * sizeof(uint)];
        for (int index = 0; index < messageRows.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(index * sizeof(uint)), rows[messageRows[index]].Value);
        return true;
    }

    private static uint ReadWord(byte[] source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset, sizeof(uint)));
}

internal enum CinematicFacialMessageHandler
{
    Unknown,
    AutomaticLipSync,
    Companion,
    Sally
}

public sealed record CinematicMorphCommand(int Channel, int Group, float Factor, float Speed);

public sealed record CinematicChannelFormEvent(float Time, int ModuleSelector, int Channel, int Slot, int Group,
    uint TargetKey = 0)
{
    public static CinematicChannelFormEvent? Decode(AnimationKey key)
    {
        foreach (var payload in key.OpaquePayloads)
        {
            if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length != 8) continue;
            bool shapeMatches = CinematicWordPairList.IsLiteral(rows[0], 0x28)
                && CinematicWordPairList.HasSelector(rows[1], 2, 2054)
                && CinematicWordPairList.IsLiteral(rows[2], 0x21) && CinematicWordPairList.IsLiteral(rows[3], 0x21)
                && CinematicWordPairList.IsLiteral(rows[4], 0x21) && CinematicWordPairList.IsLiteral(rows[5], 0x21)
                && CinematicWordPairList.HasSelector(rows[6], 3, 8263)
                && CinematicWordPairList.IsEnd(rows[7]);
            if (!shapeMatches) continue;

            int moduleSelector = unchecked((int)rows[2].Value);
            uint channel = rows[3].Value;
            uint slot = rows[4].Value;
            uint group = rows[5].Value;
            bool moduleValid = moduleSelector == -1 || moduleSelector is >= 0 and <= byte.MaxValue;
            if (!moduleValid || channel > 15 || slot > 255 || group > 65535) continue;
            return new(key.Time, moduleSelector, (int)channel, (int)slot, (int)group, rows[0].Value);
        }
        return null;
    }
}

public sealed record CinematicMorphFactorCall(uint TargetKey, int ModuleSelector, int Channel)
{
    public static CinematicMorphFactorCall? Decode(AnimationKey key)
    {
        foreach (var payload in key.OpaquePayloads)
        {
            if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length != 7) continue;
            bool callMatches = CinematicWordPairList.IsLiteral(rows[0], 0x28)
                && CinematicWordPairList.HasSelector(rows[1], 2, 2054)
                && CinematicWordPairList.IsLiteral(rows[2], 0x28)
                && CinematicWordPairList.IsLiteral(rows[3], 0x21)
                && CinematicWordPairList.IsLiteral(rows[4], 0x21)
                && CinematicWordPairList.HasSelector(rows[5], 3, 8262)
                && CinematicWordPairList.IsEnd(rows[6]);
            if (!callMatches || rows[2].Value == 0) continue;

            int moduleSelector = unchecked((int)rows[3].Value);
            int channel = unchecked((int)rows[4].Value);
            bool moduleValid = moduleSelector == -1 || moduleSelector is >= 0 and <= byte.MaxValue;
            bool channelValid = channel is >= 0 and <= 15;
            if (moduleValid && channelValid)
                return new(rows[2].Value, moduleSelector, channel);
        }
        return null;
    }
}

internal static class CinematicWordPairList
{
    internal static bool TryRead(AnimationOpaquePayload payload, out (uint Value, uint Tag)[] rows)
    {
        rows = [];
        if (payload.Kind != "word-pair-list" || payload.Data.Length < sizeof(uint)) return false;
        uint rowCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Data);
        if (rowCount > int.MaxValue || payload.Data.Length != sizeof(uint) + rowCount * 8) return false;
        rows = new (uint Value, uint Tag)[(int)rowCount];
        for (int index = 0; index < rows.Length; index++)
        {
            int offset = sizeof(uint) + index * 8;
            rows[index] = (BinaryPrimitives.ReadUInt32LittleEndian(payload.Data.AsSpan(offset)),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.Data.AsSpan(offset + sizeof(uint))));
        }
        return true;
    }

    internal static bool HasSelector((uint Value, uint Tag) row, uint category, uint selector) =>
        row.Value == 0 && row.Tag >> 24 == category && (row.Tag & 0xffff) == selector;

    // The middle tag byte is retained compiler metadata. END has no value operand;
    // retail streams contain nonzero unused bytes in both fields of that marker.
    internal static bool IsLiteral((uint Value, uint Tag) row, uint type) =>
        row.Tag >> 24 == 1 && (row.Tag & 0xffff) == type;

    internal static bool IsEnd((uint Value, uint Tag) row) => row.Tag >> 24 == 31;
}
