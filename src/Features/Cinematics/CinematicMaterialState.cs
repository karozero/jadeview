using System.Numerics;

namespace JadeView;

internal sealed class CinematicMaterialState
{
    private const int EnableLevel = 15006, SetAlpha = 15008, SetThreshold = 15010;
    private sealed record Change(float Time, uint Material, int Level, int Selector, uint Value);
    private readonly Change[] changes;
    private readonly Dictionary<(uint Material, int Level), JadeMaterialLayer> source = [];
    private readonly Dictionary<(uint Material, int Level), JadeMaterialLayer> evaluated = [];
    private readonly Dictionary<(uint ObjectKey, int SubMaterial), Vector4> environmentConstants = [];
    private readonly Dictionary<(uint ObjectKey, int SubMaterial, int Layer), byte> environmentAlphas = [];
    private readonly Dictionary<uint, float> shockwaveTransparency = [];
    private readonly CinematicEnvironmentController[] environmentControllers;
    private readonly WorldData world;
    private int appliedChangeCount;
    private bool hasEvaluation;
    private float lastEvaluationTime;
    internal float Time { get; private set; }
    internal Vector4 RliColor { get; private set; }
    internal float RliFactor { get; private set; }
    internal Vector4 FogColor { get; private set; }
    internal float FogNear { get; private set; }
    internal float FogFar { get; private set; }

    internal CinematicMaterialState(AnimationClip clip, WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials)
        : this(clip, world, materials, []) { }

    internal CinematicMaterialState(AnimationClip clip, WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials,
        IEnumerable<CinematicEnvironmentController> controllers)
    {
        this.world = world;
        var activeControllerKeys = world.Objects
            .Where(sceneObject => (sceneObject.ControlFlags & CinematicObjectState.Inactive) == 0)
            .Select(sceneObject => sceneObject.GaoKey).ToHashSet();
        environmentControllers = controllers.Where(controller => activeControllerKeys.Contains(controller.OwnerKey)).ToArray();
        changes = CinematicLiteralCall.ReadTimeline(clip)
            .Select(item => Decode(item.Time, item.Call, world, materials))
            .OfType<Change>().OrderBy(change => change.Time).ToArray();
        foreach (uint key in changes.Select(change => change.Material).Distinct())
        {
            var layers = JadeMaterialLayer.Read(materials.GetValueOrDefault(key)?.SourceBytes);
            for (int level = 0; level < layers.Count; level++) source[(key, level)] = layers[level];
        }
    }

    internal void Evaluate(float time, float attackFactor = 0,
        IEnumerable<CinematicShockwaveTimeline.ShockwaveState>? shockwaves = null)
    {
        Time = float.IsFinite(time) ? Math.Max(0, time) : 0;
        if (!hasEvaluation || Time < lastEvaluationTime)
        {
            evaluated.Clear();
            appliedChangeCount = 0;
            hasEvaluation = true;
        }
        lastEvaluationTime = Time;
        shockwaveTransparency.Clear();
        while (appliedChangeCount < changes.Length && changes[appliedChangeCount].Time <= Time)
        {
            var change = changes[appliedChangeCount++];
            var key = (change.Material, change.Level);
            if (!source.TryGetValue(key, out var original)) continue;
            var layer = evaluated.GetValueOrDefault(key) ?? original;
            evaluated[key] = change.Selector switch
            {
                EnableLevel => layer with
                {
                    Flags = change.Value != 0 ? layer.Flags & ~JadeMaterialFlags.Disabled
                    : layer.Flags | JadeMaterialFlags.Disabled
                },
                // Native local alpha is five bits; preserve planar/reflection parameters below it.
                SetAlpha => layer with { Custom = (ushort)(((uint)layer.Custom & 0x7ffu) | ((change.Value >> 3) << 11)) },
                SetThreshold => layer with { Flags = (layer.Flags & 0xc0ffffff) | ((change.Value >> 2 << 24) & 0x3f000000) },
                _ => layer
            };
        }
        var worldCycle = CinematicWorldCyclePalette.Evaluate(environmentControllers, world,
            environmentConstants, environmentAlphas, attackFactor);
        RliColor = worldCycle.RliColor;
        RliFactor = worldCycle.RliFactor;
        FogColor = worldCycle.FogColor;
        FogNear = worldCycle.FogNear;
        FogFar = worldCycle.FogFar;
        if (shockwaves != null)
        {
            float maximumShockwaveTransparency = 0;
            foreach (var effect in shockwaves)
            {
                if (effect.Finished || effect.Transparency <= 0) continue;
                shockwaveTransparency[effect.Object.GaoKey] = effect.Transparency;
                maximumShockwaveTransparency = Math.Max(maximumShockwaveTransparency, effect.Transparency);
            }
            if (maximumShockwaveTransparency <= 0) return;
            RliColor = Vector4.One;
            RliFactor = maximumShockwaveTransparency;
        }
    }

    internal JadeMaterialLayer Apply(uint material, int level, JadeMaterialLayer original) =>
        evaluated.GetValueOrDefault((material, level)) ?? original;

    internal JadeMaterialLayer ApplyEnvironmentAlpha(uint objectKey, int subMaterial, int layer,
        JadeMaterialLayer original) => environmentAlphas.TryGetValue((objectKey, subMaterial, layer), out byte alpha)
        ? original with { Custom = PackLocalAlpha(original.Custom, alpha) }
        : original;

    internal Vector4 ApplyConstant(uint objectKey, int subMaterial, Vector4 original)
    {
        var constant = environmentConstants.GetValueOrDefault((objectKey, subMaterial), original);
        if (subMaterial is 0 or 1 && shockwaveTransparency.TryGetValue(objectKey, out float transparency))
            constant = Vector4.Lerp(constant, Vector4.Zero, transparency);
        return constant;
    }

    private static ushort PackLocalAlpha(ushort custom, byte alpha) =>
        (ushort)((custom & 0x7ff) | ((alpha >> 3) << 11));

    private static Change? Decode(float time, CinematicLiteralCall? call,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials)
    {
        bool supported = call?.Selector is EnableLevel or SetAlpha or SetThreshold;
        if (!supported || call!.Arguments.Length != 3 || !float.IsFinite(time)) return null;
        uint materialSelector = call.Arguments[0];
        uint key = materialSelector;
        if ((materialSelector & 0xff000000) == 0)
        {
            key = world.Objects.FirstOrDefault(item => item.GaoKey == call.Target)?.MaterialKey ?? 0;
            if (materials.TryGetValue(key, out var root) && root.Type == 4)
                key = root.SubMaterialKeys.Count > 0 ? root.SubMaterialKeys[(int)(materialSelector % root.SubMaterialKeys.Count)] : 0;
        }
        if (materials.TryGetValue(key, out var selected) && selected.Type == 4)
            key = selected.SubMaterialKeys.FirstOrDefault();
        return new(time, key, unchecked((int)call.Arguments[1]), call.Selector, call.Arguments[2]);
    }
}
