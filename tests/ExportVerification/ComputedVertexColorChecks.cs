using System.Numerics;

namespace JadeView;

internal static class ComputedVertexColorChecks
{
    internal static void VerifyPackedRliAndAmbientEvaluation()
    {
        VerifyRliFactorBoundaries();
        VerifyPackedSubtractionAndAmbientFlags();
        VerifyRawColorIsPreserved();
    }

    private static void VerifyRliFactorBoundaries()
    {
        const uint raw = 0x80402010;
        Vector4 destination = new(200 / 255f, 120 / 255f, 40 / 255f, 64 / 255f);

        Check(Evaluate(raw, 0x40, destination, .02f).ComputedPackedColor == raw,
            "RLI factor 0.02 must skip destination subtraction at the lower strict boundary.");
        Check(Evaluate(raw, 0x40, destination, .98f).ComputedPackedColor == 0x40180000,
            "RLI factor 0.98 must use the full packed destination and saturate each channel.");

        // At 0.5 the 49152-biased float has the 0x80 table bit set, selecting the
        // exact masked half-channel term used in JadeVertexShader.
        var half = Evaluate(raw, 0x40, new Vector4(200 / 255f, 100 / 255f, 50 / 255f, 32 / 255f), .5f);
        Check(half.ComputedPackedColor == 0x70270000,
            $"The 49152-bit RLI interpolation changed: expected 70270000, got {half.ComputedPackedColor:X8}.");
    }

    private static void VerifyPackedSubtractionAndAmbientFlags()
    {
        const uint raw = 0xFF804020;
        Vector4 destination = new(8 / 255f, 16 / 255f, 32 / 255f, 255 / 255f);
        Vector4 ambient = new(10 / 255f, 20 / 255f, 30 / 255f, 1f);

        var ambientResult = Evaluate(raw, 0x40 | 0x100, destination, 1f, ambient);
        Check(ambientResult.ComputedPackedColor == 0x007e4422,
            $"RLI subtraction should saturate each packed channel then add ambient RGB; got {ambientResult.ComputedPackedColor:X8}.");
        Check(ambientResult.UsedRli && ambientResult.AppliedAmbient,
            "The result should report the enabled RLI and ambient inputs.");

        var noRli = Evaluate(raw, 0x100, destination, 1f, ambient);
        Check(noRli.ComputedPackedColor == 0x001e140a,
            "UseRLI off should clear source paint before ambient RGB is applied.");

        var noAmbient = Evaluate(raw, 0x40, destination, 1f, ambient);
        Check(noAmbient.ComputedPackedColor == 0x00603018,
            "Ambient must not be applied when UseAmbient is clear.");

        var dontScale = Evaluate(raw, 0x40 | 0x02000000 | 0x100, destination, 1f, ambient);
        Check(dontScale.ComputedPackedColor == 0xFF9e542a,
            "DontScaleRLI must preserve raw RLI subtraction input while still allowing ambient RGB.");

        var alphaProbe = Evaluate(0x7f804020, 0x40 | 0x02000000 | 0x100,
            Vector4.One, 1f, new Vector4(.1f, .1f, .1f, 1f));
        Check((alphaProbe.ComputedPackedColor >> 24) == 0x7f,
            "Ambient addition must preserve alpha; without RLI subtraction it is exactly the raw alpha.");
    }

    private static void VerifyRawColorIsPreserved()
    {
        const uint raw = 0x7f112233;
        var input = new ComputedVertexColorInput(raw, 0x40 | 0x100,
            new Vector4(.5f, .5f, .5f, .5f), .75f, new Vector4(.1f, .2f, .3f, .9f));
        var first = ComputedVertexColorEvaluator.Evaluate(input);
        var second = ComputedVertexColorEvaluator.Evaluate(input);

        Check(first.RawPackedRli == raw && second.RawPackedRli == raw,
            "Computed evaluation must retain the original packed RLI independently from its output.");
        Check(first.ComputedPackedColor == second.ComputedPackedColor,
            "Repeated evaluation must be pure and deterministic.");
        Check((first.ComputedPackedColor >> 24) <= (raw >> 24),
            "Ambient RGB must not alter alpha; only the explicit RLI destination subtraction may do so.");
    }

    private static ComputedVertexColorResult Evaluate(uint raw, uint flags, Vector4 destination,
        float factor, Vector4 ambient = default) =>
        ComputedVertexColorEvaluator.Evaluate(new(raw, flags, destination, factor, ambient));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
