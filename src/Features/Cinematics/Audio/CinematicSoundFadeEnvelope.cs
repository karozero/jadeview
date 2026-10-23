namespace JadeView;

internal sealed class CinematicSoundFadeEnvelope(float minimumTime, float maximumTime,
    float minimumPercent, float maximumPercent, float[] keyData)
{
    private const uint InsertFileId = 0x696e7374;
    private const uint FadeFlag = 1;
    private const int MaximumKeyCount = 4096;

    internal float Duration => maximumTime;

    internal float Evaluate(float elapsedSeconds)
    {
        if (elapsedSeconds <= minimumTime) return ToFactor(minimumPercent);
        if (elapsedSeconds >= maximumTime) return ToFactor(maximumPercent);
        float normalizedTime = (elapsedSeconds - minimumTime) / (maximumTime - minimumTime);
        float normalizedValue = EvaluateKeyCurve(normalizedTime);
        float percentage = minimumPercent + (maximumPercent - minimumPercent) * normalizedValue;
        return ToFactor(percentage);
    }

    private float EvaluateKeyCurve(float normalizedTime)
    {
        float previousTime = 0;
        float previousValue = keyData[0];
        float currentTime = 0;
        float currentValue = 0;

        int keyCount = keyData.Length / 2;
        for (int keyIndex = 1; keyIndex < keyCount - 1; keyIndex++)
        {
            int keyOffset = keyIndex * 2;
            currentTime = keyData[keyOffset];
            currentValue = previousValue
                + (currentTime - previousTime) * keyData[keyOffset - 1];
            if (previousTime <= normalizedTime && normalizedTime <= currentTime)
                return Interpolate(previousValue, currentValue,
                    (normalizedTime - previousTime) / (currentTime - previousTime));

            previousTime = currentTime;
            previousValue = currentValue;
        }

        currentTime = 1;
        currentValue = keyData[^2];
        if (previousTime > normalizedTime || normalizedTime > currentTime)
            return 1;
        if (currentTime == previousTime) return currentValue;
        return Interpolate(previousValue, currentValue,
            (normalizedTime - previousTime) / (currentTime - previousTime));
    }

    private static float Interpolate(float start, float end, float factor) => start + (end - start) * factor;
    private static float ToFactor(float percentage) => Math.Clamp(percentage * .01f, 0, 1);

    internal static CinematicSoundFadeEnvelope? Read(byte[] bytes)
    {
        if (bytes.Length < 16 || Preview.U32(bytes, 0) != InsertFileId
            || Preview.U32(bytes, 8) != bytes.Length - 12
            || (Preview.U32(bytes, 12) & FadeFlag) == 0)
            return null;

        float minimumTime = float.NaN;
        float maximumTime = float.NaN;
        float minimumPercent = float.NaN;
        float maximumPercent = float.NaN;
        float[]? keyData = null;
        int position = 16;
        while (position < bytes.Length)
        {
            if (position > bytes.Length - 8) return null;
            uint chunkType = Preview.U32(bytes, position);
            uint chunkSize = Preview.U32(bytes, position + 4);
            if (chunkSize < 8 || chunkSize > bytes.Length - position) return null;

            int chunkEnd = position + (int)chunkSize;
            switch (chunkType)
            {
                case 1 when chunkSize == 16:
                    minimumTime = ReadFiniteFloat(bytes, position + 8);
                    maximumTime = ReadFiniteFloat(bytes, position + 12);
                    break;
                case 3 when chunkSize == 20:
                    minimumPercent = ReadFiniteFloat(bytes, position + 8);
                    maximumPercent = ReadFiniteFloat(bytes, position + 12);
                    break;
                case 6:
                    keyData = ReadKeyData(bytes, position, chunkSize);
                    if (keyData == null) return null;
                    break;
            }
            position = chunkEnd;
        }

        if (position != bytes.Length || keyData == null
            || !float.IsFinite(minimumTime) || !float.IsFinite(maximumTime)
            || !float.IsFinite(minimumPercent) || !float.IsFinite(maximumPercent)
            || minimumTime < 0 || maximumTime <= minimumTime)
            return null;

        return new(minimumTime, maximumTime, minimumPercent, maximumPercent, keyData);
    }

    private static float[]? ReadKeyData(byte[] bytes, int position, uint chunkSize)
    {
        if (chunkSize < 12) return null;
        uint keyCount = Preview.U32(bytes, position + 8);
        if (keyCount is < 2 or > MaximumKeyCount || chunkSize != 12 + keyCount * 8)
            return null;

        int count = checked((int)keyCount * 2);
        var keyData = new float[count];
        for (int index = 0; index < count; index++)
        {
            float keyValue = ReadFiniteFloat(bytes, position + 12 + index * 4);
            if (!float.IsFinite(keyValue)) return null;
            keyData[index] = keyValue;
        }
        return keyData;
    }

    private static float ReadFiniteFloat(byte[] bytes, int offset)
    {
        float value = BitConverter.ToSingle(bytes, offset);
        return float.IsFinite(value) ? value : float.NaN;
    }
}
