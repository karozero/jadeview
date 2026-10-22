using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string CinematicSoundCompletionCallback = "Cine_Events_cine_test_fin_sons";
    private const string CinematicSoundFadeCallback = "Cine_Events_Cine_exec_soundtracks_Fade";

    private static void AddCinematicAudioBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool hasSoundCompletion = HasCinematicFunctionReference(owner, CinematicSoundCompletionCallback);
        bool hasSoundFade = HasCinematicFunctionReference(owner, CinematicSoundFadeCallback);
        if (!hasSoundCompletion && !hasSoundFade) return;

        string musicModifier = DescribeSoundModifier(owner, "des_i_SndMdf_Musique");
        string ambienceModifier = DescribeSoundModifier(owner, "des_i_SndMdf_Ambiance");
        string voiceModifier = DescribeSoundModifier(owner, "des_i_SndMdf_Voix");
        diagnostics.Add($"Cine_Events sound modifier slots (owner-local modifier IDs): music {musicModifier}; "
            + $"ambience {ambienceModifier}; voice {voiceModifier}. These IDs are not sound-bank keys, "
            + "and the archive snapshot does not reveal their live playback status.");

        if (hasSoundCompletion)
        {
            int? replayMode = EffectiveInteger(owner, "des_i_ripley");
            string completionAction = replayMode switch
            {
                1 => "when complete, the source returns this owner to its init track",
                { } mode => $"when complete, des_i_ripley={mode} makes the source destroy this owner",
                _ => "the source either returns to init or destroys this owner according to des_i_ripley"
            };
            diagnostics.Add("Cine_Events completion checks status mask 0x4 on the configured music, ambience, "
                + "and voice modifiers. If any reports that bit, completion waits in cine_test_fin_sons; "
                + $"otherwise configured streams are flushed and {completionAction}. "
                + "Iris also returns zero when sound is disabled or a modifier/owner is unavailable, so the "
                + "archive cannot distinguish a stopped stream from those cases or reveal the branch taken.");
        }

        if (hasSoundFade)
        {
            float? groupTarget = EffectiveFloat(owner, "des_f_Fadergroup");
            string groupTargetDescription = groupTarget is { } target
                ? target.ToString("G", CultureInfo.InvariantCulture)
                : "unavailable";
            diagnostics.Add("The model references the sound-fade callback: source capability bits 0x1, 0x2, "
                + "and 0x4 drive mixer tracks 1010, 1020, and 1040; bit 0x8 fades sound group 11 between "
                + $"unity and des_f_Fadergroup ({groupTargetDescription}) using frame delta. These are "
                + "configured callback capabilities, not proof of an active fade in this preview.");
        }
    }

    private static string DescribeSoundModifier(CinematicScriptOwner owner, string variableName)
    {
        int? modifierId = EffectiveInteger(owner, variableName);
        return modifierId switch
        {
            null => "unavailable",
            -1 => "-1 (status query aggregates all attached sound modifiers)",
            -10 => "-10 (disabled status-query sentinel)",
            < 0 => $"{modifierId.Value} (unknown negative sentinel)",
            _ => modifierId.Value.ToString(CultureInfo.InvariantCulture)
        };
    }
}
