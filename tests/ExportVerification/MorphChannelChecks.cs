using JadeView;

internal static class MorphChannelChecks
{
    internal static void Verify()
    {
        Check(.5f, [1, 2], [0, .5f, 0]);
        Check(1, [1, 2], [0, 1, 0]);
        Check(1.5f, [1, 2], [0, .5f, .5f]);
        Check(2, [1, 2], [0, 0, 1]);
        Check(20, [1, 2], [0, 0, 1]);
        Check(2, [1, uint.MaxValue, 2], [0, .5f, .5f]);
        Check(1, [uint.MaxValue, 2], [0, 0, .5f]);
        Check(2, [1, uint.MaxValue], [0, 1, 0]);
        Check(1, [uint.MaxValue, 99], [0, 0, 0]);
        Check(1.5f, [1, 1], [0, 1, 0]);
        Check(0, [1, 2], [0, 0, 0]);
        var sum = FaceMorphChannelEvaluator.Evaluate(3,
            [Channel(.5f, 1, [1]), Channel(-.25f, 1, [1])]);
        Require(sum[1] == .25f, "Channels must accumulate without clamping signed source weights");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, 2147483648f })
        {
            try { FaceMorphChannelEvaluator.Evaluate(3, [Channel(1, invalid, [1])]); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid morph progression was accepted");
        }
        Console.WriteLine("PASS: PC morph channel progression, adjacent interpolation, holes, repeated groups, endpoints and accumulation");
    }

    private static void Check(float progression, uint[] references, float[] expected)
    {
        var actual = FaceMorphChannelEvaluator.Evaluate(expected.Length, [Channel(1, progression, references)]);
        Require(actual.Zip(expected).All(pair => MathF.Abs(pair.First - pair.Second) < .000001f),
            $"Unexpected channel weights at progression {progression}");
    }

    private static FaceMorphChannel Channel(float weight, float progression, uint[] references) =>
        new(BitConverter.SingleToUInt32Bits(weight), BitConverter.SingleToUInt32Bits(progression), references);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
