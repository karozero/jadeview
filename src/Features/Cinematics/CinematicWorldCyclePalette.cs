using System.Numerics;

namespace JadeView;

internal static class CinematicWorldCyclePalette
{
    private const int PaletteColorCount = 9;

    internal static CinematicWorldCycleState Evaluate(
        IReadOnlyList<CinematicEnvironmentController> controllers, WorldData world,
        Dictionary<(uint ObjectKey, int SubMaterial), Vector4> constants,
        Dictionary<(uint ObjectKey, int SubMaterial, int Layer), byte> localAlphas,
        float attackFactor = 0)
    {
        constants.Clear();
        localAlphas.Clear();
        Vector4 rliColor = Vector4.Zero;
        float rliFactor = 0;
        Vector4 fogColor = Vector4.Zero;
        float fogNear = 0;
        float fogFar = 0;
        Span<uint> colors = stackalloc uint[PaletteColorCount];

        for (int controllerIndex = 0; controllerIndex < controllers.Count; controllerIndex++)
        {
            var controller = controllers[controllerIndex];
            if (!HasCompletePalettes(controller.NormalPalettes)) continue;
            SceneObject? sun = FindObject(world.Objects, controller.SunKey);
            if (sun == null) continue;

            float sunHeight = sun.PreviewMatrix?.M43 ?? sun.Transform.Translation.Z;
            Sample(controller, sunHeight, attackFactor, colors);
            rliColor = ToVector4(colors[7]);
            rliFactor = SampleRliFactor(controller, sunHeight);
            fogColor = ToVector4(colors[1]);
            float attack = Math.Clamp(attackFactor, 0, 1);
            fogNear = Lerp(controller.FogNear, controller.AttackFogNear, attack);
            fogFar = Lerp(controller.FogFar, controller.AttackFogFar, attack);

            if (IsObjectKey(controller.EnvironmentKey))
            {
                constants[(controller.EnvironmentKey, 0)] = ToVector4(colors[3]);
                constants[(controller.EnvironmentKey, 1)] = ToVector4(colors[8]);
                localAlphas[(controller.EnvironmentKey, 0, 1)] = SampleEnvironmentAlpha(controller, sunHeight);
            }
            if (IsObjectKey(controller.SkyKey))
                constants[(controller.SkyKey, 0)] = ToVector4(colors[4]);
        }

        return new(rliColor, rliFactor, fogColor, fogNear, fogFar);
    }

    private static bool HasCompletePalettes(uint[][] palettes)
    {
        if (palettes.Length != 4) return false;
        for (int index = 0; index < palettes.Length; index++)
            if (palettes[index].Length < PaletteColorCount) return false;
        return true;
    }

    private static SceneObject? FindObject(IReadOnlyList<SceneObject> objects, uint key)
    {
        for (int index = 0; index < objects.Count; index++)
            if (objects[index].GaoKey == key) return objects[index];
        return null;
    }

    private static void Sample(CinematicEnvironmentController controller, float sunHeight,
        float attackFactor, Span<uint> result)
    {
        int firstIndex;
        int secondIndex;
        float paletteBlend;
        if (sunHeight < controller.NightHeight)
        {
            firstIndex = 0;
            secondIndex = 0;
            paletteBlend = 0;
        }
        else if (sunHeight < controller.DawnHeight)
        {
            firstIndex = 0;
            secondIndex = 1;
            paletteBlend = Ratio(sunHeight, controller.NightHeight, controller.DawnHeight);
        }
        else if (sunHeight < controller.SunriseHeight)
        {
            firstIndex = 1;
            secondIndex = 2;
            paletteBlend = Ratio(sunHeight, controller.DawnHeight, controller.SunriseHeight);
        }
        else if (sunHeight <= controller.DayHeight)
        {
            firstIndex = 2;
            secondIndex = 3;
            paletteBlend = Ratio(sunHeight, controller.SunriseHeight, controller.DayHeight);
        }
        else
        {
            firstIndex = 3;
            secondIndex = 3;
            paletteBlend = 0;
        }

        uint[] normalFrom = controller.NormalPalettes[firstIndex];
        uint[] normalTo = controller.NormalPalettes[secondIndex];
        uint[] attackFrom = AttackPalette(controller.AttackPalettes, firstIndex, normalFrom);
        uint[] attackTo = AttackPalette(controller.AttackPalettes, secondIndex, normalTo);
        float attackBlend = Math.Clamp(attackFactor, 0, 1);
        for (int index = 0; index < result.Length; index++)
        {
            uint normal = InterpolatePackedColor(normalFrom[index], normalTo[index], paletteBlend);
            if (attackBlend <= 0)
            {
                result[index] = normal;
                continue;
            }

            uint attack = InterpolatePackedColor(attackFrom[index], attackTo[index], paletteBlend);
            result[index] = InterpolatePackedColor(normal, attack, attackBlend);
        }
    }

    private static uint[] AttackPalette(uint[][] palettes, int index, uint[] fallback) =>
        index < palettes.Length && palettes[index].Length > 0 ? palettes[index] : fallback;

    private static float SampleRliFactor(CinematicEnvironmentController controller, float sunHeight)
    {
        if (sunHeight < controller.NightHeight) return controller.NightRliFactor;
        if (sunHeight < controller.DawnHeight) return controller.DawnRliFactor;
        if (sunHeight < controller.SunriseHeight) return controller.SunsetRliFactor;
        if (sunHeight <= controller.DayHeight) return controller.DayRliFactor;
        return controller.SunsetRliFactor;
    }

    private static byte SampleEnvironmentAlpha(CinematicEnvironmentController controller, float height)
    {
        // world_cycle calls SetLocalAlpha(env, submaterial 0, texture level 1).
        // The night overlay starts at 127/255, fades to full opacity at dawn,
        // and remains fully opaque for the rest of the day cycle.
        return height < controller.NightHeight
            ? (byte)127
            : height < controller.DawnHeight
                ? (byte)Math.Clamp(127 + (int)(127 * Ratio(height, controller.NightHeight, controller.DawnHeight)), 127, 254)
                : (byte)255;
    }

    private static bool IsObjectKey(uint key) => key is > 2 and < uint.MaxValue;

    private static float Ratio(float value, float low, float high) =>
        high == low ? 1 : (value - low) / (high - low);

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    // Mirrors Iris' packed-color interpolation, including its endpoint tolerance and 7-bit blend.
    private static uint InterpolatePackedColor(uint from, uint to, float amount)
    {
        if (amount >= .98f) return to;
        if (amount <= .02f) return from;
        int fraction = BitConverter.SingleToInt32Bits(amount + 49152f);
        uint value = ((fraction & 0x80) != 0 ? to : from) & 0xfefefefe;
        value >>= 1;
        value += (((fraction & 0x40) != 0 ? to : from) & 0xfcfcfcfc) >> 2;
        value += (((fraction & 0x20) != 0 ? to : from) & 0xf8f8f8f8) >> 3;
        value += (((fraction & 0x10) != 0 ? to : from) & 0xf0f0f0f0) >> 4;
        value += (((fraction & 0x08) != 0 ? to : from) & 0xe0e0e0e0) >> 5;
        value += (((fraction & 0x04) != 0 ? to : from) & 0xc0c0c0c0) >> 6;
        value += (((fraction & 0x02) != 0 ? to : from) & 0x80808080) >> 7;
        return value;
    }

    private static Vector4 ToVector4(uint color) => new(
        (color & 0xff) / 255f,
        (color >> 8 & 0xff) / 255f,
        (color >> 16 & 0xff) / 255f,
        (color >> 24) / 255f);
}

internal readonly record struct CinematicWorldCycleState(
    Vector4 RliColor,
    float RliFactor,
    Vector4 FogColor,
    float FogNear,
    float FogFar);
