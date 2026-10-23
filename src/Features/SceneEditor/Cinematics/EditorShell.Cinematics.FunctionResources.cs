using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private const int ScriptConstantPreviewByteLimit = 128;

    private static void DrawCinematicScriptFunctionResources(CinematicScriptOwner owner)
    {
        if (owner.FunctionResources.Length == 0
            || !ImGui.TreeNode("Referenced .ofc payload metadata")) return;

        foreach (var resource in owner.FunctionResources)
        {
            string label = $"{resource.Name} · {resource.FunctionKey:X8} · {resource.Status}";
            if (!ImGui.TreeNode($"{label}##ofc-{resource.WorldKey:X8}-{resource.FunctionKey:X8}")) continue;

            ImGui.TextWrapped($"Source world: {resource.WorldKey:X8} · payload: {resource.PayloadSize} bytes");
            string slots = string.Join(", ", (owner.CallbackSlots ?? [])
                .Where(slot => slot.FunctionKey == resource.FunctionKey).Select(slot => slot.Index));
            ImGui.TextDisabled(slots.Length > 0 ? $"Configured callback slots: {slots}"
                : "No configured callback slot references this function; it may be called indirectly.");

            if (resource.Metadata is { } metadata)
            {
                int previewLength = Math.Min(metadata.LocalConstantBytes.Length, ScriptConstantPreviewByteLimit);
                string constantsHex = Convert.ToHexString(metadata.LocalConstantBytes.AsSpan(0, previewLength));
                string suffix = previewLength < metadata.LocalConstantBytes.Length ? "…" : "";
                ImGui.BulletText($"Stack offsets: {metadata.StackOffsetCount} · declared node bytes: {metadata.DeclaredNodeByteSize}");
                ImGui.TextWrapped($"Local constants: {metadata.LocalConstantBytes.Length} bytes · hex {constantsHex}{suffix}");
                ImGui.TextDisabled("Compact-layout candidate only. The node body is absent; native registry resolution and execution remain unproven.");
            }
            else if (!string.IsNullOrWhiteSpace(resource.Diagnostic))
                ImGui.TextWrapped(resource.Diagnostic);

            ImGui.TreePop();
        }
        ImGui.TreePop();
    }
}
