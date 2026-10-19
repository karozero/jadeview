namespace JadeView;
internal sealed partial class PcSceneLoader
{
    private void ReadAnimation(int offset, int end)
    {
        int position = offset + 4;
        var compression = new AnimationCompressionState();
        for (int channelIndex = 0; channelIndex < ReadUInt16(offset); channelIndex++)
            ReadAnimationChannel(ref position, compression);
        if (position != end)
            throw new InvalidDataException($"Event layout mismatch at {offset:X8}: {position:X8}/{end:X8}.");
    }

    private void ReadAnimationChannel(ref int position, AnimationCompressionState compression)
    {
        int channelStart = position;
        int flags = ReadUInt16(position);
        int mode = flags & 0x3f00;
        position += 8;
        uint channelTarget = ReadChannelTarget(ref position, flags, mode);
        int frameCount = ReadInt32(position);
        position += 4;
        int firstFlags = 0;
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            ReadAnimationFrame(ref position, mode, frameIndex, ref firstFlags, compression);
        var range = new SourceRange(channelStart, position - channelStart);
        currentRecord?.AnimationChannels.Add((channelTarget, range));
    }

    private uint ReadChannelTarget(ref int position, int flags, int mode)
    {
        bool hasNamedTarget = (flags & 0x8000) != 0;
        if (hasNamedTarget)
        {
            if (mode == 0)
                position += 4;
            return uint.MaxValue;
        }

        uint target = ReadUInt32(position);
        EnqueueReferenceAt(position, 'l');
        position += 4;
        return target;
    }

    private void ReadAnimationFrame(ref int position, int mode, int frameIndex, ref int firstFlags, AnimationCompressionState compression)
    {
        position += (mode & 512) != 0 ? 1 : 2;
        bool reusesFirstFlags = (mode & 2048) != 0 && frameIndex > 0;
        int frameFlags = reusesFirstFlags ? firstFlags & ~4 : ReadUInt16(position);
        if (!reusesFirstFlags)
        {
            position += 2;
            if (frameIndex == 0)
                firstFlags = frameFlags;
        }

        frameFlags &= ~2;
        int eventKind = frameFlags & 1984;
        if (eventKind == 64)
        {
            int entryCount = ReadInt32(position);
            position = checked(position + 4 + entryCount * 8);
            return;
        }

        if (eventKind != 128)
            return;
        if ((frameFlags & 32) != 0)
        {
            position += 4;
            return;
        }

        int initialControls = (mode & 8192) != 0 ? 256 : 0;
        int controls = frameIndex > 0 ? mode : initialControls;
        ReadAnimationEvent(ref position, controls, compression);
    }

    private void ReadAnimationEvent(ref int position, int controls, AnimationCompressionState compression)
    {
        int start = position;
        bool reusesSize = (controls & 8192) != 0;
        bool reusesFormat = (controls & 4096) != 0;
        int payloadSize = reusesSize ? compression.Size / 2 : ReadUInt16(position);
        if (!reusesSize)
            position += 2;
        if (reusesFormat && !reusesSize)
            throw new InvalidDataException("Invalid event compression.");
        int format = reusesFormat ? compression.Format : ReadUInt16(position);
        if (!reusesFormat)
            position += 2;
        bool hasPackedSize = (controls & 8448) != 0;
        if ((format & 1920) != 0)
        {
            position += (format & 128) != 0 ? 6 : 4;
            compression.Size = hasPackedSize ? 41 : 20;
            compression.Format = format;
            return;
        }

        if ((format & 63488) != 0)
        {
            position += (format & 63488) switch
            {
                16384 or 32768 or 8192 => 4,
                4096 or 2048 => 6,
                _ => 0
            };
            compression.Size = hasPackedSize ? 33 : 16;
            compression.Format = format;
            return;
        }

        if ((format & 23) == 0)
            return;
        compression.Size = hasPackedSize ? (payloadSize * 2 | 1) : payloadSize;
        compression.Format = format;
        int targetSize = compression.Size - (reusesSize ? 2 : 0) - (reusesFormat ? 2 : 0);
        position += TransformPayloadSize(format);
        bool hasExtraControls = (format & 8) != 0 && position - start < targetSize;
        if (!hasExtraControls)
            return;
        int controlCount = ReadInt32(position);
        position = checked(position + 4 + (2 * controlCount - 1) * 4);
    }

    private static int TransformPayloadSize(int format)
    {
        int vectorFormat = format & 3;
        if (vectorFormat != 0)
            return vectorFormat switch
            {
                3 => 36,
                2 => 24,
                1 => 12,
                _ => 0
            };
        return (format & 20) switch
        {
            4 => 68,
            16 => 16,
            _ => 0
        };
    }
}
