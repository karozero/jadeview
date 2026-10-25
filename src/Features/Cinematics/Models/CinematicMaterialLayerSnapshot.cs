using System.Numerics;

namespace JadeView;

/// <summary>A frozen playhead sample of material layers, constants, and color state.</summary>
internal sealed class CinematicMaterialLayerSnapshot(
    float time,
    IReadOnlyDictionary<(uint Material, int Level), JadeMaterialLayer> layers,
    IReadOnlyDictionary<(uint ObjectKey, int SubMaterial, int Level), byte> alphas,
    IReadOnlyDictionary<(uint ObjectKey, int SubMaterial), Vector4> constants,
    IReadOnlyDictionary<uint, Vector4> materialConstants,
    IReadOnlyDictionary<uint, float> shockwaveTransparency)
{
    internal CinematicMaterialLayerSnapshot(float time,
        IReadOnlyDictionary<(uint Material, int Level), JadeMaterialLayer> layers,
        IReadOnlyDictionary<(uint ObjectKey, int SubMaterial, int Level), byte> alphas,
        IReadOnlyDictionary<(uint ObjectKey, int SubMaterial), Vector4> constants,
        IReadOnlyDictionary<uint, float> shockwaveTransparency)
        : this(time, layers, alphas, constants, new Dictionary<uint, Vector4>(), shockwaveTransparency) { }

    internal float Time { get; } = time;
    internal Vector4 RliColor { get; init; }
    internal float RliFactor { get; init; }
    internal Vector4 AmbientColor { get; init; }
    internal Vector4 AmbientColor2 { get; init; }
    internal bool HasAmbient { get; init; }

    internal bool TryGetMaterialConstant(uint materialKey, out Vector4 color) =>
        materialConstants.TryGetValue(materialKey, out color);

    internal Vector4 ApplyConstant(uint objectKey, int subMaterial, Vector4 original)
        => ApplyConstant(0, objectKey, subMaterial, original);

    internal Vector4 ApplyConstant(uint materialKey, uint objectKey, int subMaterial, Vector4 original)
    {
        Vector4 constant = materialConstants.GetValueOrDefault(materialKey,
            constants.GetValueOrDefault((objectKey, subMaterial), original));
        if (subMaterial is 0 or 1
            && shockwaveTransparency.TryGetValue(objectKey, out float transparency))
            constant = Vector4.Lerp(constant, Vector4.Zero, transparency);
        return constant;
    }

    internal JadeMaterialLayer Apply(uint materialKey, uint objectKey, int subMaterial,
        int level, JadeMaterialLayer original)
    {
        var layer = layers.GetValueOrDefault((materialKey, level)) ?? original;
        if (!alphas.TryGetValue((objectKey, subMaterial, level), out byte alpha)) return layer;
        return layer with { Custom = (ushort)((layer.Custom & 0x7ff) | ((alpha >> 3) << 11)) };
    }
}
