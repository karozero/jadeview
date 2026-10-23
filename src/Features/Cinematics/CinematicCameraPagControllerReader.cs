using System.Globalization;

namespace JadeView;

internal static class CinematicCameraPagControllerReader
{
    private const string CallbackName = "SFX_colpag_cam_scp_loop";
    private const uint CameraObjectKey = 0x71003F56;

    internal static CinematicCameraPagController[] Read(IEnumerable<CinematicScriptOwner> owners,
        WorldData world)
    {
        var objectsByKey = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        if (!objectsByKey.TryGetValue(CameraObjectKey, out var camera)) return [];

        var controllers = new List<CinematicCameraPagController>();
        foreach (var owner in owners.Where(owner => owner.WorldKey == world.Key
            && owner.CallbackNames.Contains(CallbackName, StringComparer.Ordinal)))
        {
            if (!objectsByKey.TryGetValue(owner.OwnerKey, out var ownerObject)) continue;
            SceneObject? firstEffect = ResolveEffect(owner, "des_o_pag1", objectsByKey);
            SceneObject? secondEffect = ResolveEffect(owner, "des_o_pag2", objectsByKey);
            if (firstEffect == null && secondEffect == null) continue;
            if (!TryReadFloat(owner, "f_time", 0, out float initialTimer)
                || !TryReadFloat(owner, "f_rand", 2, out float initialDelay)
                || !TryReadInteger(owner, "i_mode", 0, out int initialMode)
                || initialDelay <= 0)
                continue;

            controllers.Add(new(ownerObject, firstEffect, secondEffect, camera,
                initialTimer, initialDelay, initialMode));
        }

        return controllers.ToArray();
    }

    private static SceneObject? ResolveEffect(CinematicScriptOwner owner, string variableName,
        IReadOnlyDictionary<uint, SceneObject> objectsByKey)
    {
        var reference = owner.EffectiveObjectReference(variableName);
        return reference?.TargetKey is > 2 and < uint.MaxValue
            ? objectsByKey.GetValueOrDefault(reference.TargetKey)
            : null;
    }

    private static bool TryReadFloat(CinematicScriptOwner owner, string name, float fallback,
        out float value)
    {
        value = fallback;
        var variable = owner.EffectiveVariable(name);
        return variable == null || variable.Type == PcVariableFormat.FloatType
            && float.TryParse(variable.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value);
    }

    private static bool TryReadInteger(CinematicScriptOwner owner, string name, int fallback,
        out int value)
    {
        value = fallback;
        var variable = owner.EffectiveVariable(name);
        return variable == null || variable.Type == PcVariableFormat.IntegerType
            && int.TryParse(variable.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

internal sealed record CinematicCameraPagController(SceneObject Owner, SceneObject? FirstEffect,
    SceneObject? SecondEffect, SceneObject Camera, float InitialTimer, float InitialDelay,
    int InitialMode);
