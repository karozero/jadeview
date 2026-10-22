using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string HelperTextInitCallback = "helper_text_hlptxt_init";
    private const string HelperTextDepartureCallback = "helper_text_hlptxt_tests_depart";
    private const string HelperTextDisplayCallback = "helper_text_hlptxt_exec_ask_display";

    private static void AddHelperTextBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        bool initializes = CinematicScriptReader.TryDescribeCallbackReachability(owner,
            HelperTextInitCallback, out string initCallbackPath);
        bool waitsForConditions = CinematicScriptReader.TryDescribeCallbackReachability(owner,
            HelperTextDepartureCallback, out string departureCallbackPath);
        bool submitsDisplay = CinematicScriptReader.TryDescribeCallbackReachability(owner,
            HelperTextDisplayCallback, out string displayCallbackPath);

        if (initializes || waitsForConditions)
        {
            diagnostics.Add($"helper_text callback evidence: init [{initCallbackPath}]; condition checks [{departureCallbackPath}]. These are source-reachable callback paths from the owner's assigned slots; they do not prove runtime dispatch.");
            diagnostics.Add("helper_text_hlptxt derives its required-condition mask during initialization from configured actor, message, visibility, capability, custom-bit, saved-flag, and destructible-state fields. Its wait callback checks those configured predicates and runtime readiness; this archive snapshot does not establish which tests pass or whether the owner is scheduled.");
            var messageActor = owner.EffectiveObjectReference("des_o_acteur_msg");
            int? messageId = EffectiveInteger(owner, "des_i_msg_ID");
            int? messageMask = EffectiveInteger(owner, "des_i_msg_INT1");
            if (waitsForConditions && messageActor != null && messageId is > 0)
            {
                string intCondition = messageMask is > 0
                    ? $"Int1 mask 0x{messageMask:X} must intersect the queued value"
                    : "any Int1 value is accepted";
                diagnostics.Add($"The configured message gate watches up to four queued messages on {DescribeObjectReference(messageActor)} for msgID {messageId}; {intCondition}.");
            }
        }

        if (submitsDisplay)
        {
            diagnostics.Add($"Text-display callback is source-reachable: {displayCallbackPath}.");
            float? automaticDelay = EffectiveFloat(owner, "des_i_auto_delay");
            diagnostics.Add(automaticDelay == 0
                ? "A populated text slot submits message 30 to the shared text/audio manager. With des_i_auto_delay=0, the helper first uses the associated sound duration; if that is zero, it falls back to the per-line duration or a text-length estimate, then applies game speed."
                : "A populated text slot submits message 30 to the shared text/audio manager. The callback derives its duration from per-line duration or a text-length estimate, then applies game speed; sound playback and callback timing are not evaluated.");

            diagnostics.Add("Per-line zero volume defaults to 0.8; near/far audio distances are written only when the line's far value is nonzero. The line's des_ai_perso_localisation entry supplies the message's speaker-position object reference.");
        }

        CinematicDialogueLine[] populatedLines = (owner.DialogueLines ?? [])
            .Where(line => line.TextBankKey != 0 || line.TextId != 0 || line.SoundId is > 0)
            .ToArray();
        if (populatedLines.Length == 0)
        {
            diagnostics.Add("No populated helper-text line slots were decoded from the effective model/instance OVA arrays.");
            return;
        }

        diagnostics.Add($"Decoded {populatedLines.Length} configured helper-text line slot(s) from the effective OVA arrays:");
        foreach (var line in populatedLines)
        {
            var details = new List<string>();
            if (line.TextBankKey != 0 || line.TextId != 0)
                details.Add($"text {line.TextBankKey:X8}:{line.TextId}");
            if (line.SoundId is > 0) details.Add($"sound modifier {line.SoundId}");
            if (line.TextBox is { } textBox) details.Add($"text box {textBox.ToString("R", CultureInfo.InvariantCulture)}");
            if (line.Volume is { } volume) details.Add($"stored volume {volume.ToString("R", CultureInfo.InvariantCulture)}");
            if (line.Near.HasValue || line.Far.HasValue)
                details.Add($"near/far {FormatOptional(line.Near)}/{FormatOptional(line.Far)}");
            if (line.Duration is { } duration)
                details.Add(duration == 0 ? "stored duration 0 (runtime fallback)"
                    : $"explicit duration {duration.ToString("R", CultureInfo.InvariantCulture)} s");
            if (line.SpeakerKey is { } speakerKey) details.Add($"speaker reference {speakerKey:X8}");
            diagnostics.Add($"Line slot {line.Slot}: {string.Join(", ", details)}.");
        }
    }
}
