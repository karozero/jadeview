using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawDiagnosticsTab()
    {
        DrawCategoryTitle("Scene and asset diagnostics", "Loading and decoding diagnostics for the current scene and its resources. Check these messages when an asset is missing or looks incorrect.");
        ImGui.Separator();

        if (Document != null)
        {
            DrawCategoryTitle("Source archive");
            ImGui.TextUnformatted($"Base BF: {Document.BaseBfPath}");
            ImGui.TextUnformatted($"SHA-256 BF: {Document.BaseBfSha256}");
            ImGui.TextUnformatted($"Container: {Document.ContainerName}");
            ImGui.Spacing();

            string text = $"Base BF: {Document.BaseBfPath}\nSHA-256: {Document.BaseBfSha256}\nContainer: {Document.ContainerName}\n\n" +
                string.Join("\n", Resolver.TextureLoaderStatus.Select(status => $"{status.Key}: {status.Value}")) + "\n" +
                string.Join("\n", Document.Diagnostics);
            DrawCategoryTitle("Loading log");
            if (ImGui.Button("Copy all")) ImGui.SetClipboardText(text);
            ImGui.InputTextMultiline("##DiagLog", ref text, (uint)Math.Max(4096, System.Text.Encoding.UTF8.GetByteCount(text) + 1),
                new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);

        }
        else
        {
            ImGui.TextDisabled("No scene document loaded.");
        }
    }
}
