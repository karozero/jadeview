using System.Numerics;

namespace JadeView;

internal sealed class CinematicMaterialState
{
    private const int EnableLevel = 15006, EnableLevelBySubMaterial = 15013,
        SetAlpha = 15008, SetConstant = 15000, SetThreshold = 15010;
    private sealed record Change(float Time, uint Material, int Level, int Selector, uint Value);
    private readonly Change[] changes;
    private readonly Dictionary<(uint Material, int Level), JadeMaterialLayer> source = [];
    private readonly Dictionary<(uint Material, int Level), JadeMaterialLayer> evaluated = [];
    private readonly Dictionary<(uint ObjectKey, int SubMaterial), Vector4> environmentConstants = [];
    private readonly Dictionary<uint, Vector4> evaluatedConstants = [];
    private readonly Dictionary<uint, Vector4> callbackConstants = [];
    private readonly Dictionary<(uint Material, int Level), byte> callbackAlphas = [];
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
    internal Vector4 AmbientColor { get; private set; }
    internal Vector4 AmbientColor2 { get; private set; }
    internal bool HasAmbient { get; private set; }
    internal CinematicMaterialLayerSnapshot CaptureLayers()
    {
        var layers = new Dictionary<(uint Material, int Level), JadeMaterialLayer>(evaluated);
        foreach (((uint material, int level), byte alpha) in callbackAlphas)
        {
            JadeMaterialLayer? layer = layers.GetValueOrDefault((material, level))
                ?? source.GetValueOrDefault((material, level));
            if (layer is { } original)
                layers[(material, level)] = original with { Custom = PackLocalAlpha(original.Custom, alpha) };
        }
        var materialConstants = new Dictionary<uint, Vector4>(evaluatedConstants);
        foreach ((uint materialKey, Vector4 color) in callbackConstants)
            materialConstants[materialKey] = color;
        return new(Time, layers,
            new Dictionary<(uint, int, int), byte>(environmentAlphas),
            new Dictionary<(uint, int), Vector4>(environmentConstants), materialConstants,
            new Dictionary<uint, float>(shockwaveTransparency))
        {
            RliColor = RliColor, RliFactor = RliFactor,
            AmbientColor = AmbientColor, AmbientColor2 = AmbientColor2, HasAmbient = HasAmbient
        };
    }

    internal CinematicMaterialState(AnimationClip clip, WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials)
        : this(clip, world, materials, []) { }

    internal CinematicMaterialState(AnimationClip clip, WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials,
        IEnumerable<CinematicEnvironmentController> controllers,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedCommands = null)
    {
        this.world = world;
        var activeControllerKeys = world.Objects
            .Where(sceneObject => (sceneObject.ControlFlags & CinematicObjectState.Inactive) == 0)
            .Select(sceneObject => sceneObject.GaoKey).ToHashSet();
        environmentControllers = controllers.Where(controller => activeControllerKeys.Contains(controller.OwnerKey)).ToArray();
        changes = CinematicLiteralCall.ReadTimeline(clip)
            .Concat(nestedCommands ?? [])
            .Select(item => Decode(item.Time, item.Call, world, materials))
            .OfType<Change>().Distinct().OrderBy(change => change.Time).ToArray();
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
        callbackConstants.Clear();
        callbackAlphas.Clear();
        if (!hasEvaluation || Time < lastEvaluationTime)
        {
            evaluated.Clear();
            evaluatedConstants.Clear();
            appliedChangeCount = 0;
            hasEvaluation = true;
        }
        lastEvaluationTime = Time;
        shockwaveTransparency.Clear();
        while (appliedChangeCount < changes.Length && changes[appliedChangeCount].Time <= Time)
        {
            var change = changes[appliedChangeCount++];
            if (change.Selector == SetConstant)
            {
                // Native MATSetConstant writes RGB into the material constant slot;
                // its packed high byte is not consumed as opacity.
                evaluatedConstants[change.Material] = UnpackConstant(change.Value);
                continue;
            }
            var key = (change.Material, change.Level);
            if (!source.TryGetValue(key, out var original)) continue;
            var layer = evaluated.GetValueOrDefault(key) ?? original;
            evaluated[key] = change.Selector switch
            {
                EnableLevel or EnableLevelBySubMaterial => layer with
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
        AmbientColor = worldCycle.AmbientColor;
        AmbientColor2 = worldCycle.AmbientColor2;
        HasAmbient = worldCycle.HasAmbient;
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
        ApplyCallbackAlpha(material, level, evaluated.GetValueOrDefault((material, level)) ?? original);

    private JadeMaterialLayer ApplyCallbackAlpha(uint material, int level, JadeMaterialLayer layer) =>
        callbackAlphas.TryGetValue((material, level), out byte alpha)
            ? layer with { Custom = PackLocalAlpha(layer.Custom, alpha) }
            : layer;

    internal JadeMaterialLayer ApplyEnvironmentAlpha(uint objectKey, int subMaterial, int layer,
        JadeMaterialLayer original) => environmentAlphas.TryGetValue((objectKey, subMaterial, layer), out byte alpha)
        ? original with { Custom = PackLocalAlpha(original.Custom, alpha) }
        : original;

    internal Vector4 ApplyConstant(uint objectKey, int subMaterial, Vector4 original)
        => ApplyConstant(0, objectKey, subMaterial, original);

    internal Vector4 ApplyConstant(uint materialKey, uint objectKey, int subMaterial, Vector4 original)
    {
        var constant = callbackConstants.GetValueOrDefault(materialKey,
            evaluatedConstants.GetValueOrDefault(materialKey,
                environmentConstants.GetValueOrDefault((objectKey, subMaterial), original)));
        if (subMaterial is 0 or 1 && shockwaveTransparency.TryGetValue(objectKey, out float transparency))
            constant = Vector4.Lerp(constant, Vector4.Zero, transparency);
        return constant;
    }

    internal void SetScriptMaterialConstants(IReadOnlyDictionary<uint, uint>? packedColors)
    {
        callbackConstants.Clear();
        if (packedColors == null) return;
        foreach ((uint materialKey, uint packedColor) in packedColors)
            callbackConstants[materialKey] = UnpackConstant(packedColor);
    }

    internal void SetScriptMaterialAlphas(IReadOnlyDictionary<(uint Material, int Level), byte>? alphas)
    {
        callbackAlphas.Clear();
        if (alphas == null) return;
        foreach (KeyValuePair<(uint Material, int Level), byte> entry in alphas)
            callbackAlphas[entry.Key] = entry.Value;
    }

    private static ushort PackLocalAlpha(ushort custom, byte alpha) =>
        (ushort)((custom & 0x7ff) | ((alpha >> 3) << 11));

    private static Change? Decode(float time, CinematicLiteralCall? call,
        WorldData world, IReadOnlyDictionary<uint, ResolvedMaterial> materials)
    {
        bool enableLevelBySubMaterial = call?.Selector == EnableLevelBySubMaterial;
        bool setConstant = call?.Selector == SetConstant;
        bool supported = enableLevelBySubMaterial || setConstant
            || call?.Selector is EnableLevel or SetAlpha or SetThreshold;
        int expectedArgumentCount = enableLevelBySubMaterial ? 4 : setConstant ? 2 : 3;
        if (!supported || call!.Arguments.Length != expectedArgumentCount || !float.IsFinite(time)) return null;
        if (setConstant)
        {
            uint materialKey = ResolveConstantMaterialKey(call.Arguments[1], call.Target, world, materials);
            return materialKey is 0 or uint.MaxValue
                ? null
                : new(time, materialKey, 0, SetConstant, call.Arguments[0]);
        }
        uint materialSelector = call.Arguments[0];
        uint key = ResolveMaterialKey(materialSelector, call.Target, world, materials);
        if (key is 0 or uint.MaxValue) return null;
        if (materials.TryGetValue(key, out var selected) && selected.Type == 4)
        {
            uint serializedSubMaterialIndex = enableLevelBySubMaterial ? call.Arguments[2] : 0;
            if (serializedSubMaterialIndex >= selected.SubMaterialKeys.Count) return null;
            int subMaterialIndex = (int)serializedSubMaterialIndex;
            key = selected.SubMaterialKeys[subMaterialIndex];
        }
        uint value = enableLevelBySubMaterial ? call.Arguments[3] : call.Arguments[2];
        return new(time, key, unchecked((int)call.Arguments[1]), call.Selector, value);
    }

    private static uint ResolveConstantMaterialKey(uint selector, uint objectKey, WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials)
    {
        uint materialKey = selector;
        if ((selector & 0xff000000) == 0)
        {
            SceneObject[] objectMatches = FindWorldObjects(world, objectKey);
            if (objectMatches.Length != 1) return 0;
            materialKey = objectMatches[0].MaterialKey;
        }
        if (!materials.TryGetValue(materialKey, out var material)) return 0;
        if (material.Type == 4)
        {
            int subMaterialIndex = (selector & 0xff000000) != 0
                ? 0
                : material.SubMaterialKeys.Count == 0 ? -1 : (int)(selector % (uint)material.SubMaterialKeys.Count);
            if (subMaterialIndex < 0 || subMaterialIndex >= material.SubMaterialKeys.Count) return 0;
            materialKey = material.SubMaterialKeys[subMaterialIndex];
        }
        return materials.TryGetValue(materialKey, out var selectedMaterial)
            && selectedMaterial.Type is 3 or 5 ? materialKey : 0;
    }

    private static Vector4 UnpackConstant(uint packedColor) => new(
        (packedColor & 255) / 255f,
        (packedColor >> 8 & 255) / 255f,
        (packedColor >> 16 & 255) / 255f,
        1f);

    internal static bool TryParseMaterialSelector(string expression, out uint selector)
    {
        string normalized = expression.Trim().TrimEnd('u', 'U', 'l', 'L');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(normalized.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out selector);
        if (int.TryParse(normalized, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int signed))
        {
            selector = unchecked((uint)signed);
            return true;
        }
        return uint.TryParse(normalized, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out selector);
    }

    internal static uint ResolveMaterialKey(uint selector, uint objectKey, WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials)
    {
        if ((selector & 0xff000000) != 0) return selector;
        SceneObject[] objectMatches = FindWorldObjects(world, objectKey);
        if (objectMatches.Length != 1) return 0;
        uint objectMaterial = objectMatches[0].MaterialKey;
        if (!materials.TryGetValue(objectMaterial, out var root) || root.Type != 4) return objectMaterial;
        if (root.SubMaterialKeys.Count == 0) return 0;
        return root.SubMaterialKeys[(int)(selector % root.SubMaterialKeys.Count)];
    }

    private static SceneObject[] FindWorldObjects(WorldData world, uint objectKey) =>
        world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Where(sceneObject => sceneObject.GaoKey == objectKey).Distinct().ToArray();
}
