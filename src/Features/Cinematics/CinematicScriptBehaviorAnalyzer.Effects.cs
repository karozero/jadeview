using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private static void AddSimpleHaloBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool hasInitCallback = owner.CallbackNames.Contains(SimpleHaloInitCallback, StringComparer.Ordinal);
        bool hasMoveCallback = owner.CallbackNames.Contains(SimpleHaloMoveCallback, StringComparer.Ordinal);
        int? effectType = EffectiveInteger(owner, "des_i_Type");
        float? startDelay = EffectiveFloat(owner, "des_f_time_init");
        float? radius = EffectiveFloat(owner, "des_f_Radius");
        float? offset = EffectiveFloat(owner, "des_f_Offset");
        float? extrusion = EffectiveFloat(owner, "des_f_Extrude");
        int? canMove = EffectiveInteger(owner, "des_i_canmove");
        int? blink = EffectiveInteger(owner, "des_i_clignote");
        float? blinkFrequency = EffectiveFloat(owner, "des_f_freq");
        float? blinkSpeed = EffectiveFloat(owner, "des_f_speed_clignote");
        uint? materialTarget = EffectiveUnsigned(owner, "des_o_gaomat");
        string materialDescription = materialTarget switch
        {
            null => "the effective material target is unavailable",
            0 => "no GAO material target is configured",
            1 or 2 => $"the main-actor slot {materialTarget} supplies the effect material",
            _ => owner.EffectiveObjectReference("des_o_gaomat") is { } reference
                ? $"'{reference.TargetName ?? $"object {reference.TargetKey:X8}"}' supplies the effect material"
                : $"object {materialTarget.Value:X8} is configured as the effect material target"
        };

        string effectDescription = effectType switch
        {
            0 => "des_i_Type=0: the recovered init callback does not create a GFX effect.",
            1 => "type 1 creates GFX effect 21 at the owner's position, using radius and extrusion parameters.",
            2 => "type 2 creates GFX effect 21 at the owner's position, using radius and offset parameters.",
            3 => "type 3 creates GFX effect 21 at the owner's position, using radius, offset and extrusion parameters.",
            > 3 => $"type {effectType} uses the default GFX effect 21 setup, which follows owner position and sight direction and reads radius, offset, extrusion, angle and number.",
            < 0 => $"negative type {effectType} enters the default GFX effect 21 initialization branch, but the move callback's sight-direction update is only enabled for type values above 3.",
            _ => "des_i_Type is unavailable; the callback's GFX effect variant cannot be determined."
        };
        diagnostics.Add(hasInitCallback
            ? $"SFX_HaloSimple init: {effectDescription}"
            : $"SFX_HaloSimple move callback is present without its init callback: {effectDescription} The serialized data does not prove that a GFX effect instance exists.");

        if (hasInitCallback)
            diagnostics.Add($"The effect material binding uses {materialDescription}. "
                + (startDelay is { } delay
                    ? $"The init callback waits {delay.ToString("R", CultureInfo.InvariantCulture)} seconds before handing control to the move callback."
                    : "The start-delay OVA value is unavailable."));

        if (radius is { } configuredRadius)
            diagnostics.Add($"Configured halo radius: {configuredRadius.ToString("R", CultureInfo.InvariantCulture)}.");
        if (offset is { } configuredOffset)
            diagnostics.Add($"Configured halo offset: {configuredOffset.ToString("R", CultureInfo.InvariantCulture)}.");
        if (extrusion is { } configuredExtrusion)
            diagnostics.Add($"Configured halo extrusion: {configuredExtrusion.ToString("R", CultureInfo.InvariantCulture)}.");

        if (hasMoveCallback && canMove == 0)
            diagnostics.Add("des_i_canmove=0: the source callbacks set the script owner's inactive flag after positioning the GFX effect; this does not remove the separately created effect.");
        else if (hasMoveCallback && canMove is null)
            diagnostics.Add("des_i_canmove is unavailable; the source's owner-inactivation behavior cannot be derived.");

        if (!hasMoveCallback)
            diagnostics.Add("The move callback is absent, so position following and capability-driven color updates are not available from this owner's callback table.");
        else if (blink is > 0)
            diagnostics.Add($"Blink mode is enabled: the callback blends the packed effect color toward zero and back at the configured frequency ({FormatOptional(blinkFrequency)}) and speed ({FormatOptional(blinkSpeed)}); the sound request is tied to the color phase, not to a decoded audio cue.");
        else if (blink == 0)
            diagnostics.Add("Blink mode is disabled; the move callback instead blends toward the configured color while capability 0x2 is set, and toward zero otherwise when capability 0x1 is active.");
        else
            diagnostics.Add("The blink-mode OVA value is unavailable; color behavior cannot be selected from this snapshot.");

        diagnostics.Add("The source callbacks use a native GFX effect rather than animating a mesh. Live capabilities, the runtime effect instance, script scheduling and sound playback are not evaluated.");
    }

    private static string FormatOptional(float? value) => value is { } number
        ? number.ToString("R", CultureInfo.InvariantCulture)
        : "unavailable";
}
