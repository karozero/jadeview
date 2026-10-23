using System.Numerics;

namespace JadeView;

internal sealed class CinematicAfterEffectTimeline
{
    private const int SetMaster = 15600;
    private const int OffsetEffectMode = 8;
    private const int ColorBalanceMode = 9;
    private readonly (float Time, int Mode, float Value)[] parameterChanges;

    internal float OffsetEffect { get; private set; } = .5f;
    internal float ColorBalance { get; private set; } = .5f;

    internal CinematicAfterEffectTimeline(AnimationClip clip)
    {
        parameterChanges = CinematicLiteralCall.ReadTimeline(clip)
            .Where(item => item.Call.Selector == SetMaster
                && item.Call.Arguments.Length == 4
                && item.Call.Arguments[0] is OffsetEffectMode or ColorBalanceMode)
            .Select(item => (Time: item.Time, Mode: (int)item.Call.Arguments[0],
                Value: BitConverter.UInt32BitsToSingle(item.Call.Arguments[3])))
            .Where(change => float.IsFinite(change.Time) && float.IsFinite(change.Value))
            .OrderBy(change => change.Time)
            .ToArray();
    }

    internal void Evaluate(float time)
    {
        OffsetEffect = .5f;
        ColorBalance = .5f;
        foreach (var change in parameterChanges)
        {
            if (change.Time > time) break;
            if (change.Mode == OffsetEffectMode) OffsetEffect = change.Value;
            else if (change.Mode == ColorBalanceMode) ColorBalance = change.Value;
        }
    }

    internal static Vector3 ApplyOffsetEffect(Vector3 source, float value)
    {
        float amount = MathF.Floor(Math.Abs(value - .5f) * 255) / 255;
        Vector3 offset = new(amount);
        Vector3 result = value >= .5f ? source + offset : source - offset;
        return Vector3.Clamp(result, Vector3.Zero, Vector3.One);
    }

    internal static Vector3 ApplyColorBalance(Vector3 source, float value)
    {
        float balance = Math.Clamp(value, 0, 1);
        if (balance < .5f)
        {
            float diffuseAlpha = MathF.Floor(balance * 510) / 255;
            float grayWeight = 1 - diffuseAlpha;
            return Vector3.Lerp(source, new Vector3(128 / 255f), grayWeight);
        }

        float modulation = MathF.Floor((balance * 2 - 1) * 255) / 255;
        float centerOffset = MathF.Floor((balance * 2 - 1) * 128) / 255;
        return Vector3.Clamp(source + source * modulation - new Vector3(centerOffset), Vector3.Zero, Vector3.One);
    }
}
