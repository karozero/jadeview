using System.Globalization;

namespace JadeView;

internal enum CinematicDialogueMessageResolutionKind
{
    ClearRequest,
    AdvanceRequest,
    ReceiverUnavailable,
    EmptySlot,
    TextReference
}

internal sealed record CinematicDialogueMessageResolution(
    CinematicDialogueMessageResolutionKind Kind,
    int? Slot = null,
    uint? TextBankKey = null,
    int? TextId = null,
    CinematicScriptOwnerId? ReceiverId = null,
    int? EffectivePriority = null,
    CinematicDialogueLine? Line = null);

internal static class CinematicDialogueMessageResolver
{
    private const int AdvanceLineRequest = 200;
    private const int LineSlotCount = 100;

    internal static CinematicDialogueMessageResolution Resolve(CinematicTimelineMessage message,
        IEnumerable<CinematicScriptOwner> owners, uint preferredWorldKey)
    {
        if (message.Int1 < 0)
            return new(CinematicDialogueMessageResolutionKind.ClearRequest);
        if (message.Int1 == AdvanceLineRequest)
            return new(CinematicDialogueMessageResolutionKind.AdvanceRequest);
        var receiver = CinematicScriptOwnerResolver.Resolve(message.ExecutionObjectKey, preferredWorldKey, owners);
        if (receiver == null)
            return new(CinematicDialogueMessageResolutionKind.ReceiverUnavailable, message.Int1 % LineSlotCount);

        int slot = message.Int1 % LineSlotCount;
        var line = receiver.DialogueLines?.FirstOrDefault(candidate => candidate.Slot == slot);
        int? priority = ResolvePriority(message.Int2, receiver);
        return line == null || line.TextBankKey == 0
            ? new(CinematicDialogueMessageResolutionKind.EmptySlot, slot, ReceiverId: receiver.Id,
                EffectivePriority: priority, Line: line)
            : new(CinematicDialogueMessageResolutionKind.TextReference, slot, line.TextBankKey, line.TextId,
                receiver.Id, priority, line);
    }

    internal static string[] DescribeSelectedLine(CinematicTimelineMessage message,
        CinematicDialogueMessageResolution resolution)
    {
        if (resolution.Line is not { } line) return [];

        var details = new List<string>();
        if (message.Int2 < 0)
        {
            details.Add($"Negative message Int2 priority {message.Int2} is ignored by Iris's type-30 queue selector.");
        }
        else if (resolution.EffectivePriority is { } priority)
        {
            string prioritySource = message.Int2 == 0
                ? "receiver default (5000 when configured default is zero)"
                : "message Int2";
            details.Add($"Queue priority {priority} · {prioritySource}; receiver selects the highest-priority eligible queued type-30 message.");
        }
        else if (message.Int2 == 0)
        {
            details.Add("Message Int2=0 requests the receiver's configured default queue priority; its effective value is unavailable or negative in the decoded OVA data.");
        }
        else
        {
            details.Add($"Queue priority {message.Int2} from message Int2; receiver selects the highest-priority eligible queued type-30 message.");
        }

        if (line.SoundId is >= 0 and var soundId)
        {
            string variation = line.SoundVariantMax is > 0
                ? $" · variation field {line.SoundVariantMax}"
                : "";
            details.Add($"Configured global voice sound ID {soundId}{variation}; archive preview cannot map the runtime sound-table order.");
        }
        else if (line.SoundId == -1)
        {
            details.Add("Global voice sound ID -1 disables per-line audio. The selected text may still be voiced through another audio path.");
        }
        if (line.Volume is { } volume)
            details.Add(volume == 0
                ? "Configured voice volume is zero; Iris substitutes 0.8."
                : $"Configured voice volume {volume.ToString("0.###", CultureInfo.InvariantCulture)}.");
        if (line.Near is { } near && line.Far is { } far)
            details.Add(near == far
                ? "Configured near/far distances are equal; Iris's text manager substitutes 0 and 50."
                : $"Configured near/far distances {near.ToString("0.###", CultureInfo.InvariantCulture)} / {far.ToString("0.###", CultureInfo.InvariantCulture)}.");

        details.Add("Static OVA/message mapping only: current queue competition, option gates, and audio playback state are not evaluated.");
        return details.ToArray();
    }

    private static int? ResolvePriority(int messagePriority, CinematicScriptOwner receiver)
    {
        if (messagePriority != 0) return messagePriority >= 0 ? messagePriority : null;
        var priorityVariable = receiver.EffectiveVariable("des_i_priorite_par_defaut");
        if (priorityVariable == null) return null;

        int? configuredPriority = ParseInteger(priorityVariable.Value);
        int? effectivePriority = configuredPriority == 0 ? 5000 : configuredPriority;
        return effectivePriority is >= 0 ? effectivePriority : null;
    }

    private static int? ParseInteger(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out int hexadecimal) ? hexadecimal : null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
            ? integer : null;
    }
}
