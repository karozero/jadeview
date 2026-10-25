using System.Numerics;
using JadeView;

internal static class CinematicRliDrawStateChecks
{
    private const uint DontScaleRli = 0x02000000;

    internal static void Verify()
    {
        var sceneColor = new Vector4(.2f, .3f, .4f, 1);
        CinematicRliDrawState fallback = CinematicRliDrawState.Resolve(
            sceneColor, .75f, objectOverride: null);
        Check(fallback.DestinationColor == sceneColor && fallback.Factor == .75f
            && !fallback.FactorIsUsed,
            "An object without a draw override must inherit scene RLI without changing its usage flag.");

        var objectColor = new Vector4(1, 1, 1, 1);
        var objectOverride = new CinematicRliDrawState(objectColor, .35f, FactorIsUsed: true);
        CinematicRliDrawState resolved = CinematicRliDrawState.Resolve(
            sceneColor, .75f, objectOverride);
        Check(resolved == objectOverride,
            "A per-object callback must override only that object's RLI inputs.");

        uint originalFlags = 0xA5A50000;
        uint usedFlags = CinematicRliDrawState.ApplyRliFactorUsage(
            originalFlags | DontScaleRli, resolved.FactorIsUsed);
        Check((usedFlags & DontScaleRli) == 0,
            "A callback that enables the RLI factor must clear DontScaleRLI.");

        uint unusedFlags = CinematicRliDrawState.ApplyRliFactorUsage(
            originalFlags, factorIsUsed: false);
        Check((unusedFlags & DontScaleRli) != 0,
            "A callback that disables the RLI factor must set DontScaleRLI.");

        Console.WriteLine("PASS: per-object cinematic RLI overrides and factor-use flags");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
