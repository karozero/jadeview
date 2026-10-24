using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicDialogueMessageLookup(CinematicTimelineMessage message, uint preferredWorldKey)
    {
        if (Document == null) return;
        var resolution = CinematicDialogueMessageResolver.Resolve(message,
            Document.CinematicScriptOwners.Values, preferredWorldKey);
        switch (resolution.Kind)
        {
            case CinematicDialogueMessageResolutionKind.ClearRequest:
                ImGui.TextDisabled("  Dialogue receiver clear/hide request; no text slot is selected.");
                break;
            case CinematicDialogueMessageResolutionKind.AdvanceRequest:
                ImGui.TextDisabled("  Dialogue receiver advance request; the resulting slot depends on its runtime cursor.");
                break;
            case CinematicDialogueMessageResolutionKind.ReceiverUnavailable:
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot}; EVE target {message.ExecutionObjectKey:X8} is unavailable.");
                break;
            case CinematicDialogueMessageResolutionKind.EmptySlot:
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot}; no text-bank reference is configured.");
                break;
            case CinematicDialogueMessageResolutionKind.TextReference:
                var receiver = resolution.ReceiverId is { } receiverId
                    ? Document.CinematicScriptOwners.GetValueOrDefault(receiverId) : null;
                string receiverName = receiver?.Name ?? "receiver";
                ImGui.TextDisabled($"  Dialogue line slot {resolution.Slot} → text bank {resolution.TextBankKey:X8}, entry {resolution.TextId} ({receiverName}).");
                break;
        }

        foreach (string detail in CinematicDialogueMessageResolver.DescribeSelectedLine(message, resolution))
            ImGui.TextWrapped($"    {detail}");
    }

    private string DescribeCinematicMessageReference(uint key, uint preferredWorldKey)
    {
        if (key == 0) return "null";
        if (key == uint.MaxValue) return "invalid";
        string? name = ResolveCinematicObjectName(key, preferredWorldKey);
        return name == null ? $"{key:X8} (unresolved)" : $"{name} ({key:X8})";
    }

    private CinematicScriptOwner? ResolveLinkedOwner(uint key, uint preferredWorldKey) =>
        Document == null ? null : CinematicScriptOwnerResolver.Resolve(key, preferredWorldKey,
            Document.CinematicScriptOwners.Values);

    private string? ResolveCinematicObjectName(uint key, uint preferredWorldKey)
    {
        if (Document == null) return null;
        var localWorld = Document.Worlds.FirstOrDefault(world => world.Key == preferredWorldKey);
        var localObject = localWorld?.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == key);
        if (localObject != null) return localObject.Name;
        var localOwner = ResolveLinkedOwner(key, preferredWorldKey);
        if (localOwner != null) return localOwner.Name;

        var otherObjects = Document.Worlds.SelectMany(world => world.Objects)
            .Where(sceneObject => sceneObject.GaoKey == key).ToArray();
        if (otherObjects.Length == 1) return otherObjects[0].Name;
        var otherOwners = Document.CinematicScriptOwners.Values.Where(owner => owner.OwnerKey == key).ToArray();
        return otherOwners.Length == 1 ? otherOwners[0].Name : null;
    }

    private static string DescribeDialogueLine(CinematicDialogueLine line)
    {
        var details = new List<string>();
        if (line.TextBankKey != 0 || line.TextId != 0)
            details.Add($"text {line.TextBankKey:X8}:{line.TextId}");
        if (line.SoundId is > 0)
            details.Add(line.SoundVariantMax is > 0
                ? $"associated sound {line.SoundId} (ai_son_differents {line.SoundVariantMax})"
                : $"associated sound {line.SoundId}");
        if (line.TextBox is { } textBox && textBox != 0) details.Add($"box {textBox:G}");
        if (line.Volume is { } volume && volume != 0) details.Add($"volume {volume:G}");
        if (line.Near is { } near && near != 0) details.Add($"near {near:G}");
        if (line.Far is { } far && far != 0) details.Add($"far {far:G}");
        return $"Slot {line.Slot}: {string.Join(" · ", details)}";
    }
}
