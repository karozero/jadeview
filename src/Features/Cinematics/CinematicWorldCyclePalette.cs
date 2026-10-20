using System.Numerics;

namespace JadeView;

internal static class CinematicWorldCyclePalette
{
    internal static CinematicWorldCycleState Evaluate(
        IEnumerable<CinematicEnvironmentController> controllers, WorldData world, float attackFactor = 0)
    {
        var constants = new Dictionary<(uint ObjectKey, int SubMaterial), Vector4>();
        var localAlphas = new Dictionary<(uint ObjectKey, int SubMaterial, int Layer), byte>();
        Vector4 rliColor = Vector4.Zero;
        float rliFactor = 0;
        foreach (var controller in controllers)
        {
            if (controller.NormalPalettes.Length != 4 || controller.NormalPalettes.Any(palette => palette.Length < 9)) continue;
            var sun = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == controller.SunKey);
            if (sun == null) continue;

            float sunHeight = sun.PreviewMatrix?.M43 ?? sun.Transform.Translation.Z;
            var colors = Sample(controller, sunHeight, attackFactor);
            rliColor = ToVector4(colors[7]);
            rliFactor = SampleRliFactor(controller, sunHeight);
            AddConstant(controller.EnvironmentKey, 0, colors[3]);
            AddConstant(controller.EnvironmentKey, 1, colors[8]);
            AddConstant(controller.SkyKey, 0, colors[4]);
            AddEnvironmentAlpha(controller, controller.EnvironmentKey, sunHeight);
        }
        return new(constants, localAlphas, rliColor, rliFactor);

        void AddConstant(uint objectKey, int subMaterial, uint color)
        {
            if (objectKey is > 2 and < uint.MaxValue)
                constants[(objectKey, subMaterial)] = ToVector4(color);
        }

        void AddEnvironmentAlpha(CinematicEnvironmentController controller, uint objectKey, float height)
        {
            if (objectKey is not (> 2 and < uint.MaxValue)) return;

            // world_cycle calls SetLocalAlpha(env, submaterial 0, texture level 1).
            // The night overlay starts at 127/255, fades to full opacity at dawn,
            // and remains fully opaque for the rest of the day cycle.
            byte alpha = height < controller.NightHeight
                ? (byte)127
                : height < controller.DawnHeight
                    ? (byte)Math.Clamp(127 + (int)(127 * Ratio(height, controller.NightHeight, controller.DawnHeight)), 127, 254)
                    : (byte)255;
            localAlphas[(objectKey, 0, 1)] = alpha;
        }
    }

    private static uint[] Sample(CinematicEnvironmentController controller, float sunHeight, float attackFactor)
    {
        var palettes = controller.NormalPalettes;
        uint[] normal;
        uint[] attack;
        if (sunHeight < controller.NightHeight)
        {
            normal = palettes[0];
            attack = AttackPalette(controller, 0, normal);
        }
        else if (sunHeight < controller.DawnHeight)
        {
            float amount = Ratio(sunHeight, controller.NightHeight, controller.DawnHeight);
            normal = Blend(palettes[0], palettes[1], amount);
            attack = Blend(AttackPalette(controller, 0, palettes[0]), AttackPalette(controller, 1, palettes[1]), amount);
        }
        else if (sunHeight < controller.SunriseHeight)
        {
            float amount = Ratio(sunHeight, controller.DawnHeight, controller.SunriseHeight);
            normal = Blend(palettes[1], palettes[2], amount);
            attack = Blend(AttackPalette(controller, 1, palettes[1]), AttackPalette(controller, 2, palettes[2]), amount);
        }
        else if (sunHeight <= controller.DayHeight)
        {
            float amount = Ratio(sunHeight, controller.SunriseHeight, controller.DayHeight);
            normal = Blend(palettes[2], palettes[3], amount);
            attack = Blend(AttackPalette(controller, 2, palettes[2]), AttackPalette(controller, 3, palettes[3]), amount);
        }
        else
        {
            normal = palettes[3];
            attack = AttackPalette(controller, 3, palettes[3]);
        }
        return attackFactor <= 0 ? normal : Blend(normal, attack, Math.Clamp(attackFactor, 0, 1));
    }

    private static uint[] AttackPalette(CinematicEnvironmentController controller, int index, uint[] fallback) =>
        controller.AttackPalettes.ElementAtOrDefault(index) is { Length: > 0 } palette ? palette : fallback;

    private static float SampleRliFactor(CinematicEnvironmentController controller, float sunHeight)
    {
        if (sunHeight < controller.NightHeight) return controller.NightRliFactor;
        if (sunHeight < controller.DawnHeight) return controller.DawnRliFactor;
        if (sunHeight < controller.SunriseHeight) return controller.SunsetRliFactor;
        if (sunHeight <= controller.DayHeight) return controller.DayRliFactor;
        return controller.SunsetRliFactor;
    }

    private static float Ratio(float value, float low, float high) =>
        high == low ? 1 : (value - low) / (high - low);

    private static uint[] Blend(uint[] from, uint[] to, float amount)
    {
        var result = new uint[Math.Min(from.Length, to.Length)];
        for (int index = 0; index < result.Length; index++)
            result[index] = InterpolatePackedColor(from[index], to[index], amount);
        return result;
    }

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

internal sealed record CinematicWorldCycleState(
    IReadOnlyDictionary<(uint ObjectKey, int SubMaterial), Vector4> Constants,
    IReadOnlyDictionary<(uint ObjectKey, int SubMaterial, int Layer), byte> LocalAlphas,
    Vector4 RliColor,
    float RliFactor);
