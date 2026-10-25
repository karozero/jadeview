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
            else if (resource.SerializedFunction is { } function)
            {
                int previewLength = Math.Min(function.LocalStringConstants.Length, ScriptConstantPreviewByteLimit);
                string constantsHex = Convert.ToHexString(function.LocalStringConstants.AsSpan(0, previewLength));
                string suffix = previewLength < function.LocalStringConstants.Length ? "…" : "";
                ImGui.TextWrapped($"Serialized node body: {function.Nodes.Length} nodes · debug nodes: {function.DebugNodes.Length} · stack offsets: {function.StackOffsetCount}");
                ImGui.TextWrapped($"Local constants: {function.LocalStringConstants.Length} bytes · local variables: {function.LocalVariables.Length} · trailing bytes: {function.TrailingBytes.Length}");
                if (function.LocalStringConstants.Length > 0)
                    ImGui.TextDisabled($"Local constant bytes: {constantsHex}{suffix}");
                if (ImGui.TreeNode($"Decoded nodes ({function.Nodes.Length})"))
                {
                    foreach (PcAiInstruction node in function.Nodes.Take(64))
                    {
                        string symbol = string.IsNullOrWhiteSpace(node.SymbolName)
                            ? node.CategoryName
                            : $"{node.CategoryName} · {node.SymbolName}";
                        ImGui.BulletText($"{node.Index}: {symbol} · value 0x{unchecked((uint)node.Value):X8} · id 0x{node.EncodedValue2:X4} · flags 0x{node.Flags:X2}");
                        if (node.BranchDescription is { Length: > 0 } branch)
                            ImGui.TextDisabled(branch);
                    }
                    if (function.Nodes.Length > 64)
                        ImGui.TextDisabled($"Showing 64 of {function.Nodes.Length} nodes.");
                    ImGui.TreePop();
                }
                ImGui.TextDisabled("Decoded serialized nodes only. Stack execution, branch values, callback scheduling and side effects are not simulated.");
            }
            else if (!string.IsNullOrWhiteSpace(resource.Diagnostic))
                ImGui.TextWrapped(resource.Diagnostic);

            ImGui.TreePop();
        }
        ImGui.TreePop();
    }
}
