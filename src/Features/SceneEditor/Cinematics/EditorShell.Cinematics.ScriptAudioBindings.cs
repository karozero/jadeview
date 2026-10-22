using System.Globalization;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptAudioBindings(CinematicScriptOwner owner)
    {
        bool hasSoundSlots = owner.Variables.Any(variable => variable.Name.StartsWith("des_i_SndMdf_",
            StringComparison.OrdinalIgnoreCase));
        if (!hasSoundSlots || Document is not { } document) return;

        CinematicScriptAudioBinding[] bindings = CinematicScriptAudioBindingResolver.Resolve(owner, document);
        EditorUi.DrawCategoryTitle("Script audio modifier bindings");
        foreach (CinematicScriptAudioBinding binding in bindings)
        {
            string role = CinematicScriptAudioBindingResolver.DescribeRole(binding.VariableName);
            if (binding.Modifiers.Length > 0 && binding.ModifierId is { } modifierId)
            {
                if (modifierId == -1)
                {
                    string soundKeys = string.Join(", ", binding.Modifiers.Select(modifier =>
                        $"{modifier.SoundKey:X8}"));
                    ImGui.BulletText($"{role}: all owner sound modifiers → sound {soundKeys} · "
                        + binding.Status);
                }
                else
                {
                    CinematicSoundModifier modifier = binding.Modifiers[0];
                    string volume = modifier.Volume.ToString("0.###", CultureInfo.InvariantCulture);
                    string delay = modifier.Delay.ToString("0.###", CultureInfo.InvariantCulture);
                    ImGui.BulletText($"{role}: modifier {modifierId} → sound {modifier.SoundKey:X8} · "
                        + $"volume {volume} · delay {delay}s · loop {(modifier.Loop ? "on" : "off")}");
                }
            }
            else
            {
                string displayedModifierId = binding.ModifierId switch
                {
                    -1 => "all modifiers",
                    { } value => value.ToString(CultureInfo.InvariantCulture),
                    _ => "?"
                };
                ImGui.BulletText($"{role}: modifier {displayedModifierId} · {binding.Status}");
            }
        }
        ImGui.TextDisabled("Bindings match the owner's effective OVA modifier ID to its type-8 GAO modifier in the same world. This identifies configured sound assets, not live playback.");
    }
}
