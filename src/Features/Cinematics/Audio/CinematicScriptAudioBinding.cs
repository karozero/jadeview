using System.Globalization;

namespace JadeView;

internal sealed record CinematicScriptAudioBinding(string VariableName, int? ModifierId,
    CinematicSoundModifier[] Modifiers, string Status);

internal static class CinematicScriptAudioBindingResolver
{
    private static readonly (string VariableName, string Role)[] SoundSlots =
    [
        ("des_i_SndMdf_Musique", "music"),
        ("des_i_SndMdf_Ambiance", "ambience"),
        ("des_i_SndMdf_Voix", "voice")
    ];

    internal static CinematicScriptAudioBinding[] Resolve(CinematicScriptOwner owner, SceneDocument document)
    {
        WorldData? sourceWorld = document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey);
        SceneObject[] ownerObjects = sourceWorld?.Objects
            .Where(sceneObject => sceneObject.GaoKey == owner.OwnerKey).ToArray() ?? [];

        return SoundSlots.Select(slot => ResolveSlot(owner, document, ownerObjects,
            slot.VariableName)).ToArray();
    }

    internal static string DescribeRole(string variableName) =>
        SoundSlots.FirstOrDefault(slot => slot.VariableName.Equals(variableName,
            StringComparison.OrdinalIgnoreCase)).Role ?? variableName;

    private static CinematicScriptAudioBinding ResolveSlot(CinematicScriptOwner owner,
        SceneDocument document, SceneObject[] ownerObjects, string variableName)
    {
        int? modifierId = ReadEffectiveInteger(owner, variableName);
        if (modifierId is null)
            return new(variableName, null, [], "OVA value unavailable");
        if (modifierId == -10)
            return new(variableName, modifierId, [], "disabled sentinel");
        if (ownerObjects.Length == 0)
            return new(variableName, modifierId, [], "owner GAO is not present in its loaded source world");

        var ownerSoundModifiers = ownerObjects.SelectMany(sceneObject => sceneObject.Modifiers)
            .Where(modifier => modifier.Type == 8 && modifier.Data.Length >= 12).ToArray();
        if (modifierId == -1)
        {
            CinematicSoundModifier[] aggregate = ownerSoundModifiers
                .Select(modifier => CinematicSoundModifier.Read(modifier, document))
                .OfType<CinematicSoundModifier>().Distinct().ToArray();
            string aggregateStatus = aggregate.Length > 0
                ? $"status query aggregates all {ownerSoundModifiers.Length} attached type-8 modifiers"
                : "status query aggregates all attached type-8 modifiers; none decoded to a sound resource";
            return new(variableName, modifierId, aggregate, aggregateStatus);
        }
        if (modifierId < 0)
            return new(variableName, modifierId, [], "unknown negative modifier sentinel");

        var matchingModifiers = ownerSoundModifiers
            .Where(modifier => Preview.I32(modifier.Data, 8) == modifierId)
            .ToArray();
        if (matchingModifiers.Length == 0)
            return new(variableName, modifierId, [], "no matching type-8 modifier on the owner GAO");
        if (matchingModifiers.Length > 1)
            return new(variableName, modifierId, [],
                $"ambiguous: {matchingModifiers.Length} type-8 modifiers share this ID");

        CinematicSoundModifier? sound = CinematicSoundModifier.Read(matchingModifiers[0], document);
        return sound is null
            ? new(variableName, modifierId, [],
                "modifier found, but its sound resource or serialized layout could not be resolved")
            : new(variableName, modifierId, [sound], "resolved");
    }

    private static int? ReadEffectiveInteger(CinematicScriptOwner owner, string variableName)
    {
        string? value = owner.EffectiveVariable(variableName)?.Value;
        if (value is null) return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int hexadecimal) ? hexadecimal : null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : null;
    }
}
