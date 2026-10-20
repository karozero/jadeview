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
    internal Vector4 RliColor { get; private set; }
    internal float RliFactor { get; private set; }

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
        changes = clip.Tracks.Where(track => track.Bone < 0).SelectMany(track => track.Keys.SelectMany(key =>
            key.OpaquePayloads.Select(payload => Decode(key.Time, payload, track.Target, clip, world, materials))))
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
        evaluated.Clear();
        environmentConstants.Clear();
        environmentAlphas.Clear();
        shockwaveTransparency.Clear();
        foreach (var change in changes.TakeWhile(change => change.Time <= time))
        {
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
        var worldCycle = CinematicWorldCyclePalette.Evaluate(environmentControllers, world, attackFactor);
        foreach (var pair in worldCycle.Constants) environmentConstants[pair.Key] = pair.Value;
        foreach (var pair in worldCycle.LocalAlphas) environmentAlphas[pair.Key] = pair.Value;
        RliColor = worldCycle.RliColor;
        RliFactor = worldCycle.RliFactor;
        if (shockwaves != null)
        {
            var activeShockwaves = shockwaves.Where(effect => !effect.Finished && effect.Transparency > 0).ToArray();
            foreach (var effect in activeShockwaves)
                shockwaveTransparency[effect.Object.GaoKey] = effect.Transparency;
            if (activeShockwaves.Length > 0)
            {
                RliColor = Vector4.One;
                RliFactor = activeShockwaves.Max(effect => effect.Transparency);
            }
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

    private static Change? Decode(float time, AnimationOpaquePayload payload, uint trackTarget, AnimationClip clip,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials)
    {
        var call = CinematicLiteralCall.Decode(payload, trackTarget, clip.CinematicOwnerKey);
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
